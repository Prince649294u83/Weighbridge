using WeighBridge.Domain.Enums;

namespace WeighBridge.Core.Abstractions;

/// <summary>
/// A single reading captured from the weight indicator.
/// </summary>
/// <param name="Value">Weight in <paramref name="Unit"/>.</param>
/// <param name="Unit">Unit of measure, e.g. <c>kg</c>.</param>
/// <param name="IsStable">True when the indicator reports a settled reading.</param>
/// <param name="TimestampUtc">When the reading was captured.</param>
/// <param name="RawFrame">The raw protocol frame, retained for diagnostics.</param>
public readonly record struct WeightReading(
    decimal Value,
    string Unit,
    bool IsStable,
    DateTime TimestampUtc,
    string? RawFrame = null)
{
    /// <summary>A zeroed, unstable reading used before the indicator connects.</summary>
    public static WeightReading Empty { get; } = new(0m, "kg", false, DateTime.MinValue);
}

/// <summary>
/// Reads live weight from the serial indicator.
/// </summary>
/// <remarks>
/// Module 0.1 registers a disconnected placeholder implementation. The Weight
/// Indicator module will supply the real serial protocol parsers behind this same
/// interface, so no consumer needs to change.
/// </remarks>
public interface IWeightIndicatorService : IHealthCheck, IAsyncDisposable
{
    /// <summary>Current connection state of the indicator.</summary>
    ConnectionState State { get; }

    /// <summary>The most recent reading, or <see cref="WeightReading.Empty"/>.</summary>
    WeightReading CurrentReading { get; }

    /// <summary>Raised whenever a new frame is decoded.</summary>
    event EventHandler<WeightReading>? ReadingReceived;

    /// <summary>Raised when the connection state changes.</summary>
    event EventHandler<ConnectionState>? StateChanged;

    /// <summary>Opens the serial port and starts polling.</summary>
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops polling and closes the serial port.</summary>
    Task DisconnectAsync();

    /// <summary>Requests a single reading on demand.</summary>
    Task<WeightReading> ReadAsync(CancellationToken cancellationToken = default);
}
