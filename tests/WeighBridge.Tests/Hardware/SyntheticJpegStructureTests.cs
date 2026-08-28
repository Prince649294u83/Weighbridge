using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Configuration;
using WeighBridge.Hardware.Cameras;
using WeighBridge.Tests.Infrastructure;

namespace WeighBridge.Tests.Hardware;

/// <summary>
/// The simulator's "photographs" are evidence on a screen and in third-party viewers, so
/// the generated file must decode as a real JPEG â€” not merely start with plausible bytes.
/// These tests walk every marker segment and enforce the structural rules strict
/// decoders (and the format itself) require.
/// </summary>
public sealed class SyntheticJpegStructureTests
{
    [Fact]
    public async Task CapturedFile_IsAStructurallyValidJpeg()
    {
        var temp = new WeighBridge.Tests.Infrastructure.TempDataRoot();
        try
        {
            var options = Options.Create(new CameraOptions
            {
                Enabled = true,
                Devices =
                {
                    new CameraDeviceOptions { Name = "Camera 1", Enabled = true },
                },
            });

            var service = new CameraService(options, temp.Paths, NullLogger<CameraService>.Instance);

            var result = await service.CaptureSnapshotAsync("Camera 1", "FirstWeight", "WB-000001");

            Assert.True(result.Success, result.ErrorMessage);
            var bytes = await File.ReadAllBytesAsync(Path.Combine(temp.Paths.CaptureDirectory, result.FilePath!));

            Assert.Equal(0xFF, bytes[0]);
            Assert.Equal(0xD8, bytes[1]); // SOI

            bool sawApp0 = false, sawCom = false, sawDqt = false, sawSof0 = false;
            int dhtCount = 0, sosCount = 0;

            var i = 2;
            while (i < bytes.Length - 1)
            {
                Assert.Equal(0xFF, bytes[i]);
                var marker = bytes[i + 1];

                if (marker == 0xD9) // EOI
                {
                    Assert.Equal(bytes.Length - 2, i);
                    break;
                }

                if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
                {
                    // Standalone markers carry no length field.
                    i += 2;
                    continue;
                }

                var length = (bytes[i + 2] << 8) | bytes[i + 3];
                Assert.True(length >= 2, $"Marker 0x{marker:X2} length {length} must be at least 2.");

                switch (marker)
                {
                    case 0xE0: sawApp0 = true; break;
                    case 0xFE: sawCom = true; break;
                    case 0xDB: sawDqt = true; break;
                    case 0xC0:
                        sawSof0 = true;
                        ValidateSof0(bytes, i + 4, length - 2);
                        break;
                    case 0xC4: dhtCount++; break;
                    case 0xDA:
                        sosCount++;
                        ValidateScanData(bytes, i + 2 + length);
                        return;
                }

                i += 2 + length;
            }

            Assert.Multiple(
                () => Assert.True(sawApp0), () => Assert.True(sawCom),
                () => Assert.True(sawDqt), () => Assert.True(sawSof0),
                () => Assert.Equal(2, dhtCount),   // DC *and* AC tables â€” one alone cannot decode
                () => Assert.Equal(1, sosCount));
        }
        finally
        {
            temp.Dispose();
        }
    }

    private static void ValidateSof0(byte[] bytes, int start, int length)
    {
        Assert.Equal(8, bytes[start]);                       // 8-bit precision
        var height = (bytes[start + 1] << 8) | bytes[start + 2];
        var width = (bytes[start + 3] << 8) | bytes[start + 4];
        Assert.Equal(64, height);
        Assert.Equal(64, width);
        Assert.Equal(1, bytes[start + 5]);                   // component count
        // Payload after the two length bytes: precision + height + width + count + 3 per
        // component = 6 + 3N.
        Assert.Equal(6 + 3 * bytes[start + 5], length);
    }

    private static void ValidateScanData(byte[] bytes, int start)
    {
        // Every MCU contributes two two-bit codes ("00" DC category-0 + "00" AC end-of-block):
        // 64 MCUs Ã— 4 bits = exactly 32 zero bytes, then EOI. No padding, no 0xFF byte that
        // would require stuffing.
        const int expectedEntropyBytes = 32;
        var entropyEnd = bytes.Length - 2;

        Assert.Equal(expectedEntropyBytes, entropyEnd - start);
        for (var i = start; i < entropyEnd; i++)
        {
            Assert.Equal(0x00, bytes[i]);
        }

        Assert.Equal(0xFF, bytes[^2]);
        Assert.Equal(0xD9, bytes[^1]);
    }
}
