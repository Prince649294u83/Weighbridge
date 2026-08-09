namespace WeighBridge.Core.Notifications;

/// <summary>
/// An optional button carried by a notification — "Retry", "View slip", "Open log".
/// </summary>
/// <remarks>
/// The callback runs on the user-interface thread, so it may navigate or open a dialog
/// directly. It is a plain delegate rather than an <c>ICommand</c> because a notification
/// travels through layers that have no WPF reference.
/// </remarks>
/// <param name="Label">Text on the button.</param>
/// <param name="Execute">Invoked when the operator activates it.</param>
/// <param name="DismissesNotification">
/// When true the notification is dismissed after the callback runs, which is right for a
/// navigating action and wrong for one the operator may want to repeat.
/// </param>
public sealed record NotificationAction(
    string Label,
    Action Execute,
    bool DismissesNotification = true);
