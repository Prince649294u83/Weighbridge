namespace WeighBridge.Core.Configuration;

/// <summary>
/// Options controlling the interpretation of raw numeric payload strings into normalized physical weights.
/// </summary>
public sealed class WeightDecodeOptions
{
    /// <summary>Expected total number of weight digits in payload (default 7). Validates payload length as a hard invariant.</summary>
    public int WeightDigits { get; set; } = 7;

    /// <summary>Number of implied decimal places to apply from the right (default 1, e.g. 0000900 -> 90.0 kg).</summary>
    public int DecimalPlaces { get; set; } = 1;

    /// <summary>Number of characters to trim from the end of the numeric payload before parsing.</summary>
    public int DigitsToRemoveFromEnd { get; set; } = 0;

    /// <summary>Whether the incoming payload string is reversed (e.g. little-endian ASCII transmission).</summary>
    public bool ReversePayload { get; set; } = false;

    /// <summary>Whether to account for or append a dummy zero digit.</summary>
    public bool DummyZero { get; set; } = false;

    /// <summary>Explicit multiplier scaling factor (default 1.0).</summary>
    public decimal ScaleFactor { get; set; } = 1.0m;

    /// <summary>Target normalized unit (e.g. "kg", "t", "lb").</summary>
    public string TargetUnit { get; set; } = "kg";
}
