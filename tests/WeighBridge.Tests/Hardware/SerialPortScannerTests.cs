using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Configuration;
using WeighBridge.Hardware.WeightIndicators;

namespace WeighBridge.Tests.Hardware;

/// <summary>
/// Covers COM port auto-detection.
/// </summary>
/// <remarks>
/// The point being pinned is that detection means <em>decoding</em>, not enumerating.
/// Listing the ports Windows reports answers nothing useful on a weighbridge: a terminal
/// has several ports, and picking the wrong one puts another device's number on a slip. A
/// port only counts as found when bytes read from it survive the same frame extractor and
/// protocol parser the live driver uses.
/// <para>
/// The scanner takes its listen and enumerate steps as delegates so these tests exercise
/// the real decode path on a machine with no serial hardware attached.
/// </para>
/// </remarks>
public sealed class SerialPortScannerTests
{
    /// <summary>Frames a real generic-ASCII indicator sends.</summary>
    private const string StableFrame = "12345.6kg\r\n";

    private static SerialPortScanner Build(
        IReadOnlyDictionary<string, byte[]> portTraffic,
        string[]? ports = null,
        WeightIndicatorOptions? indicatorOptions = null,
        List<(string Port, int Baud)>? attempts = null)
    {
        var options = new HardwareOptions
        {
            WeightIndicator = indicatorOptions ?? new WeightIndicatorOptions { PortName = "COM1", BaudRate = 9600 },
        };

        return new SerialPortScanner(
            new DelimitedFrameExtractor(),
            new GenericAsciiProtocolParser(),
            Options.Create(options),
            NullLogger<SerialPortScanner>.Instance,
            listen: (port, baud, _, _) =>
            {
                attempts?.Add((port, baud));

                // A port only answers at the rate it was keyed with; anything else reads
                // as silence, which is what a wrong baud rate looks like in practice.
                return Task.FromResult(
                    portTraffic.TryGetValue($"{port}@{baud}", out var bytes) ? bytes : []);
            },
            enumerate: () => ports ?? [.. portTraffic.Keys.Select(k => k.Split('@')[0]).Distinct()]);
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    [Fact]
    public async Task ScanAsync_FindsThePortThatSpeaksTheProtocol()
    {
        var scanner = Build(new Dictionary<string, byte[]>
        {
            ["COM3@9600"] = Ascii(StableFrame + StableFrame),
        }, ports: ["COM1", "COM2", "COM3"]);

        var results = await scanner.ScanAsync(baudRates: [9600]);

        var found = Assert.Single(results, r => r.SpeaksProtocol);
        Assert.Equal("COM3", found.PortName);
        Assert.Equal(9600, found.BaudRate);
        Assert.Equal(12345.6m, found.SampleWeightKg);
    }

    [Fact]
    public async Task ScanAsync_ReturnsTheRawFrameSoTheOperatorCanCheckTheDisplay()
    {
        // Adopting a port on the strength of "something decoded" is not enough: the
        // operator has to be able to compare the number against the indicator's own
        // display before committing it.
        var scanner = Build(new Dictionary<string, byte[]>
        {
            ["COM1@9600"] = Ascii(StableFrame),
        });

        var found = Assert.Single(await scanner.ScanAsync(baudRates: [9600]), r => r.SpeaksProtocol);

        Assert.False(string.IsNullOrWhiteSpace(found.RawSample));
        Assert.Contains("12345.6", found.RawSample);
    }

    [Fact]
    public async Task ScanAsync_FindsTheRightBaudRateWhenTheConfiguredOneIsWrong()
    {
        var scanner = Build(
            new Dictionary<string, byte[]> { ["COM4@19200"] = Ascii(StableFrame) },
            ports: ["COM4"],
            indicatorOptions: new WeightIndicatorOptions { PortName = "COM4", BaudRate = 9600 });

        var found = Assert.Single(await scanner.ScanAsync(baudRates: [9600, 19200]), r => r.SpeaksProtocol);

        Assert.Equal(19200, found.BaudRate);
    }

    [Fact]
    public async Task ScanAsync_TriesTheConfiguredBaudRateFirst()
    {
        // A rescan after moving the cable is the common reason to run this, and the rate is
        // already right in that case. Only applies when the caller names no rates of its own.
        var attempts = new List<(string Port, int Baud)>();
        var scanner = Build(
            new Dictionary<string, byte[]>(),
            ports: ["COM1"],
            indicatorOptions: new WeightIndicatorOptions { PortName = "COM1", BaudRate = 38400 },
            attempts: attempts);

        await scanner.ScanAsync();

        Assert.Equal(38400, attempts[0].Baud);
        Assert.Equal(attempts.Count, attempts.Select(a => a.Baud).Distinct().Count());
    }

    [Fact]
    public async Task ScanAsync_HonoursTheCallersBaudRateListVerbatim()
    {
        var attempts = new List<(string Port, int Baud)>();
        var scanner = Build(
            new Dictionary<string, byte[]>(),
            ports: ["COM1"],
            indicatorOptions: new WeightIndicatorOptions { PortName = "COM1", BaudRate = 38400 },
            attempts: attempts);

        await scanner.ScanAsync(baudRates: [9600, 19200]);

        Assert.Equal([9600, 19200], attempts.Select(a => a.Baud));
    }

    [Fact]
    public async Task ScanAsync_StopsAtTheFirstPortThatDecodes()
    {
        // Continuing would only offer the operator a choice between one real indicator and
        // a list of ports that stayed silent.
        var attempts = new List<(string Port, int Baud)>();
        var scanner = Build(
            new Dictionary<string, byte[]> { ["COM1@9600"] = Ascii(StableFrame) },
            ports: ["COM1", "COM2", "COM3"],
            attempts: attempts);

        await scanner.ScanAsync(baudRates: [9600]);

        Assert.Equal([("COM1", 9600)], attempts);
    }

    [Fact]
    public async Task ScanAsync_ReportsASilentPortAsSilentRatherThanFound()
    {
        var scanner = Build(new Dictionary<string, byte[]>(), ports: ["COM1"]);

        var results = await scanner.ScanAsync(baudRates: [9600]);

        Assert.All(results, r => Assert.False(r.SpeaksProtocol));
        Assert.All(results, r => Assert.Null(r.SampleWeightKg));
        Assert.Contains(results, r => r.Detail.Contains("sent nothing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScanAsync_DoesNotClaimAPortSpeakingSomethingElse()
    {
        // The failure this exists to prevent: adopting the port a printer or a display board
        // is on because bytes arrived.
        var scanner = Build(new Dictionary<string, byte[]>
        {
            ["COM1@9600"] = Ascii("@ESOME PRINTER CONTROL\r\n"),
        });

        var results = await scanner.ScanAsync(baudRates: [9600]);

        Assert.All(results, r => Assert.False(r.SpeaksProtocol));
    }

    [Fact]
    public async Task ScanAsync_WithNoPortsAtAll_ReturnsNothingRatherThanThrowing()
    {
        var scanner = Build(new Dictionary<string, byte[]>(), ports: []);

        Assert.Empty(await scanner.ScanAsync());
    }

    [Fact]
    public async Task ScanAsync_HonoursAnExplicitPortList()
    {
        var attempts = new List<(string Port, int Baud)>();
        var scanner = Build(
            new Dictionary<string, byte[]>(),
            ports: ["COM1", "COM2", "COM3"],
            attempts: attempts);

        await scanner.ScanAsync(portNames: ["COM2"], baudRates: [9600]);

        Assert.Equal([("COM2", 9600)], attempts);
    }

    [Fact]
    public async Task ScanAsync_IsCancellable()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var scanner = Build(new Dictionary<string, byte[]>(), ports: ["COM1", "COM2"]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scanner.ScanAsync(baudRates: [9600], cancellationToken: cancellation.Token));
    }

    [Fact]
    public void GetAvailablePorts_SortsNumericallySoCom10FollowsCom9()
    {
        // Ordinal sorting puts COM10 between COM1 and COM2, which reads as a bug to an
        // operator scanning the drop-down for their port.
        var scanner = Build(
            new Dictionary<string, byte[]>(),
            ports: ["COM10", "COM2", "COM1", "COM9"]);

        Assert.Equal(["COM1", "COM2", "COM9", "COM10"], scanner.GetAvailablePorts());
    }

    [Theory]
    [InlineData("12345.6kg\r\n", 12345.6)]
    [InlineData("000012,5kg\r\n", 12.5)]
    [InlineData("ST,GS,+  1234.5kg\r\n", 1234.5)]
    [InlineData("0\r\n", 0)]
    public async Task ScanAsync_AcceptsEveryFrameTheLiveDriverAccepts(string traffic, decimal expected)
    {
        var scanner = Build(new Dictionary<string, byte[]> { ["COM1@9600"] = Ascii(traffic) });

        var found = Assert.Single(
            await scanner.ScanAsync(portNames: ["COM1"], baudRates: [9600]),
            r => r.SpeaksProtocol);

        Assert.Equal(expected, found.SampleWeightKg);
    }

    [Theory]
    [InlineData("\r\n\r\n\r\n")]
    [InlineData("no numbers here\r\n")]
    [InlineData("@E\r\n")]
    public async Task ScanAsync_RejectsWhatIsNotAWeightFrame(string traffic)
    {
        var scanner = Build(new Dictionary<string, byte[]> { ["COM1@9600"] = Ascii(traffic) });

        var results = await scanner.ScanAsync(portNames: ["COM1"], baudRates: [9600]);

        Assert.All(results, r => Assert.False(r.SpeaksProtocol));
    }

    [Fact]
    public async Task ScanAsync_OnAPartialLeadingFrame_StillFindsTheCompleteOneBehindIt()
    {
        // A listen window opens mid-transmission, so the first bytes are usually the tail of
        // a frame that started before the port was opened. Here that tail is the last two
        // characters of a unit and its terminator, which cannot parse on its own.
        var scanner = Build(new Dictionary<string, byte[]>
        {
            ["COM1@9600"] = Ascii("kg\r\n" + StableFrame),
        });

        var found = Assert.Single(
            await scanner.ScanAsync(portNames: ["COM1"], baudRates: [9600]),
            r => r.SpeaksProtocol);

        Assert.Equal(12345.6m, found.SampleWeightKg);
    }

    [Fact]
    public async Task ScanAsync_OnBytesThatNeverYieldAFrame_TerminatesInsteadOfSpinning()
    {
        // The extractor reports how many bytes it consumed even when it produced no frame;
        // without honouring that the decode loop would never terminate and this test would
        // hang rather than fail.
        var noise = new byte[512];
        Array.Fill(noise, (byte)0xFF);
        var scanner = Build(new Dictionary<string, byte[]> { ["COM1@9600"] = noise });

        var results = await scanner.ScanAsync(portNames: ["COM1"], baudRates: [9600]);

        Assert.All(results, r => Assert.False(r.SpeaksProtocol));
    }

    [Fact]
    public async Task ScanAsync_ReportsWhatUndecodableBytesLookedLike()
    {
        // "COM1 sent bytes that are not weight frames" is only actionable if the operator
        // can see what did arrive.
        var scanner = Build(new Dictionary<string, byte[]>
        {
            ["COM1@9600"] = Ascii("PRINTER READY\r\n"),
        });

        var result = Assert.Single(await scanner.ScanAsync(portNames: ["COM1"], baudRates: [9600]));

        Assert.False(result.SpeaksProtocol);
        Assert.Contains("PRINTER READY", result.RawSample);
        Assert.Contains("not", result.Detail, StringComparison.OrdinalIgnoreCase);
    }
}
