using System.Text;
using WeighBridge.Core.Abstractions;
using WeighBridge.Domain.Enums;
using WeighBridge.Hardware.WeightIndicators;
using Xunit;

namespace WeighBridge.Tests.Hardware;

public sealed class WeightIndicatorProtocolTests
{
    [Fact]
    public void DelimitedFrameExtractor_Extracts_StxEtx_Frames()
    {
        var extractor = new DelimitedFrameExtractor();
        byte[] payload = [0x02, (byte)'S', (byte)'T', (byte)',', (byte)'1', (byte)'2', (byte)'0', (byte)'0', 0x03];

        bool extracted = extractor.TryExtractFrame(payload, out var frame, out int consumed);

        Assert.True(extracted);
        Assert.Equal("ST,1200", Encoding.ASCII.GetString(frame));
        Assert.Equal(payload.Length, consumed);
    }

    [Fact]
    public void DelimitedFrameExtractor_Extracts_CrLf_Frames()
    {
        var extractor = new DelimitedFrameExtractor();
        byte[] payload = Encoding.ASCII.GetBytes("+025400 kg\r\n");

        bool extracted = extractor.TryExtractFrame(payload, out var frame, out int consumed);

        Assert.True(extracted);
        Assert.Equal("+025400 kg", Encoding.ASCII.GetString(frame));
        Assert.Equal(payload.Length, consumed);
    }

    [Fact]
    public void DelimitedFrameExtractor_Extracts_BracketNull_Frames()
    {
        var extractor = new DelimitedFrameExtractor();
        byte[] payload = [(byte)'[', (byte)'0', (byte)'0', (byte)'0', (byte)'0', (byte)'3', (byte)'5', (byte)'0', 0x00];

        bool extracted = extractor.TryExtractFrame(payload, out var frame, out int consumed);

        Assert.True(extracted);
        Assert.Equal("0000350", Encoding.ASCII.GetString(frame));
        Assert.Equal(9, consumed);
    }

    [Fact]
    public void DelimitedFrameExtractor_Discards_Junk_Before_Bracket()
    {
        var extractor = new DelimitedFrameExtractor();
        byte[] buffer = [0xAA, 0xBB, (byte)'[', (byte)'0', (byte)'0', (byte)'0', (byte)'0', (byte)'4', (byte)'0', (byte)'0', 0x00];

        bool extracted = extractor.TryExtractFrame(buffer, out var frame, out int consumed);

        Assert.True(extracted);
        Assert.Equal("0000400", Encoding.ASCII.GetString(frame));
        Assert.Equal(11, consumed);
    }

    [Fact]
    public void DelimitedFrameExtractor_Handles_Partial_Buffer()
    {
        var extractor = new DelimitedFrameExtractor();
        byte[] partial = [0x02, (byte)'S', (byte)'T'];

        bool extracted = extractor.TryExtractFrame(partial, out var frame, out int consumed);

        Assert.False(extracted);
        Assert.Equal(0, consumed);
    }

    [Fact]
    public void DelimitedFrameExtractor_Discards_Junk_Before_Stx()
    {
        var extractor = new DelimitedFrameExtractor();
        byte[] buffer = [0xAA, 0xBB, 0x02, (byte)'1', 0x03];

        bool extracted = extractor.TryExtractFrame(buffer, out var frame, out int consumed);

        Assert.True(extracted);
        Assert.Equal("1", Encoding.ASCII.GetString(frame));
        Assert.Equal(5, consumed);
    }

    [Theory]
    [InlineData("ST,GS,+025400kg", 25400, "kg", true)]
    [InlineData("US,GS,+025400kg", 25400, "kg", false)]
    [InlineData("ST,NT,12450.5kg", 12450.5, "kg", true)]
    [InlineData("WT: 34000 kg STABLE", 34000, "kg", true)]
    [InlineData("WT: 34000 kg UNSTABLE", 34000, "kg", false)]
    [InlineData("WS+015200", 15200, "kg", true)]
    [InlineData("WN+015200", 15200, "kg", false)]
    [InlineData("+025400 kg", 25400, "kg", false)] // Stability evaluated by StabilityDetector
    [InlineData("-000150 kg", -150, "kg", false)]
    [InlineData("0 kg", 0, "kg", false)]
    [InlineData("0000300", 300, "kg", false)]
    [InlineData("0000350", 350, "kg", false)]
    [InlineData("0000400", 400, "kg", false)]
    public void GenericAsciiProtocolParser_Parses_Documented_Formats(
        string frameText,
        decimal expectedWeight,
        string expectedUnit,
        bool expectedStable)
    {
        var parser = new GenericAsciiProtocolParser();
        byte[] frameBytes = Encoding.ASCII.GetBytes(frameText);
        var timestamp = DateTime.UtcNow;

        bool parsed = parser.TryParse(frameBytes, timestamp, out var reading);

        Assert.True(parsed);
        Assert.Equal(expectedWeight, reading.Value);
        Assert.Equal(expectedUnit, reading.Unit);
        Assert.Equal(expectedStable, reading.IsStable);
        Assert.Equal(WeightSource.Indicator, reading.Source);
        Assert.Equal(timestamp, reading.TimestampUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("INVALID_FRAME_NO_NUMBERS")]
    [InlineData("$$$$$$")]
    public void GenericAsciiProtocolParser_Rejects_Invalid_Frames(string invalidText)
    {
        var parser = new GenericAsciiProtocolParser();
        byte[] frameBytes = Encoding.ASCII.GetBytes(invalidText);

        bool parsed = parser.TryParse(frameBytes, DateTime.UtcNow, out var reading);

        Assert.False(parsed);
        Assert.Equal(WeightReading.Empty, reading);
    }
}
