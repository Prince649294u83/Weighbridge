using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Hardware.WeightIndicators;

/// <summary>
/// Pure, deterministic string-level weight decoder that applies indicator profile transformations
/// before converting the payload to numeric physical weights.
/// </summary>
public sealed class WeightDecoder : IWeightDecoder
{
    private readonly ILogger<WeightDecoder> _logger;

    public WeightDecoder(ILogger<WeightDecoder>? logger = null)
    {
        _logger = logger ?? NullLogger<WeightDecoder>.Instance;
    }

    /// <inheritdoc />
    public bool TryDecode(ParsedWeightFrame frame, WeightDecodeOptions options, out decimal decodedWeight)
    {
        decodedWeight = 0m;

        if (frame is null || string.IsNullOrWhiteSpace(frame.RawNumericPayload))
        {
            return false;
        }

        options ??= new WeightDecodeOptions();

        var payload = frame.RawNumericPayload.Trim();

        // STEP 1: Payload Length & Character Invariant Check
        if (options.WeightDigits > 0 && payload.Length != options.WeightDigits)
        {
            _logger.LogDebug(
                "Payload '{Payload}' failed length invariant (expected {Expected}, actual {Actual})",
                payload, options.WeightDigits, payload.Length);
            return false;
        }

        foreach (var c in payload)
        {
            if (!char.IsDigit(c) && c != '.' && c != '-' && c != '+')
            {
                _logger.LogDebug("Payload '{Payload}' contains invalid character '{Char}'", payload, c);
                return false;
            }
        }

        // STEP 2: Optional String Reversal
        if (options.ReversePayload)
        {
            var charArray = payload.ToCharArray();
            Array.Reverse(charArray);
            payload = new string(charArray);
        }

        // STEP 3: Optional Trailing Digit Removal
        if (options.DigitsToRemoveFromEnd > 0)
        {
            if (payload.Length <= options.DigitsToRemoveFromEnd)
            {
                _logger.LogDebug(
                    "Payload '{Payload}' too short to remove {Count} trailing digits",
                    payload, options.DigitsToRemoveFromEnd);
                return false;
            }

            payload = payload[..^options.DigitsToRemoveFromEnd];
        }

        // STEP 4: Optional Dummy Zero Handling
        if (options.DummyZero != 0)
        {
            payload += "0";
        }

        // STEP 5: Decimal Point Placement & Numeric Parsing
        if (options.DecimalPlaces > 0 && !payload.Contains('.'))
        {
            if (payload.Length <= options.DecimalPlaces)
            {
                payload = payload.PadLeft(options.DecimalPlaces + 1, '0');
            }

            int insertPos = payload.Length - options.DecimalPlaces;
            payload = payload.Insert(insertPos, ".");
        }

        if (!decimal.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
        {
            _logger.LogDebug("Failed to parse transformed payload '{Payload}' into decimal", payload);
            return false;
        }

        // STEP 6: Scale Factor & Normalization
        if (options.ScaleFactor != 1.0m && options.ScaleFactor != 0m)
        {
            parsedValue *= options.ScaleFactor;
        }

        // Negative zero-drift clamp: scale tare or sensor drift below 0 must reset to zero
        if (parsedValue < 0m)
        {
            parsedValue = 0m;
        }

        decodedWeight = parsedValue;

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace(
                "[WeightDecoder] Raw='{Raw}' -> Decoded={Decoded} {Unit} (ScaleFactor={Scale}, Decimals={Decimals})",
                frame.RawNumericPayload, decodedWeight, options.TargetUnit, options.ScaleFactor, options.DecimalPlaces);
        }

        return true;
    }
}
