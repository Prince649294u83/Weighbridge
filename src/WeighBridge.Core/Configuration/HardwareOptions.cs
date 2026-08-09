namespace WeighBridge.Core.Configuration;

/// <summary>
/// Bound to the <c>Hardware</c> section of <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// These values are consumed by the Weight Indicator module. Module 0.1 only reads
/// them so the placeholder service can report a meaningful "configured but not
/// connected" state.
/// </remarks>
public sealed class HardwareOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Hardware";

    /// <summary>Serial weight indicator settings.</summary>
    public WeightIndicatorOptions WeightIndicator { get; set; } = new();
}

/// <summary>Serial port and protocol settings for the weight indicator.</summary>
public sealed class WeightIndicatorOptions
{
    /// <summary>Enables the weight indicator integration.</summary>
    public bool Enabled { get; set; }

    /// <summary>Serial port name, e.g. <c>COM1</c>.</summary>
    public string PortName { get; set; } = "COM1";

    /// <summary>Baud rate of the indicator.</summary>
    public int BaudRate { get; set; } = 9600;

    /// <summary>Data bits per frame.</summary>
    public int DataBits { get; set; } = 8;

    /// <summary>Parity setting: None, Odd, Even, Mark or Space.</summary>
    public string Parity { get; set; } = "None";

    /// <summary>Stop bits: One, OnePointFive or Two.</summary>
    public string StopBits { get; set; } = "One";

    /// <summary>Name of the indicator protocol parser to use.</summary>
    public string Protocol { get; set; } = "Generic";

    /// <summary>How often the indicator is polled, in milliseconds.</summary>
    public int PollIntervalMilliseconds { get; set; } = 250;

    /// <summary>Number of consecutive equal readings required before a weight is stable.</summary>
    public int StabilitySampleCount { get; set; } = 5;

    /// <summary>Unit reported by the indicator, e.g. <c>kg</c>.</summary>
    public string Unit { get; set; } = "kg";
}
