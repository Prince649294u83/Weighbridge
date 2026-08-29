using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Hardware.WeightIndicators;
using Xunit;

namespace WeighBridge.Tests.Hardware;

public sealed class WeightDecoderTests
{
    private readonly WeightDecoder _decoder = new();

    [Fact]
    public void Decode_Baseline_Integer_Frames_Preserves_Expected_Weights_With_Default_Options()
    {
        var defaultOptions = new WeightDecodeOptions();

        // Proven live baseline frames
        Assert.True(_decoder.TryDecode(new ParsedWeightFrame("0001450", "kg", null, "[0001450\0]"), defaultOptions, out var w1450));
        Assert.Equal(1450m, w1450);

        Assert.True(_decoder.TryDecode(new ParsedWeightFrame("0001400", "kg", null, "[0001400\0]"), defaultOptions, out var w1400));
        Assert.Equal(1400m, w1400);

        Assert.True(_decoder.TryDecode(new ParsedWeightFrame("0001350", "kg", null, "[0001350\0]"), defaultOptions, out var w1350));
        Assert.Equal(1350m, w1350);

        Assert.True(_decoder.TryDecode(new ParsedWeightFrame("0000300", "kg", null, "[0000300\0]"), defaultOptions, out var w300));
        Assert.Equal(300m, w300);

        Assert.True(_decoder.TryDecode(new ParsedWeightFrame("0000000", "kg", null, "[0000000\0]"), defaultOptions, out var w0));
        Assert.Equal(0m, w0);
    }

    [Fact]
    public void Decode_Candidate_Implied_Decimal_Frame_Yields_Scaled_Weight()
    {
        var options = new WeightDecodeOptions
        {
            WeightDigits = 7,
            DecimalPlaces = 1
        };

        var frame = new ParsedWeightFrame("0000900", "kg", null, "[0000900\0]");
        Assert.True(_decoder.TryDecode(frame, options, out var decoded));
        Assert.Equal(90.0m, decoded);
    }

    [Fact]
    public void Decode_Integer_Payload_Without_Implied_Decimal_Yields_Direct_Weight()
    {
        var options = new WeightDecodeOptions
        {
            WeightDigits = 7,
            DecimalPlaces = 0
        };

        var frame = new ParsedWeightFrame("0000090", "kg", null, "[0000090\0]");
        Assert.True(_decoder.TryDecode(frame, options, out var decoded));
        Assert.Equal(90.0m, decoded);
    }

    [Fact]
    public void Decode_Rejects_Malformed_Lengths_When_WeightDigits_Invariant_Is_Enforced()
    {
        var options = new WeightDecodeOptions { WeightDigits = 7 };

        // Too short (6 digits)
        Assert.False(_decoder.TryDecode(new ParsedWeightFrame("000090", "kg", null, "[000090\0]"), options, out _));

        // Too long (8 digits)
        Assert.False(_decoder.TryDecode(new ParsedWeightFrame("00009000", "kg", null, "[00009000\0]"), options, out _));
    }

    [Fact]
    public void Decode_Rejects_Non_Numeric_Characters()
    {
        var options = new WeightDecodeOptions { WeightDigits = 7 };

        Assert.False(_decoder.TryDecode(new ParsedWeightFrame("00009A0", "kg", null, "[00009A0\0]"), options, out _));
        Assert.False(_decoder.TryDecode(new ParsedWeightFrame("0000?00", "kg", null, "[0000?00\0]"), options, out _));
    }

    [Fact]
    public void Decode_Reverses_String_Payload_When_Configured()
    {
        var options = new WeightDecodeOptions
        {
            WeightDigits = 7,
            ReversePayload = true
        };

        var frame = new ParsedWeightFrame("0001234", "kg", null, "[0001234\0]");
        Assert.True(_decoder.TryDecode(frame, options, out var decoded));
        Assert.Equal(4321000m, decoded);
    }

    [Fact]
    public void Decode_Removes_Trailing_Digits_When_Configured()
    {
        var options = new WeightDecodeOptions
        {
            WeightDigits = 7,
            DigitsToRemoveFromEnd = 1,
            DecimalPlaces = 0
        };

        var frame = new ParsedWeightFrame("0000900", "kg", null, "[0000900\0]");
        Assert.True(_decoder.TryDecode(frame, options, out var decoded));
        Assert.Equal(90m, decoded); // "0000900" -> "000090" -> 90m
    }

    [Fact]
    public void Decode_Applies_Dummy_Zero_When_Configured()
    {
        var options = new WeightDecodeOptions
        {
            WeightDigits = 6,
            DummyZero = true,
            DecimalPlaces = 0
        };

        var frame = new ParsedWeightFrame("000009", "kg", null, "[000009\0]");
        Assert.True(_decoder.TryDecode(frame, options, out var decoded));
        Assert.Equal(90m, decoded); // "000009" -> "0000090" -> 90m
    }

    [Fact]
    public void Decode_Applies_Custom_Scale_Factor_When_Configured()
    {
        var options = new WeightDecodeOptions
        {
            WeightDigits = 7,
            ScaleFactor = 2.5m
        };

        var frame = new ParsedWeightFrame("0000100", "kg", null, "[0000100\0]");
        Assert.True(_decoder.TryDecode(frame, options, out var decoded));
        Assert.Equal(250m, decoded);
    }

    [Fact]
    public void Default_Decoder_Options_Never_Applies_Unconfigured_Scaling_Regression_Guard()
    {
        var defaultOptions = new WeightDecodeOptions();

        Assert.Equal(7, defaultOptions.WeightDigits);
        Assert.Equal(0, defaultOptions.DecimalPlaces);
        Assert.Equal(0, defaultOptions.DigitsToRemoveFromEnd);
        Assert.False(defaultOptions.ReversePayload);
        Assert.False(defaultOptions.DummyZero);
        Assert.Equal(1.0m, defaultOptions.ScaleFactor);

        Assert.True(_decoder.TryDecode(new ParsedWeightFrame("0001450", "kg", null, "[0001450\0]"), defaultOptions, out var decoded));
        Assert.Equal(1450m, decoded);
    }
}
