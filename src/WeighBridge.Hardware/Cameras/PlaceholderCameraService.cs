using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Domain.Enums;

namespace WeighBridge.Hardware.Cameras;

/// <summary>
/// Placeholder capture service that reports configured devices but never opens one.
/// </summary>
public sealed class PlaceholderCameraService(
    IOptions<CameraOptions> options,
    ILogger<PlaceholderCameraService> logger) : ICameraService, IDisposable
{
    private readonly CameraOptions _options = options.Value;
    private readonly ILogger<PlaceholderCameraService> _logger = logger;

    /// <inheritdoc />
    public ConnectionState State { get; private set; } = ConnectionState.Unknown;

    /// <inheritdoc />
    public IReadOnlyList<string> ConfiguredDevices =>
        _options.Devices.Where(device => device.Enabled).Select(device => device.Name).ToList();

    /// <inheritdoc />
    public string Name => "Cameras";

    /// <inheritdoc />
    public event EventHandler<ConnectionState>? StateChanged;

    /// <inheritdoc />
    public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        State = _options.Enabled ? ConnectionState.Disconnected : ConnectionState.Disabled;
        StateChanged?.Invoke(this, State);

        if (_options.Enabled)
        {
            _logger.LogWarning("Camera capture is not implemented in this build; {Count} device(s) configured", ConfiguredDevices.Count);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task DisconnectAsync()
    {
        State = _options.Enabled ? ConnectionState.Disconnected : ConnectionState.Disabled;
        StateChanged?.Invoke(this, State);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> CaptureAsync(string deviceName, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Capture requested for {Device} but camera support is not implemented", deviceName);
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<CameraCaptureResult> CaptureSnapshotAsync(
        string deviceName,
        string stage,
        string slipNumber,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Snapshot requested for {Device} ({Stage}, {Slip}) but camera support is not implemented", deviceName, stage, slipNumber);
        return Task.FromResult(CameraCaptureResult.Failed("Camera support not implemented", _options.Enabled ? CameraSource.Physical : CameraSource.Disabled));
    }

    /// <inheritdoc />
    public Task<HealthResult> CheckAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_options.Enabled
            ? HealthResult.Unreachable("Camera driver not installed in this build")
            : HealthResult.Disabled("Cameras disabled in configuration"));

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
