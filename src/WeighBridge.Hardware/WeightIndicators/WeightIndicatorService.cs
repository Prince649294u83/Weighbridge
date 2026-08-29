using System.Buffers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Domain.Enums;

namespace WeighBridge.Hardware.WeightIndicators;

/// <summary>
/// Production weight indicator service that coordinates serial transport, frame extraction,
/// protocol parsing, and rolling-window stability detection.
/// </summary>
public sealed class WeightIndicatorService : IWeightIndicatorService, IDisposable
{
    private readonly WeightIndicatorOptions _options;
    private readonly ISerialPortTransport _transport;
    private readonly IFrameExtractor _frameExtractor;
    private readonly IIndicatorProtocolParser _protocolParser;
    private readonly IWeightDecoder _weightDecoder;
    private readonly StabilityDetector _stabilityDetector;
    private readonly ILogger<WeightIndicatorService> _logger;

    private ConnectionState _state = ConnectionState.Disconnected;
    private WeightReading _currentReading;
    private CancellationTokenSource? _workerCancellation;
    private TaskCompletionSource<bool>? _initialConnectTcs;
    private Task? _workerTask;
    private int _readingCount;
    private readonly object _lock = new();

    public WeightIndicatorService(
        IOptions<HardwareOptions> options,
        ISerialPortTransport transport,
        IFrameExtractor frameExtractor,
        IIndicatorProtocolParser protocolParser,
        ILogger<WeightIndicatorService> logger,
        IWeightDecoder? weightDecoder = null)
    {
        _options = options.Value.WeightIndicator;
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _frameExtractor = frameExtractor ?? throw new ArgumentNullException(nameof(frameExtractor));
        _protocolParser = protocolParser ?? throw new ArgumentNullException(nameof(protocolParser));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _weightDecoder = weightDecoder ?? new WeightDecoder();
        _stabilityDetector = new StabilityDetector(_options);
        _currentReading = WeightReading.Empty;
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
    public WeightReading CurrentReading
    {
        get
        {
            lock (_lock)
            {
                return _currentReading;
            }
        }
        private set
        {
            lock (_lock)
            {
                _currentReading = value;
            }

            ReadingReceived?.Invoke(this, value);
        }
    }

    /// <inheritdoc />
    public string Name => $"Weight Indicator ({_options.PortName})";

    /// <inheritdoc />
    public event EventHandler<WeightReading>? ReadingReceived;

    /// <inheritdoc />
    public event EventHandler<ConnectionState>? StateChanged;

    /// <inheritdoc />
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            State = ConnectionState.Disabled;
            _logger.LogInformation("Weight indicator is disabled in configuration");
            return false;
        }

        TaskCompletionSource<bool> tcs;

        lock (_lock)
        {
            if (_workerCancellation is not null)
            {
                if (State == ConnectionState.Connected)
                {
                    return true;
                }

                if (_initialConnectTcs is { Task.IsCompleted: false } existingTcs)
                {
                    tcs = existingTcs;
                }
                else
                {
                    return State == ConnectionState.Connected;
                }
            }
            else
            {
                _initialConnectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                tcs = _initialConnectTcs;
                _workerCancellation = new CancellationTokenSource();
                var token = _workerCancellation.Token;

                _workerTask = Task.Run(() => RunWorkerLoopAsync(token), token);
            }
        }

        try
        {
            using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return State == ConnectionState.Connected;
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        TaskCompletionSource<bool>? initialTcs;
        CancellationTokenSource? cts;
        Task? task;

        lock (_lock)
        {
            initialTcs = _initialConnectTcs;
            _initialConnectTcs = null;
            cts = _workerCancellation;
            task = _workerTask;
            _workerCancellation = null;
            _workerTask = null;
            State = _options.Enabled ? ConnectionState.Disconnected : ConnectionState.Disabled;
        }

        initialTcs?.TrySetResult(false);

        if (cts is not null)
        {
            cts.Cancel();
            try
            {
                await _transport.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while closing transport during disconnect");
            }

            if (task is not null)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            cts.Dispose();
        }

        _stabilityDetector.Reset();
    }

    /// <inheritdoc />
    public Task<WeightReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_currentReading);
        }
    }

    private async Task RunWorkerLoopAsync(CancellationToken token)
    {
        var rawBuffer = new byte[4096];
        int bufferOffset = 0;

        // A terminal whose indicator is not connected - a port typed wrong, a cable pulled, or
        // a site that has not wired one up yet - retries for as long as the application is
        // running. Every attempt used to write the same six lines, roughly 120 a minute, which
        // rolls the 20 MB log in hours and leaves the site with the last few hours of history
        // instead of the thirty days it is configured to keep. So the condition is reported in
        // full when it starts, then quietly, then in full again every hundredth attempt so that
        // a log opened during a long outage still says what is wrong.
        var consecutiveFailures = 0;

        while (!token.IsCancellationRequested)
        {
            var loud = consecutiveFailures == 0 || consecutiveFailures % 100 == 0;

            try
            {
                State = ConnectionState.Connecting;
                _logger.Log(
                    loud ? LogLevel.Information : LogLevel.Debug,
                    "Connecting to serial transport on {PortName}...", _options.PortName);
                await _transport.OpenAsync(token).ConfigureAwait(false);
                State = ConnectionState.Connected;
                consecutiveFailures = 0;
                _logger.LogInformation("Serial transport connected on {PortName}", _options.PortName);
                _initialConnectTcs?.TrySetResult(true);

                bufferOffset = 0;
                var memoryBuffer = new byte[1024];

                while (!token.IsCancellationRequested && _transport.IsOpen)
                {
                    int bytesRead = await _transport.ReadAsync(memoryBuffer, token).ConfigureAwait(false);
                    if (bytesRead <= 0)
                    {
                        await Task.Delay(50, token).ConfigureAwait(false);
                        continue;
                    }

                    if (bufferOffset + bytesRead > rawBuffer.Length)
                    {
                        // Reset buffer on overflow
                        bufferOffset = 0;
                    }

                    Array.Copy(memoryBuffer, 0, rawBuffer, bufferOffset, bytesRead);
                    bufferOffset += bytesRead;

                    ProcessExtractedFrames(rawBuffer, ref bufferOffset);
                }
            }
            catch (OperationCanceledException)
            {
                _initialConnectTcs?.TrySetResult(false);
                break;
            }
            catch (Exception ex)
            {
                State = ConnectionState.Disconnected;
                _initialConnectTcs?.TrySetResult(false);

                _logger.Log(
                    loud ? LogLevel.Warning : LogLevel.Debug,
                    ex,
                    "Serial connection lost or failed on {PortName} (attempt {Attempt})",
                    _options.PortName,
                    consecutiveFailures + 1);
                consecutiveFailures++;

                try
                {
                    await _transport.CloseAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                if (!_options.AutoReconnect || token.IsCancellationRequested)
                {
                    break;
                }

                // Adaptive backoff: 1x, 2x, 4x, 8x, up to 30s maximum, with a safety floor of 1500ms
                int backoffMultiplier = Math.Min(16, 1 << Math.Min(consecutiveFailures - 1, 4));
                int baseInterval = Math.Max(1500, _options.ReconnectIntervalMs);
                int delayMs = Math.Min(30000, baseInterval * backoffMultiplier);

                _logger.Log(
                    loud ? LogLevel.Information : LogLevel.Debug,
                    "Waiting {Delay}ms before reconnecting to {PortName} (attempt {Attempt})...",
                    delayMs, _options.PortName, consecutiveFailures + 1);
                try
                {
                    await Task.Delay(delayMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        State = _options.Enabled ? ConnectionState.Disconnected : ConnectionState.Disabled;
    }

    private void ProcessExtractedFrames(byte[] rawBuffer, ref int bufferOffset)
    {
        while (bufferOffset > 0)
        {
            var currentSpan = new ReadOnlySpan<byte>(rawBuffer, 0, bufferOffset);
            if (_frameExtractor.TryExtractFrame(currentSpan, out var frame, out int bytesConsumed))
            {
                if (_protocolParser.TryParse(frame, DateTime.UtcNow, out var parsedReading))
                {
                    var rawAscii = System.Text.Encoding.ASCII.GetString(frame).Trim();
                    var parsedFrame = new ParsedWeightFrame(
                        rawAscii,
                        parsedReading.Unit,
                        parsedReading.IsStable,
                        rawAscii);

                    if (_options.Decoding is not null &&
                        _weightDecoder.TryDecode(parsedFrame, _options.Decoding, out decimal decodedWeight))
                    {
                        parsedReading = parsedReading with
                        {
                            Value = decodedWeight,
                            Unit = _options.Decoding.TargetUnit ?? parsedReading.Unit
                        };
                    }

                    bool isStable = _stabilityDetector.Evaluate(parsedReading);
                    var finalReading = parsedReading with { IsStable = isStable };
                    CurrentReading = finalReading;

                    if (_readingCount < 5 || _readingCount % 200 == 0)
                    {
                        _logger.LogInformation(
                            "ReadingReceived #{Count}: Value={Value:N1} {Unit}, Stable={Stable}, Source={Source}, Raw='{Raw}'",
                            _readingCount + 1,
                            finalReading.Value,
                            finalReading.Unit,
                            finalReading.IsStable,
                            finalReading.Source,
                            System.Text.Encoding.ASCII.GetString(frame));
                    }
                    _readingCount++;
                }

                // Shift remaining bytes
                int remaining = bufferOffset - bytesConsumed;
                if (remaining > 0)
                {
                    Array.Copy(rawBuffer, bytesConsumed, rawBuffer, 0, remaining);
                }
                bufferOffset = remaining;
            }
            else
            {
                if (bytesConsumed > 0)
                {
                    int remaining = bufferOffset - bytesConsumed;
                    if (remaining > 0)
                    {
                        Array.Copy(rawBuffer, bytesConsumed, rawBuffer, 0, remaining);
                    }
                    bufferOffset = remaining;
                }
                break;
            }
        }
    }

    /// <inheritdoc />
    public Task<HealthResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(HealthResult.Disabled("Weight indicator disabled in configuration"));
        }

        return Task.FromResult(State switch
        {
            ConnectionState.Connected => HealthResult.Healthy($"{_options.PortName} · {_options.BaudRate} baud · reading: {CurrentReading.Value:N0} kg"),
            ConnectionState.Connecting => HealthResult.Degraded($"{_options.PortName} · connecting"),
            _ => HealthResult.Unreachable($"{_options.PortName} · disconnected"),
        });
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        await _transport.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
