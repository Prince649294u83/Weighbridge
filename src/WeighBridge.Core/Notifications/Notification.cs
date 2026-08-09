using WeighBridge.Core.Diagnostics;
using WeighBridge.Core.Mvvm;

namespace WeighBridge.Core.Notifications;

/// <summary>
/// One message raised by a module and shown to the operator by whatever presenter the
/// shell provides.
/// </summary>
/// <remarks>
/// <para>
/// Observable because the presenter binds to it directly and
/// <see cref="IsRead"/> changes after it has been created. The content properties are
/// immutable: a module describes what happened once, and nothing may rewrite history.
/// </para>
/// <para>
/// The raising module never learns how the notification is displayed — toast, snackbar,
/// status bar or notification centre are all the presenter's business.
/// </para>
/// </remarks>
public sealed class Notification : ObservableObject
{
    private bool _isRead;
    private bool _isDismissed;

    /// <summary>Creates a notification.</summary>
    /// <param name="severity">Urgency, which drives colour and default duration.</param>
    /// <param name="title">Short headline.</param>
    /// <param name="message">Body text.</param>
    /// <param name="module">Raising module, shown as provenance and used for filtering.</param>
    /// <param name="duration">
    /// How long before it dismisses itself. <c>null</c> takes the default for the
    /// severity; <see cref="TimeSpan.Zero"/> or less means it stays until dismissed.
    /// </param>
    /// <param name="action">Optional button.</param>
    /// <param name="details">
    /// Technical detail — an exception, a device response — shown in an expander rather
    /// than in the body.
    /// </param>
    public Notification(
        NotificationSeverity severity,
        string title,
        string message,
        string? module = null,
        TimeSpan? duration = null,
        NotificationAction? action = null,
        string? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Id = Guid.NewGuid();
        Timestamp = DateTimeOffset.Now;
        Severity = severity;
        Title = title;
        Message = message ?? string.Empty;
        Module = module;
        Details = details;
        Action = action;
        Duration = duration ?? DefaultDurationFor(severity);
        CorrelationId = CorrelationScope.Current;
    }

    /// <summary>Unique identity, used to dismiss a specific notification.</summary>
    public Guid Id { get; }

    /// <summary>When the notification was raised.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Urgency of the message.</summary>
    public NotificationSeverity Severity { get; }

    /// <summary>Short headline.</summary>
    public string Title { get; }

    /// <summary>Body text.</summary>
    public string Message { get; }

    /// <summary>Raising module, when it identified itself.</summary>
    public string? Module { get; }

    /// <summary>Technical detail for an expander.</summary>
    public string? Details { get; }

    /// <summary>Optional button.</summary>
    public NotificationAction? Action { get; }

    /// <summary>
    /// Time on screen before automatic dismissal. Zero or less means it persists until
    /// the operator dismisses it.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>Correlation identifier of the operation that raised it, when any.</summary>
    public string? CorrelationId { get; }

    /// <summary>True when the notification never dismisses itself.</summary>
    public bool IsPersistent => Duration <= TimeSpan.Zero;

    /// <summary>Local time, formatted for the notification list.</summary>
    public string DisplayTime => Timestamp.ToString("HH:mm:ss");

    /// <summary>True once the operator has seen it. Drives the unread badge.</summary>
    public bool IsRead
    {
        get => _isRead;
        set => SetProperty(ref _isRead, value);
    }

    /// <summary>
    /// True once removed from the active list. It stays in history — an operator
    /// reviewing what happened during a shift needs the dismissed ones too.
    /// </summary>
    public bool IsDismissed
    {
        get => _isDismissed;
        private set => SetProperty(ref _isDismissed, value);
    }

    /// <summary>
    /// Records that the notification has left the active list.
    /// </summary>
    /// <remarks>
    /// A method rather than a settable property, and one-way rather than a toggle: the
    /// notification service owns this transition, and nothing may un-dismiss a notification
    /// the operator has already dealt with. The setter cannot be <c>internal</c> because
    /// the service lives in another assembly.
    /// </remarks>
    public void MarkDismissed() => IsDismissed = true;

    /// <inheritdoc />
    public override string ToString() => $"[{Severity}] {Title}: {Message}";

    /// <summary>
    /// Default time on screen for a severity.
    /// </summary>
    /// <remarks>
    /// An error is persistent by design. On a weighbridge terminal the operator is
    /// frequently away from the screen dealing with a vehicle, and a failure that
    /// disappeared after four seconds would be a failure nobody ever saw.
    /// </remarks>
    private static TimeSpan DefaultDurationFor(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Information => TimeSpan.FromSeconds(4),
        NotificationSeverity.Success => TimeSpan.FromSeconds(4),
        NotificationSeverity.Warning => TimeSpan.FromSeconds(8),
        _ => TimeSpan.Zero,
    };
}
