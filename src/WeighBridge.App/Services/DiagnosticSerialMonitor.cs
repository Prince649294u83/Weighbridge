using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WeighBridge.App.Services;

public sealed class DiagnosticDataChunk
{
    public byte[] RawBytes { get; }
    public string AsciiRepresentation { get; }
    public string HexRepresentation { get; }
    public bool CtsHolding { get; }
    public bool DsrHolding { get; }
    public bool CdHolding { get; }

    public DiagnosticDataChunk(byte[] bytes, int count, bool cts, bool dsr, bool cd)
    {
        RawBytes = new byte[count];
        Array.Copy(bytes, RawBytes, count);
        CtsHolding = cts;
        DsrHolding = dsr;
        CdHolding = cd;

        var sbAscii = new StringBuilder(count * 2);
        var sbHex = new StringBuilder(count * 3);

        for (int i = 0; i < count; i++)
        {
            byte b = bytes[i];
            sbHex.Append(b.ToString("X2")).Append(' ');

            if (b == 0x02) sbAscii.Append("<STX>");
            else if (b == 0x03) sbAscii.Append("<ETX>");
            else if (b == 0x0D) sbAscii.Append("<CR>");
            else if (b == 0x0A) sbAscii.Append("<LF>\n");
            else if (b == 0x06) sbAscii.Append("<ACK>");
            else if (b == 0x15) sbAscii.Append("<NAK>");
            else if (b == 0x20) sbAscii.Append(' ');
            else if (b >= 32 && b <= 126) sbAscii.Append((char)b);
            else sbAscii.Append($"<0x{b:X2}>");
        }

        AsciiRepresentation = sbAscii.ToString();
        HexRepresentation = sbHex.ToString();
    }
}

public sealed class DiagnosticSerialMonitor : IAsyncDisposable, IDisposable
{
    private SerialPort? _port;
    private CancellationTokenSource? _cts;
    private Task? _readTask;

    public bool IsMonitoring => _port is not null && _port.IsOpen;
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
