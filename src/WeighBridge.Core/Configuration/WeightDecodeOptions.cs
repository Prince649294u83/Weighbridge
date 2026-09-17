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

    private int _dummyZero;

    /// <summary>Whether to account for or append a dummy zero digit (0 = off, 1 = on).</summary>
    /// <remarks>
    /// Older configuration files may store this as a JSON boolean (<c>false</c>/<c>true</c>)
    /// rather than the expected integer 0/1. The configuration binder raises
    /// <see cref="System.FormatException"/> when it tries to convert "False" to Int32.
    /// This is handled by the <c>PostConfigure</c> normalisation in
    /// <c>CoreServiceCollectionExtensions</c>; the property itself is a plain int.
    /// </remarks>
    public int DummyZero
    {
        get => _dummyZero;
        set => _dummyZero = value;
    }

    /// <summary>Explicit multiplier scaling factor (default 1.0).</summary>
    public decimal ScaleFactor { get; set; } = 1.0m;

    /// <summary>Target normalized unit (e.g. "kg", "t", "lb").</summary>
    public string TargetUnit { get; set; } = "kg";

    /// <summary>Ending character/string for frame termination (e.g. "NUL (0x00)").</summary>
    public string EndingString { get; set; } = "NUL (0x00)";

    /// <summary>Whether values are transmitted as hexadecimal.</summary>
    public bool HexValue { get; set; } = false;

    /// <summary>Whether the indicator is an Essae ES0D14 model (special protocol handling).</summary>
    public bool EssaeMode { get; set; } = false;

    /// <summary>Whether to use RTS/CTS hardware flow control on the indicator serial line.</summary>
    public bool RtsCts { get; set; } = false;

    /// <summary>Buffer data delay in milliseconds before processing the serial payload.</summary>
    public int BufferData { get; set; } = 50;

    /// <summary>Wait time in milliseconds for weight to stabilise before accepting a reading.</summary>
    public int StableWaitTime { get; set; } = 0;
}
