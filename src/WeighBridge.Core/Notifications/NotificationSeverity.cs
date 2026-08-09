namespace WeighBridge.Core.Notifications;

/// <summary>
/// How urgent a notification is, and therefore how it is coloured and how long it
/// stays on screen.
/// </summary>
/// <remarks>
/// The values match the semantic colour roles of the design system, so a presenter maps
/// severity to a brush without a translation table.
/// </remarks>
public enum NotificationSeverity
{
    /// <summary>Neutral progress or state information. Dismisses itself.</summary>
    Information = 0,

    /// <summary>An operation completed. Dismisses itself.</summary>
    Success = 1,

    /// <summary>
    /// Something needs attention but the operation continued — a printer offline while
    /// a weighment was saved.
    /// </summary>
    Warning = 2,

    /// <summary>
    /// An operation failed. Stays on screen until dismissed: an operator who stepped away
    /// from the terminal must not miss it.
    /// </summary>
    Error = 3,
}
