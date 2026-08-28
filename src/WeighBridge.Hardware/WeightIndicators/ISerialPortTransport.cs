namespace WeighBridge.Hardware.WeightIndicators;

/// <summary>
/// Abstraction over serial port transport to allow deterministic testing and isolation from hardware.
/// </summary>
public interface ISerialPortTransport : IAsyncDisposable
{
    /// <summary>True when the serial port is currently open.</summary>
    bool IsOpen { get; }

    /// <summary>Port name (e.g. "COM1").</summary>
    string PortName { get; }

    /// <summary>Opens the serial connection.</summary>
    Task OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes the serial connection.</summary>
    Task CloseAsync();

    /// <summary>Reads available bytes into the provided buffer asynchronously.</summary>
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
}
