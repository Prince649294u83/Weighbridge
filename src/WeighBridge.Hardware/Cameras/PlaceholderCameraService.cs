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
    ILogger<PlaceholderCameraService> logger) : ICameraService
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
    public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        State = _options.Enabled ? ConnectionState.Disconnected : ConnectionState.Disabled;

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
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> CaptureAsync(string deviceName, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Capture requested for {Device} but camera support is not implemented", deviceName);
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<HealthResult> CheckAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_options.Enabled
            ? HealthResult.Unreachable("Camera driver not installed in this build")
            : HealthResult.Disabled("Cameras disabled in configuration"));

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
