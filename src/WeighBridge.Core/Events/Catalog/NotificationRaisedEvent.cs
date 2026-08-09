using WeighBridge.Core.Notifications;

namespace WeighBridge.Core.Events.Catalog;

/// <summary>
/// Announces that a notification was raised.
/// </summary>
/// <remarks>
/// Lets a subscriber observe operator-facing messages without depending on
/// <see cref="INotificationService"/> — an audit log recording what the operator was told,
/// or a future server sync forwarding errors to a central console.
/// </remarks>
public sealed class NotificationRaisedEvent(Notification notification, string? source = null)
    : ApplicationEvent(source)
{
    /// <summary>The notification that was raised.</summary>
    public Notification Notification { get; } = notification;
}
