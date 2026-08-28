namespace WeighBridge.Core.Abstractions;

/// <summary>
/// Extracts delimited or framed byte segments from a continuous serial stream.
/// </summary>
public interface IFrameExtractor
{
    /// <summary>
    /// Attempts to extract the next complete frame from the incoming byte buffer.
    /// </summary>
    /// <param name="buffer">The unparsed buffer.</param>
    /// <param name="frame">The extracted frame payload excluding bounding delimiters.</param>
    /// <param name="bytesConsumed">How many bytes to advance the source buffer.</param>
    /// <returns>True if a complete frame was extracted, false if more bytes are required.</returns>
    bool TryExtractFrame(ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> frame, out int bytesConsumed);
}
