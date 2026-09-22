using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WeighBridge.Core.Abstractions;

namespace WeighBridge.App.Services;

public sealed class DiagnosticSerialMonitor : IAsyncDisposable, IDisposable
{
    private SerialPort? _port;
    private CancellationTokenSource? _cts;
    private Task? _readTask;

    public bool IsMonitoring => _port is not null && _port.IsOpen;
    public bool IsRunning => IsMonitoring;
    public string ActivePortName => _port?.PortName ?? string.Empty;

    public event EventHandler<DiagnosticDataChunk>? DataReceived;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<bool>? StateChanged;

    public async Task<bool> StartAsync(
        string portName,
        int baudRate,
        int dataBits = 8,
        Parity parity = Parity.None,
        StopBits stopBits = StopBits.One,
        Handshake handshake = Handshake.None)
    {
        await StopAsync();

        try
        {
            _port = new SerialPort
            {
                PortName = portName,
                BaudRate = baudRate,
                DataBits = dataBits,
                Parity = parity,
                StopBits = stopBits,
                Handshake = handshake,
                ReadTimeout = 500,
                WriteTimeout = 500,
                DtrEnable = true,
                RtsEnable = true
            };

            _port.Open();
            _cts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoopAsync(_cts.Token));
            StateChanged?.Invoke(this, true);
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            await StopAsync();
            return false;
        }
    }

    public async Task StopAsync()
    {
        if (_cts != null)
        {
            try
            {
                _cts.Cancel();
            }
            catch { }
        }

        if (_readTask != null)
        {
            try
            {
                await Task.WhenAny(_readTask, Task.Delay(500));
            }
            catch { }
            _readTask = null;
        }

        if (_port != null)
        {
            try
            {
                if (_port.IsOpen)
                {
                    _port.DiscardInBuffer();
                    _port.DiscardOutBuffer();
                    _port.Close();
                }
            }
            catch { }
            finally
            {
                _port.Dispose();
                _port = null;
            }
        }

        _cts?.Dispose();
        _cts = null;
        StateChanged?.Invoke(this, false);
    }

    public bool Send(string text, bool appendCrLf = true)
    {
        if (_port == null || !_port.IsOpen) return false;

        try
        {
            var data = appendCrLf ? text + "\r\n" : text;
            _port.Write(data);
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Send failed: {ex.Message}");
            return false;
        }
    }

    public bool SendHex(string hexString)
    {
        if (_port == null || !_port.IsOpen) return false;

        try
        {
            var parts = hexString.Split(new[] { ' ', ',', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new List<byte>();
            foreach (var part in parts)
            {
                if (byte.TryParse(part, System.Globalization.NumberStyles.HexNumber, null, out var b))
                {
                    bytes.Add(b);
                }
            }

            if (bytes.Count > 0)
            {
                var arr = bytes.ToArray();
                _port.Write(arr, 0, arr.Length);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Send hex failed: {ex.Message}");
            return false;
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[2048];

        while (!ct.IsCancellationRequested && _port != null && _port.IsOpen)
        {
            try
            {
                var bytesRead = await _port.BaseStream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (bytesRead > 0)
                {
                    bool cts = false, dsr = false, cd = false;
                    try
                    {
                        cts = _port.CtsHolding;
                        dsr = _port.DsrHolding;
                        cd = _port.CDHolding;
                    }
                    catch { }

                    var chunk = new DiagnosticDataChunk(buffer, bytesRead, cts, dsr, cd);
                    DataReceived?.Invoke(this, chunk);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    ErrorOccurred?.Invoke(this, $"Serial read error: {ex.Message}");
                }
                break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            if (_port is not null && _port.IsOpen)
            {
                _port.Close();
            }
            _port?.Dispose();
        }
        catch { }
        _cts?.Dispose();
    }
}
