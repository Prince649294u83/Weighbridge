using WeighBridge.Core.Abstractions;

namespace WeighBridge.Hardware.WeightIndicators;

/// <summary>
/// Extracts frames from raw byte streams using standard delimiters:
/// 1. STX (0x02) ... ETX (0x03)
/// 2. CRLF (\r\n) or LF (\n)
/// 3. Bare CR (\r)
/// </summary>
/// <remarks>
/// Whichever terminator completes first in the buffer wins. An earlier revision looked
/// for STX before anything else, which meant a complete line-delimited frame sitting in
/// front of an unterminated STX frame was discarded as junk — a real measurement lost
/// because a different instrument's half-arrived frame happened to be ahead of it.
/// </remarks>
public sealed class DelimitedFrameExtractor : IFrameExtractor
{
    private const int RunawayLimit = 1024;
    private const int RetainedTail = 256;

    private const byte Stx = 0x02;
    private const byte Etx = 0x03;
    private const byte Quote = 0x22;        // '"'
    private const byte BracketStart = 0x5B; // '['
    private const byte BracketEnd = 0x5D;   // ']'
    private const byte NullTerminator = 0x00;
    private const byte Cr = 0x0D;
    private const byte Lf = 0x0A;

    public bool TryExtractFrame(ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> frame, out int bytesConsumed)
    {
        frame = default;
        bytesConsumed = 0;

        if (buffer.IsEmpty)
        {
            return false;
        }

        int bracketIndex = buffer.IndexOf(BracketStart);
        int stxIndex = buffer.IndexOf(Stx);
        int lfIndex = buffer.IndexOf(Lf);
        int quoteIndex = buffer.IndexOf(Quote);

        // Quote-delimited indicator format: '"' (0x22) payload '"' (e.g. "  66315  ")
        if (quoteIndex >= 0 && (bracketIndex < 0 || quoteIndex < bracketIndex) && (stxIndex < 0 || quoteIndex < stxIndex) && (lfIndex < 0 || quoteIndex < lfIndex))
        {
            int relativeClosingQuote = buffer.Slice(quoteIndex + 1).IndexOf(Quote);
            if (relativeClosingQuote >= 0)
            {
                int closingQuoteIndex = quoteIndex + 1 + relativeClosingQuote;
                frame = buffer.Slice(quoteIndex + 1, relativeClosingQuote);
                bytesConsumed = closingQuoteIndex + 1;
                return true;
            }

            // Unterminated quote frame: discard preceding junk if any
            if (quoteIndex > 0)
            {
                bytesConsumed = quoteIndex;
            }

            return false;
        }

        // Bracket-framed indicator format: '[' (0x5B) followed by payload and terminated by \0, \r, \n, or ']'
        if (bracketIndex >= 0 && (stxIndex < 0 || bracketIndex < stxIndex) && (lfIndex < 0 || bracketIndex < lfIndex))
        {
            var afterBracket = buffer.Slice(bracketIndex + 1);
            int termIndex = -1;
            for (int i = 0; i < afterBracket.Length; i++)
            {
                byte b = afterBracket[i];
                if (b is NullTerminator or Cr or Lf or BracketEnd)
                {
                    termIndex = i;
                    break;
                }
            }

            if (termIndex >= 0)
            {
                frame = afterBracket[..termIndex];
                bytesConsumed = bracketIndex + 1 + termIndex + 1;
                if (afterBracket[termIndex] == Cr && termIndex + 1 < afterBracket.Length && afterBracket[termIndex + 1] == Lf)
                {
                    bytesConsumed++;
                }
                return true;
            }

            if (bracketIndex > 0)
            {
                bytesConsumed = bracketIndex;
            }

            return false;
        }

        // A line that completes before any STX frame does is extracted first.
        if (lfIndex >= 0 && (stxIndex < 0 || lfIndex < stxIndex))
        {
            return ExtractLine(buffer, terminatorIndex: lfIndex, out frame, out bytesConsumed);
        }

        if (stxIndex >= 0)
        {
            int relativeEtx = buffer.Slice(stxIndex + 1).IndexOf(Etx);

            if (relativeEtx >= 0)
            {
                int etxIndex = stxIndex + 1 + relativeEtx;

                frame = buffer.Slice(stxIndex + 1, relativeEtx);
                bytesConsumed = etxIndex + 1;
                return true;
            }

            // Support continuous STX-prefixed frames where each frame begins with STX
            // and has no ETX (e.g. STX " 390 STX " 975 STX ...)
            int relativeNextStx = buffer.Slice(stxIndex + 1).IndexOf(Stx);
            if (relativeNextStx >= 0)
            {
                frame = buffer.Slice(stxIndex + 1, relativeNextStx);
                bytesConsumed = stxIndex + 1 + relativeNextStx;
                return true;
            }

            // STX frames terminated by newline
            int relativeLf = buffer.Slice(stxIndex + 1).IndexOf(Lf);
            if (relativeLf >= 0)
            {
                int end = relativeLf;
                if (end > 0 && buffer[stxIndex + 1 + end - 1] == Cr)
                {
                    end--;
                }
                frame = buffer.Slice(stxIndex + 1, end);
                bytesConsumed = stxIndex + 1 + relativeLf + 1;
                return true;
            }

            // Unterminated STX frame. Junk accumulated ahead of it is dropped so the
            // buffer cannot grow without bound while the payload is still arriving.
            if (stxIndex > 0)
            {
                bytesConsumed = stxIndex;
            }

            // Otherwise: waiting for ETX, next STX, or line ending.
            return false;
        }

        // No STX anywhere. A bare CR — one not immediately followed by LF, which the
        // branch above would already have taken — terminates its line by itself.
        int crIndex = buffer.IndexOf(Cr);
        if (crIndex >= 0 && crIndex < buffer.Length - 1 && buffer[crIndex + 1] != Lf)
        {
            return ExtractLine(buffer, terminatorIndex: crIndex, out frame, out bytesConsumed);
        }

        // Cap runaway buffers when junk accumulates with no delimiter at all: keep only
        // the tail, which may hold the start of a frame still being received.
        if (buffer.Length > RunawayLimit)
        {
            bytesConsumed = buffer.Length - RetainedTail;
        }

        return false;
    }

    /// <summary>
    /// Extracts the line ending at <paramref name="terminatorIndex"/> (an LF or a bare
    /// CR), stripping a trailing CR from a CRLF pair.
    /// </summary>
    private static bool ExtractLine(
        ReadOnlySpan<byte> buffer,
        int terminatorIndex,
        out ReadOnlySpan<byte> frame,
        out int bytesConsumed)
    {
        int frameEnd = terminatorIndex;
        if (frameEnd > 0 && buffer[frameEnd - 1] == Cr)
        {
            frameEnd--;
        }

        frame = buffer[..frameEnd];
        bytesConsumed = terminatorIndex + 1;
        return true;
    }
}
