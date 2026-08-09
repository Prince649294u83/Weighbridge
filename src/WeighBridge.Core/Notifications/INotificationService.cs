using System.Collections.ObjectModel;
using System.ComponentModel;

namespace WeighBridge.Core.Notifications;

/// <summary>
/// Raises operator-facing messages without knowing how they are shown.
/// </summary>
/// <remarks>
/// <para>
/// A module calls <see cref="NotifySuccess"/> or <see cref="NotifyError(string, string, string?, string?)"/>
/// and is done. Whether that becomes a toast in the corner, a snackbar, a line in the
/// status bar or an entry in a notification centre is decided entirely by the presenter
/// the shell registers — and can be changed without touching a single module.
/// </para>
/// <para>
/// Safe to call from any thread. The implementation marshals onto the user-interface
/// thread before touching the bound collections, so a background task reporting a failure
/// does not have to know it is on the wrong thread.
/// </para>
/// </remarks>
public interface INotificationService : INotifyPropertyChanged
{
    /// <summary>
    /// Notifications currently on screen, newest first.
    /// </summary>
    /// <remarks>
    /// Read-only to the consumer but change-notifying, so a view binds to it directly
    /// while a module is still obliged to go through the service to add anything.
    /// </remarks>
    ReadOnlyObservableCollection<Notification> Active { get; }

    /// <summary>Everything raised this session, newest first, capped at a bounded depth.</summary>
    ReadOnlyObservableCollection<Notification> History { get; }

    /// <summary>Number of history entries the operator has not read. Drives the badge.</summary>
    int UnreadCount { get; }

    /// <summary>Raised when a notification is added to the active list.</summary>
    event EventHandler<NotificationEventArgs>? Notified;

    /// <summary>Raised when a notification leaves the active list.</summary>
    event EventHandler<NotificationEventArgs>? Dismissed;

    /// <summary>Shows a neutral informational message.</summary>
    Notification NotifyInformation(string title, string message, string? module = null);

    /// <summary>Shows a success message.</summary>
    Notification NotifySuccess(string title, string message, string? module = null);

    /// <summary>Shows a warning.</summary>
    Notification NotifyWarning(string title, string message, string? module = null, string? details = null);

    /// <summary>
    /// Shows an error. Persistent by default, so an operator away from the terminal
    /// still sees it on their return.
    /// </summary>
    Notification NotifyError(string title, string message, string? module = null, string? details = null);

    /// <summary>
    /// Shows an error described by an exception, putting the exception text in the detail
    /// area rather than in the body — an operator needs the plain-language message, and a
    /// stack trace in a toast helps nobody.
    /// </summary>
    Notification NotifyError(string title, string message, Exception exception, string? module = null);

    /// <summary>Raises a fully specified notification.</summary>
    Notification Notify(Notification notification);

    /// <summary>
    /// Removes a notification from the active list. It stays in history — an operator
    /// reviewing what happened during a shift needs the dismissed ones too.
    /// </summary>
    void Dismiss(Notification notification);

    /// <summary>Removes a notification by identity.</summary>
    void Dismiss(Guid notificationId);

    /// <summary>Clears the active list.</summary>
    void DismissAll();

    /// <summary>Marks every history entry as read, clearing the unread badge.</summary>
    void MarkAllAsRead();

    /// <summary>Empties history. Does not touch the active list.</summary>
    void ClearHistory();
}

/// <summary>Carries the notification a service event concerns.</summary>
public sealed class NotificationEventArgs(Notification notification) : EventArgs
{
    /// <summary>The notification that was raised or dismissed.</summary>
    public Notification Notification { get; } = notification;
}
