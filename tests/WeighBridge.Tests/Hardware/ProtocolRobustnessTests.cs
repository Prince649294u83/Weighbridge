using WeighBridge.Hardware.WeightIndicators;

namespace WeighBridge.Tests.Hardware;

/// <summary>
/// Regression pins for the decimal-comma defect: a European indicator sending
/// <c>000012,5kg</c> used to be read as 5 kg â€” the comma was mistaken for the Toledo
/// format's field separator and the last segment taken as the whole weight. On a
/// weighbridge, a silently wrong number is the worst possible failure.
/// </summary>
public sealed class DecimalCommaParserTests
{
    private static readonly DateTime Timestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly GenericAsciiProtocolParser _parser = new();

    private WeighBridge.Core.Abstractions.WeightReading Parse(string frame)
    {
        Assert.True(
            _parser.TryParse(System.Text.Encoding.ASCII.GetBytes(frame), Timestamp, out var reading),
            $"Frame '{frame}' should parse.");
        return reading;
    }

    [Theory]
    [InlineData("000012,5kg", 12.5)]
    [InlineData("+00012500,5kg", 12500.5)]
    [InlineData("12,5", 12.5)]
    [InlineData("12345,678 kg", 12345.678)]
    public void DecimalComma_IsTheDecimalSeparator_NotAFieldSplit(string frame, decimal expected)
    {
        var reading = Parse(frame);
        Assert.Equal(expected, reading.Value);
        Assert.Equal("kg", reading.Unit);
    }

    [Theory]
    [InlineData("000012.5kg", 12.5)]
    [InlineData("25400", 25400)]
    [InlineData("-100", -100)]
    public void DecimalPoint_StillParses(string frame, decimal expected)
    {
        var reading = Parse(frame);
        Assert.Equal(expected, reading.Value);
    }

    /// <summary>The Toledo status token is letters; digits mean a decimal comma follows.</summary>
    [Fact]
    public void ToledoFormat_WithStatusToken_StillSplitsOnCommas()
    {
        var reading = Parse("ST,GS,+025400kg");
        Assert.Equal(25400m, reading.Value);
        Assert.True(reading.IsStable);
    }

    [Fact]
    public void ToledoUnstable_IsNotStable()
    {
        var reading = Parse("US,GS,+025400kg");
        Assert.False(reading.IsStable);
    }

    [Fact]
    public void UnknownStatusToken_DoesNotClaimStability()
    {
        // "OL" (overload) must not be read as stable; the detector decides.
        var reading = Parse("OL,GS,+025400kg");
        Assert.False(reading.IsStable);
        Assert.Equal(25400m, reading.Value);
    }

    [Fact]
    public void KeywordFormat_AcceptsDecimalComma()
    {
        var reading = Parse("WT: 12500,5 kg STABLE");
        Assert.Equal(12500.5m, reading.Value);
        Assert.True(reading.IsStable);
    }

    [Fact]
    public void StatusPrefixFormat_AcceptsDecimalComma()
    {
        var reading = Parse("WS+0012500,5");
        Assert.Equal(12500.5m, reading.Value);
    }

    [Theory]
    [InlineData("12.5.6")]
    [InlineData("--5")]
    [InlineData("+-5")]
    public void AmbiguousNumbers_FailRatherThanGuess(string frame)
    {
        Assert.False(_parser.TryParse(System.Text.Encoding.ASCII.GetBytes(frame), Timestamp, out _));
    }

    [Theory]
    [InlineData("5000 lb", "lb")]
    [InlineData("12,5 t", "t")]
    [InlineData("999999 g", "g")]
    [InlineData("7 tn", "t")]
    public void Units_AreRecognised(string frame, string expectedUnit)
    {
        var reading = Parse(frame);
        Assert.Equal(expectedUnit, reading.Unit);
    }
}

/// <summary>
/// Regression pins for the mixed-protocol buffering defect: a complete line-delimited
/// frame sitting ahead of an unterminated STX frame used to be discarded as junk.
/// </summary>
public sealed class DelimitedFrameExtractorTests
{
    private readonly DelimitedFrameExtractor _extractor = new();

    private static byte[] B(params byte[] bytes) => bytes;
    private const byte Stx = 0x02;
    private const byte Etx = 0x03;

    [Fact]
    public void CompleteLine_BeforeUnterminatedStx_IsExtracted_NotDestroyed()
    {
        // A full CRLF frame, then an STX whose payload is still arriving.
        var buffer = System.Text.Encoding.ASCII.GetBytes("ST,GS,+025400kg\r\n")
            .Append(Stx).ToArray();

        Assert.True(_extractor.TryExtractFrame(buffer, out var frame, out var consumed));
        Assert.Equal("ST,GS,+025400kg"u8.ToArray(), frame.ToArray());
        Assert.Equal(consumed, buffer.Length - 1); // only the STX remains

        // And the STX path still works on what's left.
        var rest = buffer[consumed..].Append(Etx).ToArray();
        Assert.True(_extractor.TryExtractFrame(rest, out var stxFrame, out _));
        Assert.Empty(stxFrame.ToArray());
    }

    [Fact]
    public void StxFrame_CompletingFirst_Wins()
    {
        var buffer = new byte[] { Stx }
            .Concat(System.Text.Encoding.ASCII.GetBytes("25400"))
            .Append(Etx)
            .ToArray();

        Assert.True(_extractor.TryExtractFrame(buffer, out var frame, out var consumed));
        Assert.Equal("25400"u8.ToArray(), frame.ToArray());
        Assert.Equal(buffer.Length, consumed);
    }

    [Fact]
    public void JunkBeforeStx_IsConsumedWithoutTouchingLaterLines()
    {
        var junk = new byte[] { 0xFF, 0xFE };
        var buffer = junk.Append(Stx).ToArray();

        Assert.False(_extractor.TryExtractFrame(buffer, out _, out var consumed));
        Assert.Equal(junk.Length, consumed);
    }

    [Fact]
    public void BareCr_TerminatesItsLine()
    {
        var line = System.Text.Encoding.ASCII.GetBytes("+025400 kg\r\n");
        var buffer = line.Concat(System.Text.Encoding.ASCII.GetBytes("partial-no-terminator")).ToArray();

        Assert.True(_extractor.TryExtractFrame(buffer, out var frame, out var consumed));
        Assert.Equal("+025400 kg"u8.ToArray(), frame.ToArray());
        Assert.Equal(line.Length, consumed);
    }

    [Fact]
    public void RunawayBuffer_TrimsToTail()
    {
        var buffer = Enumerable.Repeat((byte)'x', 1100).ToArray();

        Assert.False(_extractor.TryExtractFrame(buffer, out _, out var consumed));
        Assert.Equal(1100 - 256, consumed);
    }

    [Fact]
    public void BracketNull_Frame_IsExtracted_And_Junk_Before_Bracket_Is_Consumed()
    {
        byte[] junk = [0xAA, 0xBB, 0xCC];
        byte[] frame = [(byte)'[', (byte)'0', (byte)'0', (byte)'0', (byte)'0', (byte)'3', (byte)'0', (byte)'0', 0x00];
        byte[] buffer = junk.Concat(frame).ToArray();

        Assert.True(_extractor.TryExtractFrame(buffer, out var extracted, out var consumed));
        Assert.Equal("0000300"u8.ToArray(), extracted.ToArray());
        Assert.Equal(junk.Length + frame.Length, consumed);
    }
}
