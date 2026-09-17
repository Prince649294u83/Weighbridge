namespace WeighBridge.Core.Abstractions;

/// <summary>
/// Authoritative date and time formatting service driven by application runtime configuration.
/// </summary>
public interface IDateTimeFormatter
{
    /// <summary>
    /// Formats a time according to the active 12-hour or 24-hour setting (e.g. "04:35:12 PM" vs "16:35:12").
    /// </summary>
    string FormatTime(DateTime dateTime);

    /// <summary>
    /// Formats a short time according to the active 12-hour or 24-hour setting (e.g. "04:35 PM" vs "16:35").
    /// </summary>
    string FormatShortTime(DateTime dateTime);

    /// <summary>
    /// Formats a date using the standard application date pattern (e.g. "31/08/2026" or "31 Aug 2026").
    /// </summary>
    string FormatDate(DateTime dateTime);

    /// <summary>
    /// Formats a full date and time according to active settings.
    /// </summary>
    string FormatDateTime(DateTime dateTime);

    /// <summary>
    /// Event raised when the time format setting changes at runtime.
    /// </summary>
    event EventHandler? FormatChanged;
}
