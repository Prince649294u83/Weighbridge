using Microsoft.EntityFrameworkCore;
using WeighBridge.Core.Abstractions;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Masters;
using WeighBridge.Domain.Weighments;
using Xunit;

namespace WeighBridge.Tests.Masters;

public sealed class VehicleEntryMasterIntegrationTests
{
    [Fact]
    public async Task VehicleEntry_WithMasterEntities_PersistsForeignKeysAndSnapshots()
    {
        using var harness = new MasterHarness();

        // 1. Setup Masters
        var vt = await harness.VehicleTypeService.CreateAsync(new CreateVehicleTypeRequest("12-WHEELER"));
        var vehicle = await harness.VehicleService.CreateAsync(new CreateVehicleRequest("MH12PQ5555", vt.Id, TareWeightKg: 11200m));
        var party = await harness.PartyService.CreateAsync(new CreatePartyRequest("JSW Steel", Code: "JSW"));
        var material = await harness.MaterialService.CreateAsync(new CreateMaterialRequest("TMT Bars", Code: "TMT"));

        // 2. Open Weighment with Master Links
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

        Assert.Equal("MH12PQ5555", weighment.VehicleNumber);
        Assert.Equal("JSW Steel", weighment.PartyName);
        Assert.Equal("TMT Bars", weighment.MaterialName);
        Assert.Equal("12-WHEELER", weighment.VehicleTypeName);
        Assert.Equal(vehicle.Id, weighment.VehicleId);
        Assert.Equal(party.Id, weighment.PartyId);
        Assert.Equal(material.Id, weighment.MaterialId);
        Assert.Equal(vt.Id, weighment.VehicleTypeId);

        // 3. Complete the weighment
        await harness.WeighmentService.RecordFirstWeightAsync(weighment.Id, 25000m, WeightSource.Indicator);
        await harness.WeighmentService.RecordSecondWeightAsync(weighment.Id, 11200m, WeightSource.Indicator);

        // 4. Reload from database
        await using var context = harness.CreateContext();
        var reloaded = await context.Set<Weighment>().FirstOrDefaultAsync(w => w.Id == weighment.Id);

        Assert.NotNull(reloaded);
        Assert.Equal(WeighmentStatus.Completed, reloaded.Status);
        Assert.Equal("MH12PQ5555", reloaded.VehicleNumber);
        Assert.Equal("JSW Steel", reloaded.PartyName);
        Assert.Equal(vehicle.Id, reloaded.VehicleId);
        Assert.Equal(party.Id, reloaded.PartyId);
        Assert.Equal(material.Id, reloaded.MaterialId);
        Assert.Equal(vt.Id, reloaded.VehicleTypeId);
    }

    [Fact]
    public async Task MasterDeactivation_PreservesHistoricalWeighmentSnapshots()
    {
        using var harness = new MasterHarness();

        // 1. Setup Master and Weighment
        var party = await harness.PartyService.CreateAsync(new CreatePartyRequest("Old Mining Corp"));
        var weighment = await harness.WeighmentService.CreateAsync(new NewWeighment
        {
            VehicleNumber = "KA05MN8888",
            Mode = WeighmentMode.GrossFirst,
            PartyName = party.Name,
            PartyId = party.Id,
        });

        await harness.WeighmentService.RecordFirstWeightAsync(weighment.Id, 30000m, WeightSource.Manual);
        await harness.WeighmentService.RecordSecondWeightAsync(weighment.Id, 12000m, WeightSource.Manual);

        // 2. Deactivate the Master party
        await harness.PartyService.DeactivateAsync(party.Id);

        // 3. Check that active party list no longer contains the party
        var activeParties = await harness.PartyService.GetAllAsync(includeInactive: false);
        Assert.DoesNotContain(activeParties, p => p.Id == party.Id);

        // 4. Verify historical weighment snapshot is completely intact and accessible
        await using var context = harness.CreateContext();
        var history = await context.Set<Weighment>().FirstOrDefaultAsync(w => w.Id == weighment.Id);

        Assert.NotNull(history);
        Assert.Equal(WeighmentStatus.Completed, history.Status);
        Assert.Equal("Old Mining Corp", history.PartyName);
        Assert.Equal(party.Id, history.PartyId);
    }

    [Fact]
    public async Task DirectOperatorEntry_PreservesSnapshotsWithNullForeignKeys()
    {
        using var harness = new MasterHarness();

        var weighment = await harness.WeighmentService.CreateAsync(new NewWeighment
        {
            VehicleNumber = "UP32ZZ0001",
            Mode = WeighmentMode.TareFirst,
            PartyName = "Adhoc Local Farmer",
            MaterialName = "Wheat Grains",
            VehicleId = null,
            PartyId = null,
            MaterialId = null,
            VehicleTypeId = null,
            VehicleTypeName = null,
        });

        Assert.Equal("UP32ZZ0001", weighment.VehicleNumber);
        Assert.Equal("Adhoc Local Farmer", weighment.PartyName);
        Assert.Equal("Wheat Grains", weighment.MaterialName);
        Assert.Null(weighment.VehicleId);
        Assert.Null(weighment.PartyId);
        Assert.Null(weighment.MaterialId);
        Assert.Null(weighment.VehicleTypeId);
        Assert.Null(weighment.VehicleTypeName);
    }
}
