namespace WeighBridge.Core.Abstractions;

/// <summary>
/// Event arguments raised when a scale indicator protocol or stream has been successfully locked/detected.
/// </summary>
public sealed class ScaleAutoDetectedEventArgs : EventArgs
{
    /// <summary>
    /// Name or identifier of the recognized protocol (e.g. "Toledo Continuous", "Avery Berkel", "Generic ASCII").
    /// </summary>
    public string Protocol { get; }

    /// <summary>
    /// Baud rate at which the indicator locked onto valid frames.
    /// </summary>
    public int BaudRate { get; }

    /// <summary>
    /// Serial COM port name where the indicator is streaming.
    /// </summary>
    public string PortName { get; }

    /// <summary>
    /// Sample weight value decoded from the initial locking frames, if available.
    /// </summary>
    public decimal? SampleWeight { get; }

    public ScaleAutoDetectedEventArgs(string protocol, int baudRate, string portName, decimal? sampleWeight = null)
    {
        Protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
        BaudRate = baudRate;
        PortName = portName ?? throw new ArgumentNullException(nameof(portName));
        SampleWeight = sampleWeight;
    }
}
