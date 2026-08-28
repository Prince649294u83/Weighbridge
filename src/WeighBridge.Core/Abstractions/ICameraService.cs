using WeighBridge.Domain.Enums;

namespace WeighBridge.Core.Abstractions;

/// <summary>
/// Result of a camera capture operation.
/// </summary>
public readonly record struct CameraCaptureResult(
    bool Success,
    string? FilePath,
    CameraSource Source,
    long FileSizeBytes,
    string? Checksum = null,
    string? ErrorMessage = null)
{
    public static CameraCaptureResult Failed(string errorMessage, CameraSource source = CameraSource.Disabled) =>
        new(false, null, source, 0, null, errorMessage);

    public static CameraCaptureResult Succeeded(string filePath, CameraSource source, long sizeBytes, string? checksum = null) =>
        new(true, filePath, source, sizeBytes, checksum, null);
}

/// <summary>
/// Captures vehicle images at weighment time.
/// </summary>
public interface ICameraService : IHealthCheck, IAsyncDisposable
{
    /// <summary>Current connection state of the capture subsystem.</summary>
    ConnectionState State { get; }

    /// <summary>Friendly names of the configured devices.</summary>
    IReadOnlyList<string> ConfiguredDevices { get; }

    /// <summary>Raised when the camera subsystem connection state changes.</summary>
    event EventHandler<ConnectionState>? StateChanged;

    /// <summary>Opens every enabled device.</summary>
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes every open device.</summary>
    Task DisconnectAsync();

    /// <summary>
    /// Captures a still from <paramref name="deviceName"/> and returns the file path,
    /// or <c>null</c> when capture is unavailable.
    /// </summary>
    Task<string?> CaptureAsync(string deviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures a still from <paramref name="deviceName"/> with metadata for a weighment transaction.
    /// </summary>
    Task<CameraCaptureResult> CaptureSnapshotAsync(
        string deviceName,
        string stage,
        string slipNumber,
        CancellationToken cancellationToken = default);
}
