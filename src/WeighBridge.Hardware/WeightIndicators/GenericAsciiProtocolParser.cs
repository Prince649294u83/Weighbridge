using System.Globalization;
using System.Text;
using WeighBridge.Core.Abstractions;
using WeighBridge.Domain.Enums;

namespace WeighBridge.Hardware.WeightIndicators;

/// <summary>
/// Parser for explicitly documented generic industrial ASCII weight indicator frames.
/// </summary>
/// <remarks>
/// Supports:
/// 1. Toledo Continuous format: <c>ST,GS,+025400kg</c> or <c>US,GS,+000000kg</c> ("ST"=Stable, "US"=Unstable)
/// 2. Prefix-Status format: <c>WS+025400</c> ("WS"=Stable, "WN"=Unstable / Normal)
/// 3. Keyword format: <c>WT: 25400 kg STABLE</c> / <c>WT: 25400 kg UNSTABLE</c>
/// 4. Numeric signed format: <c>+025400 kg</c>, <c>-000100 kg</c>, <c>25400</c> (stability determined by stability detector)
///
/// Both '.' and ',' are accepted as decimal separators in every format
/// (<c>000012,5kg</c> is twelve and a half kilograms), because several European
/// indicators are configured that way and misreading the value is not recoverable
/// downstream — the wrong number becomes the slip.
/// </remarks>
public sealed class GenericAsciiProtocolParser : IIndicatorProtocolParser
{
    public string Name => "GenericAscii";

    public bool TryParse(ReadOnlySpan<byte> frame, DateTime timestampUtc, out WeightReading reading)
    {
        reading = WeightReading.Empty;

        if (frame.IsEmpty)
        {
            return false;
        }

        string text = Encoding.ASCII.GetString(frame).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Support Yaohua XK3190 equal-sign frame delimiters, quote-bounded frames, and Essae/Industrial '$' headers
        text = text.Trim('"', '=', '$', ' ', '\0', '\r', '\n');
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Format 1: Comma-separated with an alphabetic status token first
        // (e.g. ST,GS,+025400kg or US,GS, 12500 kg). The status token is what makes this
        // the comma-separated format rather than a decimal comma: a frame like
        // "000012,5kg" has digits before the comma and must never be split here, or the
        // last segment ("5kg") would be read as the whole weight.
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && IsStatusToken(parts[0]))
        {
            bool isExplicitlyStable = parts[0].Equals("ST", StringComparison.OrdinalIgnoreCase);

            if (TryParseNumericWeight(parts[^1], out decimal val, out string unit))
            {
                reading = new WeightReading(val, unit, isExplicitlyStable, timestampUtc, WeightSource.Indicator, text);
                return true;
            }
        }

        // Format 2: Keyword format (e.g. WT: 25400 kg STABLE)
        if (text.StartsWith("WT:", StringComparison.OrdinalIgnoreCase))
        {
            bool isExplicitlyStable = text.Contains("STABLE", StringComparison.OrdinalIgnoreCase) &&
                                      !text.Contains("UNSTABLE", StringComparison.OrdinalIgnoreCase);

            string payload = text[3..];
            if (TryParseNumericWeight(payload, out decimal val, out string unit))
            {
                reading = new WeightReading(val, unit, isExplicitlyStable, timestampUtc, WeightSource.Indicator, text);
                return true;
            }
        }

        // Format 3: Status prefix format (e.g. WS+025400 or WN+025400)
        if (text.Length >= 4 && (text.StartsWith("WS", StringComparison.OrdinalIgnoreCase) || text.StartsWith("WN", StringComparison.OrdinalIgnoreCase)))
        {
            bool isExplicitlyStable = text.StartsWith("WS", StringComparison.OrdinalIgnoreCase);
            string numericPart = text[2..];
            if (TryParseNumericWeight(numericPart, out decimal val, out string unit))
            {
                reading = new WeightReading(val, unit, isExplicitlyStable, timestampUtc, WeightSource.Indicator, text);
                return true;
            }
        }

        // Format 4: Pure signed/unsigned numeric with optional unit and either decimal
        // separator (e.g. "+025400 kg", "25400 kg", "12500.5", "12500,5").
        if (TryParseNumericWeight(text, out decimal plainVal, out string plainUnit))
        {
            // When stability flag is not part of the frame, IsStable is false; StabilityDetector will evaluate it
            reading = new WeightReading(plainVal, plainUnit, false, timestampUtc, WeightSource.Indicator, text);
            return true;
        }

        return false;
    }

    /// <summary>
    /// True when the segment looks like a status code (letters only, at most four of
    /// them) rather than the leading digits of a decimal-comma number.
    /// </summary>
    private static bool IsStatusToken(string token)
    {
        if (token.Length is 0 or > 8)
        {
            return false;
        }

        foreach (char c in token)
        {
            if (!char.IsLetter(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Extracts the numeric weight and unit from free-form text.
    /// </summary>
    /// <remarks>
    /// Accepts exactly one sign and exactly one decimal separator ('.' or ','); anything
    /// more ambiguous than that fails rather than being guessed at. Spaces between the
    /// number and its unit are tolerated.
    /// </remarks>
    private static bool TryParseNumericWeight(string text, out decimal weight, out string unit)
    {
        weight = 0m;
        unit = "kg";

        var number = new StringBuilder(text.Length);
        var unitBuilder = new StringBuilder();
        int signCount = 0;
        int separatorCount = 0;
        bool numberStarted = false;

        foreach (char c in text)
        {
            if (char.IsDigit(c))
            {
                numberStarted = true;
                number.Append(c);
            }
            else if (c is '-' or '+')
            {
                // A sign is only meaningful at the very start of the number.
                if (numberStarted || ++signCount > 1)
                {
                    return false;
                }

                number.Append(c);
            }
            else if (c is '.' or ',')
            {
                // One decimal separator, wherever the instrument puts it.
                if (++separatorCount > 1)
                {
                    return false;
                }

                numberStarted = true;
                number.Append('.');
            }
            else if (char.IsWhiteSpace(c) || c is '=' or '"' or '$')
            {
                continue;
            }
            else if (c is '/')
            {
                // In Essae ES0D14 and Indian weighbridge digitizer protocols, '/' (ASCII 47)
                // represents a blank/suppressed digit or leading zero (e.g. "$ ////0" = 0 kg, "$ //250" = 250 kg).
                numberStarted = true;
                number.Append('0');
            }
            else if (char.IsLetter(c))
            {
                unitBuilder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                // Any other punctuation means this is not a plain weight field.
                return false;
            }
        }

        string numStr = number.ToString();
        if (numStr.Length == 0 ||
            !decimal.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out weight))
        {
            return false;
        }

        string extractedUnit = unitBuilder.ToString().Trim();
        if (extractedUnit is "kg" or "t" or "tn" or "lb" or "g")
        {
            unit = extractedUnit == "tn" ? "t" : extractedUnit;
        }

        return true;
    }
}
