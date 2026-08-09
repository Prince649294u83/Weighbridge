namespace WeighBridge.Core.Notifications;

/// <summary>
/// Tuning for the notification centre.
/// </summary>
/// <remarks>
/// Not bound to a configuration section yet. It exists so the caps are named constants in
/// one place rather than literals inside the manager, and so a future Notifications
/// section binds to it without a signature change.
/// </remarks>
public sealed class NotificationOptions
{
    /// <summary>Configuration section this class binds to when one is added.</summary>
    public const string SectionName = "Notifications";

    /// <summary>
    /// Most notifications shown at once. Older ones are dismissed to make room, so a
    /// burst of failures from a disconnected device cannot bury the screen.
    /// </summary>
    public int MaxActive { get; set; } = 5;

    /// <summary>
    /// History depth. Bounded because this application runs for months at a time and an
    /// unbounded list is a slow leak.
    /// </summary>
    public int MaxHistory { get; set; } = 200;

    /// <summary>
    /// When true, an error notification is never evicted by the
    /// <see cref="MaxActive"/> cap — only by the operator dismissing it.
    /// </summary>
    public bool ProtectErrorsFromEviction { get; set; } = true;
}
