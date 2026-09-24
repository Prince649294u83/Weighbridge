using System.Globalization;

namespace WeighBridge.Domain.Weighments;

/// <summary>
/// The one place that knows what a slip number looks like.
/// </summary>
/// <remarks>
/// <para>
/// Operators, drivers and auditors quote slip numbers; the database quotes primary keys.
/// Both are needed, so a weighment carries both — and the format of the human-readable one
/// is defined here rather than being written out at each of the dozen places that display,
/// print, search for or export it.
/// </para>
/// <para>
/// The sequence is derived from the database identity. That makes the number unique and
/// monotonic under normal committed inserts without a separate counter row to lock.
/// </para>
/// <para>
/// ponytail: one continuous series. A series that restarts each financial year, or one per
/// weighbridge in a multi-bridge site, needs its own counter and a wider format — change
/// <see cref="Format"/> and <see cref="TryParse"/> together and leave existing numbers
/// alone, because a printed slip cannot be reformatted after the fact.
/// </para>
/// </remarks>
public static class SlipNumbers
{
    /// <summary>Prefix previously used for slip numbers (retained for backward compatibility).</summary>
    public const string LegacyPrefix = "WB-";

    /// <summary>Current prefix is empty for numeric-only tickets.</summary>
    public const string Prefix = "";

    /// <summary>Digits the sequence is padded to. Longer sequences are not truncated.</summary>
    public const int Digits = 6;

    /// <summary>
    /// Column width to allow. Generous on purpose: the format outgrowing its column is a
    /// failure that arrives at slip 1,000,000 with a truck on the platform.
    /// </summary>
    public const int MaxLength = 24;

    /// <summary>Renders a sequence number as a slip number, for example <c>000042</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The sequence is not positive.</exception>
    public static string Format(long sequence)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence),
                sequence,
                "A slip number sequence starts at 1.");
        }

        return sequence.ToString(new string('0', Digits), CultureInfo.InvariantCulture);
    }

    /// <summary>Reads the sequence back out of a slip number, accepting both modern (000042) and legacy (WB-000042) formats.</summary>
    /// <returns><c>true</c> when <paramref name="value"/> is a well-formed slip number.</returns>
    public static bool TryParse(string? value, out long sequence)
    {
        sequence = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var span = value.Trim().AsSpan();

        if (span.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            span = span[LegacyPrefix.Length..];
        }
        else if (span.StartsWith("WB", StringComparison.OrdinalIgnoreCase))
        {
            span = span[2..];
        }

        return long.TryParse(
                   span,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out sequence)
               && sequence > 0;
    }

    /// <summary>
    /// Turns whatever the operator typed into a slip number, so a search for <c>42</c>,
    /// <c>000042</c>, <c>wb-42</c> or <c>WB-000042</c> all find <c>000042</c>.
    /// </summary>
    /// <returns>The canonical slip number, or <c>null</c> when the input is not one.</returns>
    public static string? Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (TryParse(value, out var sequence))
        {
            return Format(sequence);
        }

        return null;
    }
}
