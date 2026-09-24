using System.IO.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Hardware.WeightIndicators;

/// <summary>
/// Finds the weight indicator by listening on each serial port and decoding what arrives.
/// </summary>
/// <remarks>
/// <para>
/// The decode step reuses the live <see cref="IFrameExtractor"/> and
/// <see cref="IIndicatorProtocolParser"/> rather than a looser "does this look like
/// digits" test. Anything the driver could not read is not a match, and anything that
/// matches here is something the driver will read — the detector and the runtime agree by
/// construction instead of by coincidence.
/// </para>
/// <para>
/// Ports are probed in order and the scan stops at the first one that decodes a weight,
/// so the common case (one indicator, one adapter) is fast. Every attempt is reported,
/// including the failures, because "COM1 opened but sent nothing in 1.5s" and "COM1 is
/// held by another program" send the person holding the cable to different places.
/// </para>
/// </remarks>
public sealed class SerialPortScanner : IIndicatorPortScanner
{
    /// <summary>
    /// Rates tried when the caller names none, after the configured rate. Ordered by how
    /// often industrial indicators ship set to them, so the usual case exits early.
    /// </summary>
    private static readonly int[] StandardBaudRates = [9600, 4800, 2400, 1200, 19200, 38400, 57600, 115200];

    private static readonly TimeSpan DefaultListen = TimeSpan.FromMilliseconds(1500);

    private readonly IFrameExtractor _frames;
    private readonly IIndicatorProtocolParser _parser;
    private readonly WeightIndicatorOptions _options;
    private readonly ILogger<SerialPortScanner> _logger;

    /// <summary>
    /// Reads whatever arrives on one port at one baud rate. Replaceable so the decode path
    /// can be tested on a machine with no serial hardware; the default is the real port.
    /// </summary>
    private readonly Func<string, int, TimeSpan, CancellationToken, Task<byte[]>> _listen;

    /// <summary>Enumerates the ports. Replaceable for the same reason as <see cref="_listen"/>.</summary>
    private readonly Func<string[]> _enumerate;

    public SerialPortScanner(
        IFrameExtractor frames,
        IIndicatorProtocolParser parser,
        IOptions<HardwareOptions> options,
        ILogger<SerialPortScanner> logger,
        Func<string, int, TimeSpan, CancellationToken, Task<byte[]>>? listen = null,
        Func<string[]>? enumerate = null)
    {
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value.WeightIndicator;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _listen = listen ?? ListenOnRealPortAsync;
        _enumerate = enumerate ?? SerialPort.GetPortNames;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAvailablePorts()
    {
        // Sorted numerically, not lexically: COM10 belongs after COM9, and a list that
        // reads COM1, COM10, COM2 looks like a bug to the operator reading it.
        return [.. _enumerate()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(PortNumber)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PortProbeResult>> ScanAsync(
        IEnumerable<string>? portNames = null,
        IEnumerable<int>? baudRates = null,
        TimeSpan? listenPerAttempt = null,
        CancellationToken cancellationToken = default)
    {
        var ports = (portNames ?? GetAvailablePorts()).ToArray();
        var rates = ResolveBaudRates(baudRates);
        var listen = listenPerAttempt ?? DefaultListen;
        var results = new List<PortProbeResult>();

        if (ports.Length == 0)
        {
            _logger.LogWarning("Indicator scan found no serial ports on this machine");
            return results;
        }

        _logger.LogInformation(
            "Scanning {PortCount} serial port(s) at {RateCount} baud rate(s) for a {Protocol} indicator",
            ports.Length,
            rates.Length,
            _parser.Name);

        foreach (var port in ports)
        {
            foreach (var baud in rates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await ProbeAsync(port, baud, listen, cancellationToken).ConfigureAwait(false);
                results.Add(result);

                if (result.SpeaksProtocol)
                {
                    _logger.LogInformation(
                        "Indicator detected on {PortName} at {BaudRate} baud: {Weight} kg from frame '{Frame}'",
                        result.PortName,
                        result.BaudRate,
                        result.SampleWeightKg,
                        result.RawSample);

                    // First match wins. Continuing would only offer the operator a choice
                    // between one real indicator and a list of ports that stayed silent.
                    return results;
                }
            }
        }

        _logger.LogWarning(
            "Indicator scan completed without a match across {Attempts} attempt(s)",
            results.Count);

        return results;
    }

    /// <summary>
    /// Listens on one port at one baud rate and reports whether the bytes decoded.
    /// </summary>
    private async Task<PortProbeResult> ProbeAsync(
        string portName,
        int baudRate,
        TimeSpan listen,
        CancellationToken cancellationToken)
    {
        byte[] received;

        try
        {
            received = await _listen(portName, baudRate, listen, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            // Named specifically: this is the "another program has the port" case, and it is
            // the one failure the operator can actually do something about.
            return new PortProbeResult(portName, baudRate, false, null, null,
                $"{portName} is in use by another program. Close it and scan again.");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Probe of {PortName} at {BaudRate} failed to open", portName, baudRate);
            return new PortProbeResult(portName, baudRate, false, null, null,
                $"{portName} could not be opened at {baudRate} baud ({ex.GetType().Name}).");
        }

        if (received.Length == 0)
        {
            return new PortProbeResult(portName, baudRate, false, null, null,
                $"{portName} opened at {baudRate} baud but sent nothing.");
        }

        if (TryDecode(received, out var weight, out var frame))
        {
            return new PortProbeResult(portName, baudRate, true, weight, frame,
                $"{portName} at {baudRate} baud is sending {_parser.Name} frames — read {weight} kg.");
        }

        return new PortProbeResult(portName, baudRate, false, null, Describe(received),
            $"{portName} sent {received.Length} byte(s) at {baudRate} baud that are not " +
            $"{_parser.Name} frames — most likely the wrong baud rate or a different device.");
    }

    /// <summary>
    /// Runs the received bytes through the real extractor and parser.
    /// </summary>
    /// <remarks>
    /// Internal so the decision this whole class rests on can be tested with no serial
    /// hardware present, which is every build machine.
    /// </remarks>
    internal bool TryDecode(ReadOnlySpan<byte> buffer, out decimal weight, out string? frameText)
    {
        weight = 0m;
        frameText = null;

        var remaining = buffer;

        while (!remaining.IsEmpty)
        {
            if (!_frames.TryExtractFrame(remaining, out var frame, out var consumed))
            {
                // No further complete frame. bytesConsumed can still be positive when the
                // extractor discards junk; without honouring it this loop would spin.
                if (consumed <= 0)
                {
                    return false;
                }

                remaining = remaining[consumed..];
                continue;
            }

            if (_parser.TryParse(frame, DateTime.UtcNow, out var reading))
            {
                weight = reading.Value;
                frameText = reading.RawFrame;
                return true;
            }

            remaining = consumed > 0 ? remaining[consumed..] : default;
        }

        return false;
    }

    /// <summary>
    /// The configured rate first, then the standard rates, with no repeats.
    /// </summary>
    /// <remarks>
    /// The configured rate leads because a rescan after moving the cable is the common
    /// reason to run this, and in that case the rate is already right.
    /// </remarks>
    private int[] ResolveBaudRates(IEnumerable<int>? requested)
    {
        if (requested is not null)
        {
            var explicitRates = requested.Where(rate => rate > 0).Distinct().ToArray();
            if (explicitRates.Length > 0)
            {
                return explicitRates;
            }
        }

        var rates = new List<int>();
        if (_options.BaudRate > 0)
        {
            rates.Add(_options.BaudRate);
        }

        foreach (var standard in StandardBaudRates)
        {
            if (!rates.Contains(standard))
            {
                rates.Add(standard);
            }
        }

        return [.. rates];
    }

    /// <summary>
    /// Opens the port, reads for the listen window, and returns everything that arrived.
    /// </summary>
    private async Task<byte[]> ListenOnRealPortAsync(
        string portName,
        int baudRate,
        TimeSpan listen,
        CancellationToken cancellationToken)
    {
        var parity = Enum.TryParse<Parity>(_options.Parity, true, out var p) ? p : Parity.None;
        var stopBits = Enum.TryParse<StopBits>(_options.StopBits, true, out var s) ? s : StopBits.One;
        var handshake = Enum.TryParse<Handshake>(_options.Handshake, true, out var h) ? h : Handshake.None;

        try
        {
            using var port = new SerialPort(portName, baudRate, parity, _options.DataBits, stopBits)
            {
                ReadTimeout = 250,
                WriteTimeout = 250,
                DtrEnable = true,
                RtsEnable = true,
                Handshake = handshake,
            };

            port.Open();

            var buffer = new byte[512];
            var collected = new List<byte>(1024);
            var deadline = DateTime.UtcNow + listen;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (port.BytesToRead == 0)
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var read = port.Read(buffer, 0, Math.Min(buffer.Length, port.BytesToRead));
                if (read <= 0)
                {
                    continue;
                }

                collected.AddRange(buffer.AsSpan(0, read).ToArray());

                // Enough for several frames from any indicator. Reading past this only delays
                // the answer, and a device that has sent this much is not going to become
                // decodable by sending more of the same.
                if (collected.Count >= 512)
                {
                    break;
                }
            }

            return [.. collected];
        }
        catch (IOException ioEx) when (ioEx.HResult == unchecked((int)0x8007001F) || ioEx.Message.Contains("not functioning", StringComparison.OrdinalIgnoreCase))
        {
            // CH340 / USB-Serial direct stream fallback for scanner
            return await ListenViaDirectStreamAsync(portName, baudRate, listen, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<byte[]> ListenViaDirectStreamAsync(
        string portName,
        int baudRate,
        TimeSpan listen,
        CancellationToken cancellationToken)
    {
        try
        {
            using var handle = SerialPortTransport.OpenRawHandle(portName, baudRate, (int)listen.TotalMilliseconds, rts: true, dtr: true);
            var buffer = new byte[512];
            var collected = new List<byte>(1024);
            var deadline = DateTime.UtcNow + listen;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var stat = new SerialPortTransport.ComStat();
                if (!SerialPortTransport.ClearCommError(handle, out _, ref stat))
                {
                    break;
                }

                if (stat.cbInQue > 0)
                {
                    int toRead = (int)Math.Min((uint)buffer.Length, stat.cbInQue);
                    if (SerialPortTransport.ReadFile(handle, buffer, (uint)toRead, out uint read, IntPtr.Zero) && read > 0)
                    {
                        collected.AddRange(buffer.AsSpan(0, (int)read).ToArray());

                        if (collected.Count >= 512)
                        {
                            break;
                        }
                    }
                }

                await Task.Delay(30, cancellationToken).ConfigureAwait(false);
            }

            return [.. collected];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Direct stream probe on {PortName} at {BaudRate} failed", portName, baudRate);
            return [];
        }
    }

    /// <summary>Renders undecodable bytes as printable text so the log shows what arrived.</summary>
    private static string Describe(ReadOnlySpan<byte> bytes)
    {
        var span = bytes.Length > 64 ? bytes[..64] : bytes;
        return string.Concat(span.ToArray().Select(b => b is >= 0x20 and < 0x7F ? ((char)b).ToString() : $"<{b:X2}>"));
    }

    /// <summary>Trailing digits of a port name, for numeric ordering. Non-numeric sorts last.</summary>
    private static int PortNumber(string name)
    {
        var digits = new string([.. name.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit)]);
        return int.TryParse(digits, out var number) ? number : int.MaxValue;
    }
}
