using System.IO.Ports;
using Microsoft.Extensions.Logging;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Hardware.WeightIndicators;

/// <summary>
/// Real Windows serial port transport using <see cref="SerialPort"/>.
/// </summary>
public sealed class SerialPortTransport : ISerialPortTransport
{
    private readonly WeightIndicatorOptions _options;
    private readonly ILogger<SerialPortTransport> _logger;
    private SerialPort? _serialPort;
    private Stream? _baseStream;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;
    private int _consecutiveOpenFailures;

    public SerialPortTransport(WeightIndicatorOptions options, ILogger<SerialPortTransport> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsOpen => _serialPort is { IsOpen: true };

    public string PortName => _options.PortName;

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_serialPort is { IsOpen: true })
            {
                return;
            }

            _serialPort?.Dispose();

            var parity = Enum.TryParse<Parity>(_options.Parity, true, out var p) ? p : Parity.None;
            var stopBits = Enum.TryParse<StopBits>(_options.StopBits, true, out var s) ? s : StopBits.One;

            _logger.Log(
                _consecutiveOpenFailures == 0 || _consecutiveOpenFailures % 100 == 0
                    ? LogLevel.Information
                    : LogLevel.Debug,
                "Opening serial port {PortName} ({BaudRate}, {DataBits}, {Parity}, {StopBits})",
                _options.PortName,
                _options.BaudRate,
                _options.DataBits,
                parity,
                stopBits);

            _serialPort = new SerialPort(
                _options.PortName,
                _options.BaudRate,
                parity,
                _options.DataBits,
                stopBits)
            {
                ReadTimeout = 5000,
                WriteTimeout = 5000,
                DtrEnable = true,
                RtsEnable = true,
            };

            _serialPort.Open();
            _baseStream = _serialPort.BaseStream;
            _consecutiveOpenFailures = 0;
            _logger.LogInformation("Serial port {PortName} opened successfully", _options.PortName);
        }
        catch (Exception ex)
        {
            // A port that is absent is absent on every retry, and the caller retries every few
            // seconds for as long as the application runs. Reported at Error when the failure
            // starts and every hundredth attempt after that; the attempts in between are Debug,
            // because an identical Error line every three seconds rolls the log long before
            // anyone reads it and buries every other error with it.
            _logger.Log(
                _consecutiveOpenFailures == 0 || _consecutiveOpenFailures % 100 == 0
                    ? LogLevel.Error
                    : LogLevel.Debug,
                ex,
                "Failed to open serial port {PortName} (attempt {Attempt})",
                _options.PortName,
                _consecutiveOpenFailures + 1);
            _consecutiveOpenFailures++;
            _serialPort?.Dispose();
            _serialPort = null;
            _baseStream = null;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CloseAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_serialPort is null)
            {
                return;
            }

            _logger.LogInformation("Closing serial port {PortName}", _options.PortName);
            try
            {
                if (_serialPort.IsOpen)
                {
                    _serialPort.Close();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while closing serial port {PortName}", _options.PortName);
            }
            finally
            {
                _serialPort.Dispose();
                _serialPort = null;
                _baseStream = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var stream = _baseStream;
        if (stream is null || _serialPort is not { IsOpen: true })
        {
            throw new InvalidOperationException($"Serial port {_options.PortName} is not open.");
        }

        return await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await CloseAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
