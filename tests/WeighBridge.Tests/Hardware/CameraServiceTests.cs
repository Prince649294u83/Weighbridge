using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Configuration;
using WeighBridge.Domain.Enums;
using WeighBridge.Hardware.Cameras;
using WeighBridge.Tests.Infrastructure;
using Xunit;

namespace WeighBridge.Tests.Hardware;

public sealed class CameraServiceTests
{
    [Fact]
    public async Task CameraService_Connects_And_Captures_Valid_Jpeg_File()
    {
        using var tempRoot = new TempDataRoot();
        var cameraOptions = Options.Create(new CameraOptions
        {
            Enabled = true,
            Devices = [new CameraDeviceOptions { Name = "Front Camera", Enabled = true }],
        });

        var cameraService = new CameraService(cameraOptions, tempRoot.Paths, NullLogger<CameraService>.Instance);

        bool connected = await cameraService.ConnectAsync();
        Assert.True(connected);
        Assert.Equal(ConnectionState.Connected, cameraService.State);

        var result = await cameraService.CaptureSnapshotAsync("Front Camera", "FirstWeight", "WB-000099");

        Assert.True(result.Success);
        Assert.NotNull(result.FilePath);
        Assert.Equal(CameraSource.Simulator, result.Source);
        Assert.True(result.FileSizeBytes > 0);
        Assert.NotNull(result.Checksum);

        string fullPath = Path.Combine(tempRoot.Paths.CaptureDirectory, result.FilePath);
        Assert.True(File.Exists(fullPath));

        byte[] fileBytes = await File.ReadAllBytesAsync(fullPath);
        Assert.Equal(0xFF, fileBytes[0]);
        Assert.Equal(0xD8, fileBytes[1]); // JPEG SOI
        Assert.Equal(0xFF, fileBytes[^2]);
        Assert.Equal(0xD9, fileBytes[^1]); // JPEG EOI
    }

    [Fact]
    public async Task CameraService_Fails_Gracefully_When_Disabled()
    {
        using var tempRoot = new TempDataRoot();
        var cameraOptions = Options.Create(new CameraOptions
        {
            Enabled = false,
        });

        var cameraService = new CameraService(cameraOptions, tempRoot.Paths, NullLogger<CameraService>.Instance);

        var result = await cameraService.CaptureSnapshotAsync("Front Camera", "FirstWeight", "WB-000099");

        Assert.False(result.Success);
        Assert.Equal(CameraSource.Disabled, result.Source);
        Assert.Null(result.FilePath);
    }
}
