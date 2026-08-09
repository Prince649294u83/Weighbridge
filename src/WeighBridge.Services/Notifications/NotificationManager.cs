using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Events;
using WeighBridge.Core.Events.Catalog;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Notifications;
using WeighBridge.Core.Threading;

namespace WeighBridge.Services.Notifications;

/// <summary>
/// The notification centre: the single place a message from any module is collected,
/// timed out, remembered and handed to whatever presenter the shell provides.
/// </summary>
/// <remarks>
/// <para>
/// Every mutation runs on the user-interface thread. The collections are bound to by the
/// shell, and WPF only permits a bound collection to be modified from the thread that
/// created it — so a failure reported from a serial-port polling thread is marshalled here
/// rather than at every call site.
/// </para>
/// <para>
/// Auto-dismissal is a per-notification timer rather than a single sweep. A sweep would
/// need to run several times a second to feel responsive; a timer costs nothing while it
/// waits and disappears when the notification is dismissed by hand.
/// </para>
/// </remarks>
public sealed class NotificationManager : ObservableObject, INotificationService, IDisposable
{
    private readonly IEventPublisher _eventPublisher;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<NotificationManager> _logger;
    private readonly NotificationOptions _options;

    private readonly ObservableCollection<Notification> _active = [];
    private readonly ObservableCollection<Notification> _history = [];

    /// <summary>Auto-dismiss timers, keyed by notification, so a manual dismiss cancels one.</summary>
    private readonly Dictionary<Guid, CancellationTokenSource> _timers = [];

    private int _unreadCount;
    private bool _disposed;

    /// <summary>Creates the notification centre.</summary>
    public NotificationManager(
        IEventPublisher eventPublisher,
        IUiDispatcher dispatcher,
        IOptions<NotificationOptions> options,
        ILogger<NotificationManager> logger)
    {
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new NotificationOptions();

        Active = new ReadOnlyObservableCollection<Notification>(_active);
        History = new ReadOnlyObservableCollection<Notification>(_history);
    }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<Notification> Active { get; }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<Notification> History { get; }

    /// <inheritdoc />
    public int UnreadCount
    {
        get => _unreadCount;
        private set => SetProperty(ref _unreadCount, value);
    }

    /// <inheritdoc />
    public event EventHandler<NotificationEventArgs>? Notified;

    /// <inheritdoc />
    public event EventHandler<NotificationEventArgs>? Dismissed;

    /// <inheritdoc />
    public Notification NotifyInformation(string title, string message, string? module = null)
        => Notify(new Notification(NotificationSeverity.Information, title, message, module));

    /// <inheritdoc />
    public Notification NotifySuccess(string title, string message, string? module = null)
        => Notify(new Notification(NotificationSeverity.Success, title, message, module));

    /// <inheritdoc />
    public Notification NotifyWarning(string title, string message, string? module = null, string? details = null)
        => Notify(new Notification(NotificationSeverity.Warning, title, message, module, details: details));

    /// <inheritdoc />
    public Notification NotifyError(string title, string message, string? module = null, string? details = null)
        => Notify(new Notification(NotificationSeverity.Error, title, message, module, details: details));

    /// <inheritdoc />
    public Notification NotifyError(string title, string message, Exception exception, string? module = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return NotifyError(title, message, module, exception.ToString());
    }

    /// <inheritdoc />
    public Notification Notify(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Logged before display. A notification the operator dismissed in two seconds is
        // still evidence when the shift is reconstructed from the log afterwards.
        Log(notification);

        _dispatcher.Post(() => Add(notification));

        // Published outside the dispatcher hop so a subscriber sees it immediately rather
        // than behind whatever is queued on the UI thread.
        _eventPublisher.Publish(new NotificationRaisedEvent(notification, nameof(NotificationManager)));

        return notification;
    }

    /// <inheritdoc />
    public void Dismiss(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        _dispatcher.Post(() => Remove(notification));
    }

    /// <inheritdoc />
    public void Dismiss(Guid notificationId)
        => _dispatcher.Post(() =>
        {
            var match = _active.FirstOrDefault(candidate => candidate.Id == notificationId);

            if (match is not null)
            {
                Remove(match);
            }
        });

    /// <inheritdoc />
    public void DismissAll()
        => _dispatcher.Post(() =>
        {
            foreach (var notification in _active.ToArray())
            {
                Remove(notification);
            }
        });

    /// <inheritdoc />
    public void MarkAllAsRead()
        => _dispatcher.Post(() =>
        {
            foreach (var notification in _history)
            {
                notification.IsRead = true;
            }

            UnreadCount = 0;
        });

    /// <inheritdoc />
    public void ClearHistory()
        => _dispatcher.Post(() =>
        {
            foreach (var notification in _history)
            {
                notification.PropertyChanged -= OnNotificationPropertyChanged;
            }

            _history.Clear();
            UnreadCount = 0;
            _logger.LogDebug("Notification history cleared");
        });

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Cancels every pending auto-dismiss, so a timer cannot fire into a torn-down
        // application during shutdown.
        foreach (var timer in _timers.Values)
        {
            timer.Cancel();
            timer.Dispose();
        }

        _timers.Clear();
    }

    /// <summary>Adds to both lists and starts the auto-dismiss timer. UI thread.</summary>
    private void Add(Notification notification)
    {
        if (_disposed)
        {
            return;
        }

        _active.Insert(0, notification);

        notification.PropertyChanged += OnNotificationPropertyChanged;
        _history.Insert(0, notification);

        if (!notification.IsRead)
        {
            UnreadCount++;
        }

        TrimHistory();
        EvictExcessActive();

        Notified?.Invoke(this, new NotificationEventArgs(notification));

        if (!notification.IsPersistent)
        {
            StartAutoDismiss(notification);
        }
    }

    /// <summary>Removes from the active list, leaving history intact. UI thread.</summary>
    private void Remove(Notification notification)
    {
        CancelAutoDismiss(notification.Id);

        if (!_active.Remove(notification))
        {
            return;
        }

        notification.MarkDismissed();
        Dismissed?.Invoke(this, new NotificationEventArgs(notification));
    }

    /// <summary>
    /// Dismisses the notification once its duration elapses.
    /// </summary>
    /// <remarks>
    /// A cancelled delay is the normal path, not an error: it means the operator dismissed
    /// the notification first, which is exactly what should happen.
    /// </remarks>
    private void StartAutoDismiss(Notification notification)
    {
        var cancellation = new CancellationTokenSource();
        _timers[notification.Id] = cancellation;

        _ = DismissAfterDelayAsync(notification, cancellation.Token);
    }

    private async Task DismissAfterDelayAsync(Notification notification, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(notification.Duration, cancellationToken).ConfigureAwait(false);

            _dispatcher.Post(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Remove(notification);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Dismissed by hand, or the application is shutting down.
        }
        catch (Exception ex)
        {
            // Awaited by nobody, so an unguarded failure here would surface much later as
            // an unobserved task exception with no connection to this notification.
            _logger.LogError(ex, "Auto-dismiss of notification {Id} failed", notification.Id);
        }
    }

    private void CancelAutoDismiss(Guid notificationId)
    {
        if (_timers.Remove(notificationId, out var cancellation))
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    /// <summary>
    /// Keeps the active list within its cap by dismissing the oldest entries.
    /// </summary>
    /// <remarks>
    /// Errors are skipped when configured to be: a burst of warnings from a device that
    /// has just gone offline must not push the error explaining why off the screen.
    /// </remarks>
    private void EvictExcessActive()
    {
        if (_options.MaxActive <= 0)
        {
            return;
        }

        while (_active.Count > _options.MaxActive)
        {
            var victim = FindEvictionCandidate();

            if (victim is null)
            {
                // Everything on screen is protected. Better to exceed the cap than to
                // discard an error the operator has not acknowledged.
                return;
            }

            Remove(victim);
        }
    }

    private Notification? FindEvictionCandidate()
    {
        for (var index = _active.Count - 1; index >= 0; index--)
        {
            var candidate = _active[index];

            if (!_options.ProtectErrorsFromEviction || candidate.Severity != NotificationSeverity.Error)
            {
                return candidate;
            }
        }

        return null;
    }

    private void TrimHistory()
    {
        if (_options.MaxHistory <= 0)
        {
            return;
        }

        while (_history.Count > _options.MaxHistory)
        {
            var oldest = _history[^1];
            oldest.PropertyChanged -= OnNotificationPropertyChanged;
            _history.RemoveAt(_history.Count - 1);

            if (!oldest.IsRead && UnreadCount > 0)
            {
                UnreadCount--;
            }
        }
    }

    /// <summary>
    /// Keeps <see cref="UnreadCount"/> correct when a presenter marks one notification as
    /// read, without the presenter needing to tell the service it did so.
    /// </summary>
    private void OnNotificationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Notification.IsRead) || sender is not Notification notification)
        {
            return;
        }

        if (notification.IsRead && UnreadCount > 0)
        {
            UnreadCount--;
        }
        else if (!notification.IsRead)
        {
            UnreadCount++;
        }
    }

    /// <summary>Mirrors a notification into the log at the matching level.</summary>
    private void Log(Notification notification)
    {
        var level = notification.Severity switch
        {
            NotificationSeverity.Error => LogLevel.Error,
            NotificationSeverity.Warning => LogLevel.Warning,
            _ => LogLevel.Information,
        };

        _logger.Log(
            level,
            "Notification [{Severity}] {Title}: {Message} (module {Module}, correlation {CorrelationId}){Details}",
            notification.Severity,
            notification.Title,
            notification.Message,
            notification.Module ?? "(none)",
            notification.CorrelationId ?? "(none)",
            notification.Details is null ? string.Empty : Environment.NewLine + notification.Details);
    }
}
