using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Application;
using WeighBridge.Core.Configuration;
using WeighBridge.Domain.Enums;

namespace WeighBridge.Hardware.Cameras;

/// <summary>
/// Camera coordinator supporting multi-camera configuration (Camera 1 / Camera 2),
/// file-safe disk storage under <see cref="IApplicationPaths.CaptureDirectory"/>,
/// and deterministic test captures with explicit <see cref="CameraSource"/> provenance.
/// </summary>
public sealed class CameraService : ICameraService, IDisposable
{
    private readonly CameraOptions _options;
    private readonly IApplicationPaths _paths;
    private readonly ILogger<CameraService> _logger;

    private ConnectionState _state = ConnectionState.Disconnected;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CameraService(
        IOptions<CameraOptions> options,
        IApplicationPaths paths,
        ILogger<CameraService> logger)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public ConnectionState State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            StateChanged?.Invoke(this, value);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ConfiguredDevices =>
        _options.Devices.Where(d => d.Enabled).Select(d => d.Name).ToList();

    /// <inheritdoc />
    public string Name => "Cameras";

    /// <inheritdoc />
    public event EventHandler<ConnectionState>? StateChanged;

    /// <inheritdoc />
    public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            State = ConnectionState.Disabled;
            _logger.LogInformation("Camera subsystem is disabled in configuration");
            return Task.FromResult(false);
        }

        State = ConnectionState.Connected;
        _logger.LogInformation("Camera subsystem connected with {Count} active device(s)", ConfiguredDevices.Count);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task DisconnectAsync()
    {
        State = _options.Enabled ? ConnectionState.Disconnected : ConnectionState.Disabled;
        _logger.LogInformation("Camera subsystem disconnected");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<string?> CaptureAsync(string deviceName, CancellationToken cancellationToken = default)
    {
        var result = await CaptureSnapshotAsync(deviceName, "Manual", "WB-MANUAL", cancellationToken).ConfigureAwait(false);
        return result.Success ? result.FilePath : null;
    }

    /// <inheritdoc />
    public async Task<CameraCaptureResult> CaptureSnapshotAsync(
        string deviceName,
        string stage,
        string slipNumber,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return CameraCaptureResult.Failed("Camera subsystem is disabled in configuration.", CameraSource.Disabled);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _paths.EnsureCreated();

            string safeSlip = string.IsNullOrWhiteSpace(slipNumber)
                ? "DRAFT"
                : slipNumber.Replace('/', '-').Replace('\\', '-').Replace(':', '-').Trim();

            string safeDevice = string.IsNullOrWhiteSpace(deviceName)
                ? "Camera1"
                : deviceName.Replace(' ', '_').Replace('/', '-').Replace('\\', '-').Trim();

            string safeStage = string.IsNullOrWhiteSpace(stage) ? "Capture" : stage.Trim();
            string shortId = Guid.NewGuid().ToString("N")[..8];
            string fileName = $"{safeSlip}_{safeDevice}_{safeStage}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{shortId}.jpg";
            string fullPath = Path.Combine(_paths.CaptureDirectory, fileName);

            // Generate deterministic test pattern JPEG for simulated device
            byte[] jpegBytes = CreateTestJpegPattern(safeDevice, safeSlip, safeStage);

            await File.WriteAllBytesAsync(fullPath, jpegBytes, cancellationToken).ConfigureAwait(false);

            string hash = Convert.ToHexString(SHA256.HashData(jpegBytes));
            long fileSize = jpegBytes.Length;

            _logger.LogInformation(
                "Captured snapshot for {Device} ({Stage}, Slip: {Slip}) -> {FileName} ({Bytes} bytes)",
                deviceName,
                stage,
                slipNumber,
                fileName,
                fileSize);

            // Return relative file path for portability across machines/data roots
            return CameraCaptureResult.Succeeded(fileName, CameraSource.Simulator, fileSize, hash);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture snapshot from camera {Device}", deviceName);
            return CameraCaptureResult.Failed($"Camera capture failed: {ex.Message}", CameraSource.Simulator);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task<HealthResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(HealthResult.Disabled("Cameras disabled in configuration"));
        }

        return Task.FromResult(State switch
        {
            // Not "Cameras online". Nothing is online: CreateTestJpegPattern below generates
            // every image this service files against a weighment, and health text that reads
            // like a working camera is how a generated photograph gets taken for evidence.
            ConnectionState.Connected => HealthResult.Degraded(
                $"Cameras enabled with generated images ({ConfiguredDevices.Count} configured device(s)); no capture device is implemented"),
            _ => HealthResult.Degraded("Cameras disconnected"),
        });
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Dispose();
    }

    /// <summary>
    /// Generates a valid baseline JFIF JPEG (64×64, grayscale, all-grey pixels) carrying
    /// its provenance in the COM (comment) marker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A real decoder must be able to render this file — it stands in for camera evidence
    /// on an operator's screen and in third-party viewers, so "bytes that happen to start
    /// like a JPEG" is not good enough. The earlier revision emitted a DC Huffman table
    /// but never the AC table the SOS header points at, and filled the scan with raw zero
    /// bytes that are not valid entropy-coded data; most strict decoders refused it.
    /// </para>
    /// <para>
    /// Every MCU below encodes as: DC symbol (category 0, code "00") followed by the AC
    /// end-of-block symbol (code "00") — four bits per block, 64 blocks, 32 bytes of
    /// entropy data with no padding and nothing needing byte-stuffing.
    /// </para>
    /// </remarks>
    private static byte[] CreateTestJpegPattern(string device, string slip, string stage)
    {
        string commentText = $"WeighBridge Modern | Device: {device} | Slip: {slip} | Stage: {stage} | Date: {DateTime.UtcNow:O}";
        byte[] commentBytes = Encoding.UTF8.GetBytes(commentText);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // SOI (Start of Image)
        writer.Write((byte)0xFF);
        writer.Write((byte)0xD8);

        // APP0 (JFIF standard header, version 1.01, density 72×72, no thumbnail)
        writer.Write((byte)0xFF);
        writer.Write((byte)0xE0);
        WriteBigEndianUInt16(writer, 16);
        writer.Write(Encoding.ASCII.GetBytes("JFIF\0"));
        writer.Write((byte)0x01); // Version 1.01
        writer.Write((byte)0x01);
        writer.Write((byte)0x00); // Units: none
        WriteBigEndianUInt16(writer, 72); // X density
        WriteBigEndianUInt16(writer, 72); // Y density
        writer.Write((byte)0x00); // Thumbnail width 0
        writer.Write((byte)0x00); // Thumbnail height 0

        // COM (Comment marker) — carries the metadata a test or an auditor reads back.
        writer.Write((byte)0xFF);
        writer.Write((byte)0xFE);
        WriteBigEndianUInt16(writer, (ushort)(commentBytes.Length + 2));
        writer.Write(commentBytes);

        // DQT (quantization table 0, 8-bit, all 16s — flat quantization)
        writer.Write((byte)0xFF);
        writer.Write((byte)0xDB);
        WriteBigEndianUInt16(writer, 67); // Length = 2 + 1 + 64
        writer.Write((byte)0x00); // Table ID 0, 8-bit precision
        for (int i = 0; i < 64; i++)
        {
            writer.Write((byte)16);
        }

        // SOF0 (baseline DCT, 64×64, one grayscale component using quantization table 0).
        // Per the JPEG spec the segment length counts itself and equals 8 + 3 per component.
        writer.Write((byte)0xFF);
        writer.Write((byte)0xC0);
        WriteBigEndianUInt16(writer, (ushort)(8 + 3 * 1));
        writer.Write((byte)0x08); // Precision = 8 bit
        WriteBigEndianUInt16(writer, 64); // Height
        WriteBigEndianUInt16(writer, 64); // Width
        writer.Write((byte)0x01); // Component count
        writer.Write((byte)0x01); // Component ID 1
        writer.Write((byte)0x11); // Sampling 1×1
        writer.Write((byte)0x00); // Quantization table 0

        // DHT — DC table 0. One code of length two ("00"), mapping to symbol 0x00,
        // which is DC category zero: a differential of exactly zero.
        WriteHuffmanTable(writer, dcTable: true, tableId: 0,
            bits: [0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            values: [0x00]);

        // DHT — AC table 0. The SOS header below points both tables here, so this one has
        // to exist. One code of length two ("00"), mapping to 0x00, end-of-block.
        WriteHuffmanTable(writer, dcTable: false, tableId: 0,
            bits: [0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            values: [0x00]);

        // SOS (Start of Scan): one component, DC table 0 + AC table 0, full spectrum.
        writer.Write((byte)0xFF);
        writer.Write((byte)0xDA);
        WriteBigEndianUInt16(writer, 8); // Length = 6 + 2 per component
        writer.Write((byte)0x01); // Component count
        writer.Write((byte)0x01); // Component ID 1
        writer.Write((byte)0x00); // DC table 0 / AC table 0
        writer.Write((byte)0x00); // Spectral selection start
        writer.Write((byte)0x3F); // Spectral selection end
        writer.Write((byte)0x00); // Successive approximation

        // Entropy-coded data. 8×8 = 64 MCUs; each contributes four bits ("00" DC + "00"
        // EOB), so the whole scan is 256 zero bits — 32 clean bytes, no padding, no 0xFF.
        for (int i = 0; i < 32; i++)
        {
            writer.Write((byte)0x00);
        }

        // EOI (End of Image)
        writer.Write((byte)0xFF);
        writer.Write((byte)0xD9);

        return ms.ToArray();
    }

    private static void WriteBigEndianUInt16(BinaryWriter writer, ushort value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)(value & 0xFF));
    }

    private static void WriteHuffmanTable(
        BinaryWriter writer,
        bool dcTable,
        int tableId,
        byte[] bits,
        byte[] values)
    {
        writer.Write((byte)0xFF);
        writer.Write((byte)0xC4);
        WriteBigEndianUInt16(writer, (ushort)(2 + 1 + bits.Length + values.Length));
        writer.Write((byte)((dcTable ? 0x00 : 0x10) | tableId));
        foreach (var bit in bits)
        {
            writer.Write(bit);
        }
        foreach (var value in values)
        {
            writer.Write(value);
        }
    }
}
