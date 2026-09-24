using WeighBridge.Core.Configuration;

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

    /// <summary>Hardware Clear-To-Send pin state.</summary>
    bool CtsHolding => false;

    /// <summary>Hardware Data-Set-Ready pin state.</summary>
    bool DsrHolding => false;

    /// <summary>Hardware Carrier-Detect pin state.</summary>
    bool CdHolding => false;

    /// <summary>Writes bytes to the serial transport.</summary>
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>
    /// Dynamically retunes the physical serial transport to a new baud rate
    /// using in-place Win32 DCB modification without closing or churning the handle.
    /// </summary>
    Task<bool> RetuneAsync(int baudRate, CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <summary>
    /// Updates transport configuration parameters dynamically in-memory.
    /// </summary>
    void UpdateOptions(WeightIndicatorOptions newOptions) { }
}
