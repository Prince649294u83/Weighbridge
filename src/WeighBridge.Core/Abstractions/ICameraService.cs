using WeighBridge.Domain.Enums;

namespace WeighBridge.Core.Abstractions;

/// <summary>
/// Captures vehicle images at weighment time.
/// </summary>
/// <remarks>
/// Module 0.1 registers a disabled placeholder so the shell can report camera state
/// without any capture driver being installed.
/// </remarks>
public interface ICameraService : IHealthCheck, IAsyncDisposable
{
    /// <summary>Current connection state of the capture subsystem.</summary>
    ConnectionState State { get; }

    /// <summary>Friendly names of the configured devices.</summary>
    IReadOnlyList<string> ConfiguredDevices { get; }

    /// <summary>Opens every enabled device.</summary>
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes every open device.</summary>
    Task DisconnectAsync();

    /// <summary>
    /// Captures a still from <paramref name="deviceName"/> and returns the file path,
    /// or <c>null</c> when capture is unavailable.
    /// </summary>
    Task<string?> CaptureAsync(string deviceName, CancellationToken cancellationToken = default);
}
