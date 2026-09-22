using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Domain.Enums;

namespace WeighBridge.Hardware.WeightIndicators;

/// <summary>
/// Placeholder weight indicator that never connects.
/// </summary>
/// <remarks>
/// Module 0.1 ships no serial protocol. This implementation lets the shell display an
/// accurate indicator state — <c>Disabled</c> when the hardware is switched off in
/// configuration, otherwise <c>Disconnected</c> — and gives later modules a drop-in
/// replacement point: only the DI registration changes.
/// </remarks>
public sealed class PlaceholderWeightIndicatorService(
    IOptions<HardwareOptions> options,
    ILogger<PlaceholderWeightIndicatorService> logger) : IWeightIndicatorService, IDisposable
{
    private readonly WeightIndicatorOptions _options = options.Value.WeightIndicator;
    private readonly ILogger<PlaceholderWeightIndicatorService> _logger = logger;

    private ConnectionState _state = ConnectionState.Unknown;

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
    public WeightReading CurrentReading => WeightReading.Empty;

    /// <inheritdoc />
    public string Name => "Weight Indicator";

    /// <summary>
    /// Never raised by the placeholder — no frames are ever decoded. Explicit
    /// accessors keep the public contract identical to the real implementation
    /// without declaring a backing field that is never invoked.
    /// </summary>
    public event EventHandler<WeightReading>? ReadingReceived
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public event EventHandler<ConnectionState>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<DiagnosticDataChunk>? RawTelemetryReceived
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            State = ConnectionState.Disabled;
            _logger.LogInformation("Weight indicator is disabled in configuration");
            return Task.FromResult(false);
        }

        State = ConnectionState.Disconnected;

        _logger.LogWarning(
            "Weight indicator support is not implemented in this build; {Port} at {BaudRate} baud was not opened",
            _options.PortName,
            _options.BaudRate);

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task DisconnectAsync()
    {
        State = _options.Enabled ? ConnectionState.Disconnected : ConnectionState.Disabled;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<WeightReading> ReadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(WeightReading.Empty);

    /// <inheritdoc />
    public Task<HealthResult> CheckAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_options.Enabled
            ? HealthResult.Unreachable($"{_options.PortName} · driver not installed in this build")
            : HealthResult.Disabled("Weight indicator disabled in configuration"));

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        // Nothing to release: the placeholder never opens a serial port.
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
