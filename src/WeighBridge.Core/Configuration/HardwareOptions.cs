namespace WeighBridge.Core.Configuration;

/// <summary>
/// Bound to the <c>Hardware</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class HardwareOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Hardware";

    /// <summary>Serial weight indicator settings.</summary>
    public WeightIndicatorOptions WeightIndicator { get; set; } = new();

    /// <summary>Auxiliary serial port settings.</summary>
    public AuxiliaryPortSettings PortSettings { get; set; } = new();
}

/// <summary>Auxiliary receive and send serial port settings.</summary>
public sealed class AuxiliaryPortSettings
{
    public SerialPortEndpointOptions ReceivePort1 { get; set; } = new() { PortName = "COM4", BaudRate = 9600 };
    public SerialPortEndpointOptions ReceivePort2 { get; set; } = new() { PortName = "COM5", BaudRate = 9600 };
    public SerialPortEndpointOptions SendDataPort { get; set; } = new() { PortName = "COM6", BaudRate = 9600 };
}

/// <summary>A single serial port endpoint setting.</summary>
public sealed class SerialPortEndpointOptions
{
    public bool Enabled { get; set; }
    public string PortName { get; set; } = "COM1";
    public int BaudRate { get; set; } = 9600;
}

/// <summary>Serial port and protocol settings for the weight indicator.</summary>
public sealed class WeightIndicatorOptions
{
    /// <summary>Enables the weight indicator integration.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Driver mode: "Serial", "Simulator", or "Disabled". The simulator has to be asked for
    /// by name - it was the default, and the default of a weighbridge must not be a source of
    /// invented weights.
    /// </summary>
    public string DriverType { get; set; } = "Serial";

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

    /// <summary>Name of the indicator protocol parser to use, e.g. "GenericAscii".</summary>
    public string Protocol { get; set; } = "GenericAscii";

    /// <summary>How often the indicator is polled / evaluated, in milliseconds.</summary>
    public int PollIntervalMilliseconds { get; set; } = 250;

    /// <summary>Number of consecutive readings within tolerance required before a weight is marked stable.</summary>
    public int StabilitySampleCount { get; set; } = 5;

    /// <summary>Weight variation tolerance in kg below which consecutive samples are considered stable.</summary>
    public decimal StabilityToleranceKg { get; set; } = 5.0m;

    /// <summary>Minimum time duration in milliseconds the weight must remain within tolerance.</summary>
    public int StabilityDurationMs { get; set; } = 1000;

    /// <summary>Whether Data Terminal Ready (DTR) signal is asserted on the serial port.</summary>
    public bool DtrEnable { get; set; } = true;

    /// <summary>Whether Request To Send (RTS) signal is asserted on the serial port.</summary>
    public bool RtsEnable { get; set; } = true;

    /// <summary>Hardware handshake protocol: None, XOnXOff, RequestToSend, or RequestToSendXOnXOff.</summary>
    public string Handshake { get; set; } = "None";

    /// <summary>Serial read timeout in milliseconds.</summary>
    public int ReadTimeoutMs { get; set; } = 5000;

    /// <summary>Whether to automatically attempt reconnection after a serial disconnect.</summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>Delay between reconnection attempts in milliseconds.</summary>
    public int ReconnectIntervalMs { get; set; } = 1500;

    /// <summary>Unit reported by the indicator, e.g. <c>kg</c>.</summary>
    public string Unit { get; set; } = "kg";

    /// <summary>Options controlling the string-level decoding and interpretation of the raw payload.</summary>
    public WeightDecodeOptions Decoding { get; set; } = new();
}
