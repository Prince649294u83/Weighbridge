using System.IO.Ports;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Hardware.WeightIndicators;

/// <summary>
/// Streams live weight updates to an auxiliary outdoor yard scoreboard display
/// via the configured SendDataPort (COM6) when enabled in Port Settings.
/// </summary>
public sealed class AuxiliaryYardDisplayService : IDisposable
{
    private readonly IOptionsMonitor<HardwareOptions> _optionsMonitor;
    private readonly IWeightIndicatorService _indicator;
    private readonly ILogger<AuxiliaryYardDisplayService> _logger;
    private SerialPort? _serialPort;
    private readonly object _gate = new();
    private bool _disposed;

    public AuxiliaryYardDisplayService(
        IOptionsMonitor<HardwareOptions> optionsMonitor,
        IWeightIndicatorService indicator,
        ILogger<AuxiliaryYardDisplayService> logger)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _indicator = indicator ?? throw new ArgumentNullException(nameof(indicator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _indicator.ReadingReceived += OnReadingReceived;
        _optionsMonitor.OnChange(OnOptionsChanged);
    }

    private void OnOptionsChanged(HardwareOptions options)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var sendPort = options?.PortSettings?.SendDataPort;
            if (sendPort == null || !sendPort.Enabled)
            {
                ClosePort();
            }
            else if (_serialPort != null &&
                     (!string.Equals(_serialPort.PortName, sendPort.PortName, StringComparison.OrdinalIgnoreCase) ||
                      _serialPort.BaudRate != sendPort.BaudRate))
            {
                ClosePort();
            }
        }
    }

    private void OnReadingReceived(object? sender, WeightReading reading)
    {
        if (_disposed) return;

        var options = _optionsMonitor.CurrentValue;
        var sendPort = options?.PortSettings?.SendDataPort;
        if (sendPort == null || !sendPort.Enabled)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed) return;

            try
            {
                EnsurePortOpen(sendPort);
                if (_serialPort is { IsOpen: true })
                {
                    decimal clamped = reading.Value < 0m ? 0m : reading.Value;
                    string payload = $"[ {clamped,7:0} ]\r\n";
                    byte[] bytes = Encoding.ASCII.GetBytes(payload);
                    _serialPort.Write(bytes, 0, bytes.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to send weight frame to auxiliary yard display on {Port}", sendPort.PortName);
                ClosePort();
            }
        }
    }

    private void EnsurePortOpen(SerialPortEndpointOptions endpoint)
    {
        if (_serialPort is { IsOpen: true })
        {
            return;
        }

        try
        {
            _serialPort?.Dispose();
            _serialPort = new SerialPort(endpoint.PortName, endpoint.BaudRate, Parity.None, 8, StopBits.One)
            {
                WriteTimeout = 500,
                ReadTimeout = 500,
            };
            _serialPort.Open();
            _logger.LogInformation("Opened auxiliary yard display port {PortName} at {BaudRate} baud",
                endpoint.PortName, endpoint.BaudRate);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not open auxiliary yard display port {PortName}", endpoint.PortName);
            _serialPort?.Dispose();
            _serialPort = null;
        }
    }

    private void ClosePort()
    {
        try
        {
            if (_serialPort is not null)
            {
                if (_serialPort.IsOpen) _serialPort.Close();
                _serialPort.Dispose();
                _serialPort = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error closing auxiliary yard display port");
            _serialPort = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _indicator.ReadingReceived -= OnReadingReceived;
            ClosePort();
        }
    }
}
