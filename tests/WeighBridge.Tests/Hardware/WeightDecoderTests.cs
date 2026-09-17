using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Hardware.WeightIndicators;
using Xunit;

namespace WeighBridge.Tests.Hardware;

public sealed class WeightDecoderTests
{
    private readonly WeightDecoder _decoder = new();

    [Theory]
    [InlineData("0000000", 0.0)]
    [InlineData("0000050", 5.0)]
    [InlineData("0000100", 10.0)]
    [InlineData("0000150", 15.0)]
    [InlineData("0000200", 20.0)]
    [InlineData("0000900", 90.0)]
    [InlineData("0000950", 95.0)]
    [InlineData("0001000", 100.0)]
    [InlineData("0001050", 105.0)]
    [InlineData("0001100", 110.0)]
    [InlineData("0001350", 135.0)]
    [InlineData("0001400", 140.0)]
    [InlineData("0001450", 145.0)]
    public void Production_Profile_Decodes_All_Physically_Observed_Frames_Correctly(string rawPayload, decimal expectedKg)
    {
        // Default WeightDecodeOptions has DecimalPlaces = 1 as the production indicator profile
        var options = new WeightDecodeOptions();

        var frame = new ParsedWeightFrame(rawPayload, "kg", null, $"[{rawPayload}\0]");
        Assert.True(_decoder.TryDecode(frame, options, out var decodedWeight));
        Assert.Equal(expectedKg, decodedWeight);
    }

    [Fact]
    public void Production_Profile_Prevents_Unscaled_Magnitude_Errors_Regression_Guard()
    {
        var options = new WeightDecodeOptions();

        // 15 kg must NEVER become 150 kg
        _decoder.TryDecode(new ParsedWeightFrame("0000150", "kg", null, "[0000150\0]"), options, out var w15);
        Assert.NotEqual(150.0m, w15);
        Assert.Equal(15.0m, w15);

        // 90 kg must NEVER become 900 kg
        _decoder.TryDecode(new ParsedWeightFrame("0000900", "kg", null, "[0000900\0]"), options, out var w90);
        Assert.NotEqual(900.0m, w90);
        Assert.Equal(90.0m, w90);

        // 145 kg must NEVER become 1450 kg
        _decoder.TryDecode(new ParsedWeightFrame("0001450", "kg", null, "[0001450\0]"), options, out var w145);
        Assert.NotEqual(1450.0m, w145);
        Assert.Equal(145.0m, w145);
    }

    [Fact]
    public void Decode_Integer_Payload_Without_Implied_Decimal_Yields_Direct_Weight_When_Configured()
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
            ReversePayload = true,
            DecimalPlaces = 0
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
            DummyZero = 1,
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
            DecimalPlaces = 0,
            ScaleFactor = 2.5m
        };

        var frame = new ParsedWeightFrame("0000100", "kg", null, "[0000100\0]");
        Assert.True(_decoder.TryDecode(frame, options, out var decoded));
        Assert.Equal(250m, decoded);
    }
}
