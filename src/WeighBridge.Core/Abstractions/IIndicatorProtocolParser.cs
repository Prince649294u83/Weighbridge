namespace WeighBridge.Core.Abstractions;

/// <summary>
/// Parses an extracted byte frame from an indicator protocol into a strongly-typed <see cref="WeightReading"/>.
/// </summary>
public interface IIndicatorProtocolParser
{
    /// <summary>Identifier of the protocol (e.g. "GenericAscii", "Toledo", "Avery").</summary>
    string Name { get; }

    /// <summary>
    /// Attempts to parse an extracted frame into a valid weight reading.
    /// </summary>
    /// <param name="frame">The raw frame bytes.</param>
    /// <param name="timestampUtc">The timestamp to stamp on the reading.</param>
    /// <param name="reading">The parsed weight reading if successful.</param>
    /// <returns>True if the frame was valid and parsed successfully, false otherwise.</returns>
    bool TryParse(ReadOnlySpan<byte> frame, DateTime timestampUtc, out WeightReading reading);
}
