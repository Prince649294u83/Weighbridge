using System.IO.Ports;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Hardware.WeightIndicators;

/// <summary>
/// Real Windows serial port transport using <see cref="SerialPort"/> with an automatic
/// direct stream fallback for CH340 / USB-Serial chipsets whose Windows drivers reject
/// .NET's internal DCB configuration during active UART data streams.
/// </summary>
public sealed class SerialPortTransport : ISerialPortTransport
{
    #region Win32 Interop

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetCommState(SafeFileHandle hFile, ref Dcb lpDCB);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommState(SafeFileHandle hFile, ref Dcb lpDCB);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommTimeouts(SafeFileHandle hFile, ref CommTimeouts lpCommTimeouts);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EscapeCommFunction(SafeFileHandle hFile, uint dwFunc);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetCommModemStatus(SafeFileHandle hFile, out uint lpModemStat);

    private const uint GenericReadWrite = 0x80000000u | 0x40000000u;
    private const uint OpenExisting = 3;
    private const uint SetRts = 3;
    private const uint SetDtr = 5;
    private const uint MsCtsOn = 0x0010;
    private const uint MsDsrOn = 0x0020;
    private const uint MsRlsdOn = 0x0080;

    [StructLayout(LayoutKind.Sequential)]
    private struct Dcb
    {
        public uint DCBlength;
        public uint BaudRate;
        public uint Flags;
        public ushort wReserved;
        public ushort XonLim;
        public ushort XoffLim;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public byte XonChar;
        public byte XoffChar;
        public byte ErrorChar;
        public byte EofChar;
        public byte EvtChar;
        public ushort wReserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommTimeouts
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }

    #endregion

    private WeightIndicatorOptions _options;
    private readonly ILogger<SerialPortTransport> _logger;
    private SerialPort? _serialPort;
    private SafeFileHandle? _rawHandle;
    private Stream? _baseStream;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;
    private int _consecutiveOpenFailures;

    public SerialPortTransport(WeightIndicatorOptions options, ILogger<SerialPortTransport> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void UpdateOptions(WeightIndicatorOptions newOptions)
    {
        ArgumentNullException.ThrowIfNull(newOptions);
        _options = newOptions;
    }

    public bool IsOpen => (_serialPort is { IsOpen: true }) || (_rawHandle is { IsInvalid: false, IsClosed: false });

    public string PortName => _options.PortName;

    public bool CtsHolding
    {
        get
        {
            try
            {
                if (_serialPort is { IsOpen: true }) return _serialPort.CtsHolding;
                if (_rawHandle is { IsInvalid: false, IsClosed: false } && GetCommModemStatus(_rawHandle, out var stat))
                    return (stat & MsCtsOn) != 0;
                return false;
            }
            catch { return false; }
        }
    }

    public bool DsrHolding
    {
        get
        {
            try
            {
                if (_serialPort is { IsOpen: true }) return _serialPort.DsrHolding;
                if (_rawHandle is { IsInvalid: false, IsClosed: false } && GetCommModemStatus(_rawHandle, out var stat))
                    return (stat & MsDsrOn) != 0;
                return false;
            }
            catch { return false; }
        }
    }

    public bool CdHolding
    {
        get
        {
            try
            {
                if (_serialPort is { IsOpen: true }) return _serialPort.CDHolding;
                if (_rawHandle is { IsInvalid: false, IsClosed: false } && GetCommModemStatus(_rawHandle, out var stat))
                    return (stat & MsRlsdOn) != 0;
                return false;
            }
            catch { return false; }
        }
    }

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsOpen)
            {
                return;
            }

            _serialPort?.Dispose();
            _serialPort = null;
            _baseStream?.Dispose();
            _baseStream = null;
            _rawHandle?.Dispose();
            _rawHandle = null;

            var parity = Enum.TryParse<Parity>(_options.Parity, true, out var p) ? p : Parity.None;
            var stopBits = Enum.TryParse<StopBits>(_options.StopBits, true, out var s) ? s : StopBits.One;
            var handshake = _options.Decoding.RtsCts
                ? Handshake.RequestToSend
                : (Enum.TryParse<Handshake>(_options.Handshake, true, out var h) ? h : Handshake.None);
            bool rts = _options.RtsEnable || _options.Decoding.RtsCts;
            int timeout = _options.ReadTimeoutMs > 0 ? _options.ReadTimeoutMs : 5000;

            _logger.Log(
                _consecutiveOpenFailures == 0 || _consecutiveOpenFailures % 100 == 0
                    ? LogLevel.Information
                    : LogLevel.Debug,
                "Opening serial port {PortName} ({BaudRate}, {DataBits}, {Parity}, {StopBits}, DTR={Dtr}, RTS={Rts}, Handshake={Handshake})",
                _options.PortName,
                _options.BaudRate,
                _options.DataBits,
                parity,
                stopBits,
                _options.DtrEnable,
                rts,
                handshake);

            _serialPort = new SerialPort(
                _options.PortName,
                _options.BaudRate,
                parity,
                _options.DataBits,
                stopBits)
            {
                ReadTimeout = timeout,
                WriteTimeout = timeout,
                DtrEnable = _options.DtrEnable,
                RtsEnable = rts,
                Handshake = handshake,
            };

            try
            {
                _serialPort.Open();
                _baseStream = _serialPort.BaseStream;
                _consecutiveOpenFailures = 0;
                _logger.LogInformation("Serial port {PortName} opened successfully", _options.PortName);
            }
            catch (IOException ioEx) when (ioEx.HResult == unchecked((int)0x8007001F) || ioEx.Message.Contains("not functioning", StringComparison.OrdinalIgnoreCase))
            {
                // CH340 or drivers where SetCommState fails inside .NET SerialPort.Open()
                // due to continuous incoming UART data. Fall back to direct Win32 stream.
                _logger.LogInformation(
                    "Standard SerialPort.Open hit driver limitation on {PortName}; activating direct stream transport",
                    _options.PortName);

                _serialPort.Dispose();
                _serialPort = null;

                OpenRawStream(timeout, rts);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Log(
                _consecutiveOpenFailures == 0 || _consecutiveOpenFailures % 20 == 0
                    ? LogLevel.Warning
                    : LogLevel.Debug,
                ex,
                "Serial port {PortName} is in use by another application or access was denied. Close external serial utilities (e.g. Terminal v1.9b) locking this port.",
                _options.PortName);
            _consecutiveOpenFailures++;
            _serialPort?.Dispose();
            _serialPort = null;
            _baseStream?.Dispose();
            _baseStream = null;
            _rawHandle?.Dispose();
            _rawHandle = null;
            throw;
        }
        catch (Exception ex)
        {
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
            _baseStream?.Dispose();
            _baseStream = null;
            _rawHandle?.Dispose();
            _rawHandle = null;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static SafeFileHandle OpenRawHandle(string portName, int timeoutMs = 5000, bool rts = true, bool dtr = true)
    {
        var devicePath = portName.StartsWith(@"\\", StringComparison.Ordinal)
            ? portName
            : @"\\.\" + portName;

        var handle = CreateFile(
            devicePath,
            GenericReadWrite,
            0, // exclusive
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            throw new IOException($"Failed to open direct serial stream on {portName} (Win32 error {err})", err);
        }

        if (dtr)
        {
            EscapeCommFunction(handle, SetDtr);
        }
        if (rts)
        {
            EscapeCommFunction(handle, SetRts);
        }

        var timeouts = new CommTimeouts
        {
            ReadIntervalTimeout = 50,
            ReadTotalTimeoutMultiplier = 10,
            ReadTotalTimeoutConstant = (uint)timeoutMs,
            WriteTotalTimeoutMultiplier = 10,
            WriteTotalTimeoutConstant = (uint)timeoutMs
        };
        SetCommTimeouts(handle, ref timeouts);

        return handle;
    }

    private void OpenRawStream(int timeoutMs, bool rts)
    {
        var handle = OpenRawHandle(_options.PortName, timeoutMs, rts, _options.DtrEnable);
        _rawHandle = handle;
        _baseStream = new FileStream(handle, FileAccess.ReadWrite, 4096, isAsync: false);
        _consecutiveOpenFailures = 0;
        _logger.LogInformation("Serial port {PortName} opened successfully via direct stream transport", _options.PortName);
    }

    public async Task CloseAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_serialPort is null && _rawHandle is null)
            {
                return;
            }

            _logger.LogInformation("Closing serial port {PortName}", _options.PortName);
            try
            {
                if (_serialPort is { IsOpen: true })
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
                _serialPort?.Dispose();
                _serialPort = null;
                _baseStream?.Dispose();
                _baseStream = null;
                _rawHandle?.Dispose();
                _rawHandle = null;
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
        if (stream is null || !IsOpen)
        {
            throw new InvalidOperationException($"Serial port {_options.PortName} is not open.");
        }

        return await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var stream = _baseStream;
        if (stream is null || !IsOpen)
        {
            throw new InvalidOperationException($"Serial port {_options.PortName} is not open.");
        }

        await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
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
