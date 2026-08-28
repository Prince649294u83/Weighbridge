using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Masters;
using WeighBridge.Domain.Weighments;
using WeighBridge.Hardware.Cameras;
using WeighBridge.Hardware.WeightIndicators;
using WeighBridge.Tests.Infrastructure;
using WeighBridge.Tests.Masters;
using Xunit;

namespace WeighBridge.Tests.Hardware;

public sealed class VehicleEntryHardwareIntegrationTests
{
    [Fact]
    public async Task VehicleEntry_Hardware_Integration_Full_Workflow_With_Camera()
    {
        using var harness = new MasterHarness();
        using var tempRoot = new TempDataRoot();

        // 1. Setup Masters
        var vt = await harness.VehicleTypeService.CreateAsync(new CreateVehicleTypeRequest("10-WHEELER"));
        var vehicle = await harness.VehicleService.CreateAsync(new CreateVehicleRequest("MH14GH9999", vt.Id, TareWeightKg: 10500m));
        var party = await harness.PartyService.CreateAsync(new CreatePartyRequest("Tata Steel", Code: "TATA"));
        var material = await harness.MaterialService.CreateAsync(new CreateMaterialRequest("Coal", Code: "COAL"));

        // 2. Setup Hardware Simulator & Camera
        var hardwareOptions = Options.Create(new HardwareOptions
        {
            WeightIndicator = new WeightIndicatorOptions
            {
                Enabled = true,
                DriverType = "Simulator",
                PollIntervalMilliseconds = 50,
                Unit = "kg",
            },
        });

        var simulator = new WeightIndicatorSimulator(hardwareOptions, NullLogger<WeightIndicatorSimulator>.Instance);
        await simulator.ConnectAsync();

        var cameraOptions = Options.Create(new CameraOptions
        {
            Enabled = true,
            Devices = [new CameraDeviceOptions { Name = "Front Camera", Enabled = true }],
        });

        var cameraService = new CameraService(cameraOptions, tempRoot.Paths, NullLogger<CameraService>.Instance);
        await cameraService.ConnectAsync();

        // 3. Open Weighment
        var weighment = await harness.WeighmentService.CreateAsync(new NewWeighment
        {
            VehicleNumber = vehicle.VehicleNumber,
            Mode = WeighmentMode.GrossFirst,
            PartyName = party.Name,
            MaterialName = material.Name,
            VehicleId = vehicle.Id,
            PartyId = party.Id,
            MaterialId = material.Id,
            VehicleTypeId = vt.Id,
            VehicleTypeName = vt.TypeName,
        });

        Assert.NotNull(weighment);
        Assert.Equal("MH14GH9999", weighment.VehicleNumber);

        // 4. Simulate gross weight
        simulator.SetWeight(35400m, isStable: true);
        var reading1 = await simulator.ReadAsync();
        Assert.True(reading1.IsStable);
        Assert.Equal(35400m, reading1.Value);
        Assert.Equal(WeightSource.Simulator, reading1.Source);

        var firstResult = await harness.WeighmentService.RecordFirstWeightAsync(weighment.Id, reading1.Value, reading1.Source);
        Assert.Equal(35400m, firstResult.FirstWeight?.Kilograms);
        Assert.Equal(WeightSource.Simulator, firstResult.FirstWeight?.Source);

        // Capture front camera snapshot for first weight
        var snap1 = await cameraService.CaptureSnapshotAsync("Front Camera", "FirstWeight", weighment.SlipNumber);
        Assert.True(snap1.Success);
        Assert.NotNull(snap1.FilePath);
        Assert.Equal(CameraSource.Simulator, snap1.Source);

        await harness.WeighmentService.AttachImageAsync(
            weighment.Id,
            "Front Camera",
            "FirstWeight",
            snap1.Source,
            snap1.FilePath,
            DateTime.UtcNow,
            snap1.FileSizeBytes,
            snap1.Checksum);

        var imagesAfterFirst = await harness.WeighmentService.GetImagesAsync(weighment.Id);
        Assert.Single(imagesAfterFirst);
        Assert.Equal("Front Camera", imagesAfterFirst[0].CameraName);
        Assert.Equal("FirstWeight", imagesAfterFirst[0].Stage);
        Assert.Equal(CameraSource.Simulator, imagesAfterFirst[0].Source);

        // 5. Simulate return vehicle with tare weight
        simulator.SetWeight(10500m, isStable: true);
        var reading2 = await simulator.ReadAsync();
        Assert.True(reading2.IsStable);
        Assert.Equal(10500m, reading2.Value);
        Assert.Equal(WeightSource.Simulator, reading2.Source);

        var secondResult = await harness.WeighmentService.RecordSecondWeightAsync(weighment.Id, reading2.Value, reading2.Source);
        Assert.Equal(WeighmentStatus.Completed, secondResult.Status);
        Assert.Equal(35400m, secondResult.Gross?.Kilograms);
        Assert.Equal(10500m, secondResult.Tare?.Kilograms);
        Assert.Equal(24900m, secondResult.NetWeightKg);
        Assert.Equal(WeightSource.Simulator, secondResult.SecondWeight?.Source);

        // Capture front camera snapshot for second weight
        var snap2 = await cameraService.CaptureSnapshotAsync("Front Camera", "SecondWeight", weighment.SlipNumber);
        Assert.True(snap2.Success);

        await harness.WeighmentService.AttachImageAsync(
            weighment.Id,
            "Front Camera",
            "SecondWeight",
            snap2.Source,
            snap2.FilePath!,
            DateTime.UtcNow,
            snap2.FileSizeBytes,
            snap2.Checksum);

        var imagesAfterSecond = await harness.WeighmentService.GetImagesAsync(weighment.Id);
        Assert.Equal(2, imagesAfterSecond.Count);

        // 6. Reload directly from Database Context
        await using var context = harness.CreateContext();
        var reloaded = await context.Set<Weighment>()
            .Include(w => w.Images)
            .FirstOrDefaultAsync(w => w.Id == weighment.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(WeighmentStatus.Completed, reloaded.Status);
        Assert.Equal(WeightSource.Simulator, reloaded.FirstWeight?.Source);
        Assert.Equal(WeightSource.Simulator, reloaded.SecondWeight?.Source);
        Assert.Equal(24900m, reloaded.NetWeightKg);
        Assert.Equal(2, reloaded.Images.Count);
        Assert.All(reloaded.Images, img => Assert.Equal(CameraSource.Simulator, img.Source));

        await simulator.DisposeAsync();
        await cameraService.DisposeAsync();
    }
}
