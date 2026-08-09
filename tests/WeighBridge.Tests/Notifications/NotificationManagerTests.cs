using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Diagnostics;
using WeighBridge.Core.Events.Catalog;
using WeighBridge.Core.Notifications;
using WeighBridge.Services.Events;
using WeighBridge.Services.Notifications;
using WeighBridge.Tests.Infrastructure;

namespace WeighBridge.Tests.Notifications;

/// <summary>
/// Covers the notification centre: collection behaviour, the unread badge, auto-dismissal,
/// the capacity caps and the event published for other modules.
/// </summary>
public sealed class NotificationManagerTests
{
    private static NotificationManager CreateManager(
        NotificationOptions? options = null,
        TestUiDispatcher? dispatcher = null,
        EventBus? bus = null)
    {
        var dispatch = dispatcher ?? new TestUiDispatcher();

        return new NotificationManager(
            bus ?? new EventBus(dispatch, NullLogger<EventBus>.Instance),
            dispatch,
            Options.Create(options ?? new NotificationOptions()),
            NullLogger<NotificationManager>.Instance);
    }

    [Fact]
    public void NotifyInformation_AddsToActiveAndHistory()
    {
        using var manager = CreateManager();

        var notification = manager.NotifyInformation("Saved", "Ticket stored", "VehicleEntry");

        Assert.Single(manager.Active);
        Assert.Single(manager.History);
        Assert.Same(notification, manager.Active[0]);
        Assert.Equal(NotificationSeverity.Information, notification.Severity);
        Assert.Equal("VehicleEntry", notification.Module);
    }

    [Fact]
    public void Notify_PutsNewestFirst()
    {
        using var manager = CreateManager();

        manager.NotifyInformation("First", "1");
        manager.NotifyInformation("Second", "2");

        Assert.Equal("Second", manager.Active[0].Title);
        Assert.Equal("First", manager.Active[1].Title);
        Assert.Equal("Second", manager.History[0].Title);
    }

    [Fact]
    public void NotifyError_IsPersistent()
    {
        using var manager = CreateManager();

        var error = manager.NotifyError("Failed", "Could not reach the indicator");

        Assert.True(error.IsPersistent);
        Assert.Equal(TimeSpan.Zero, error.Duration);
    }

    [Fact]
    public void NotifyError_WithException_PutsTheExceptionInDetails()
    {
        using var manager = CreateManager();
        var exception = new InvalidOperationException("port already open");

        var error = manager.NotifyError("Failed", "Could not open the port", exception, "Hardware");

        Assert.NotNull(error.Details);
        Assert.Contains("port already open", error.Details);

        // The operator-facing body stays plain language; the trace goes to the expander.
        Assert.Equal("Could not open the port", error.Message);
    }

    [Fact]
    public void Notify_MarshalsThroughTheDispatcher()
    {
        var dispatcher = new TestUiDispatcher(isOnUiThread: false);
        using var manager = CreateManager(dispatcher: dispatcher);

        manager.NotifyInformation("Saved", "Ticket stored");

        Assert.Equal(1, dispatcher.MarshalledCount);
    }

    [Fact]
    public void Notify_PublishesNotificationRaisedEvent()
    {
        var dispatcher = new TestUiDispatcher();
        var bus = new EventBus(dispatcher, NullLogger<EventBus>.Instance);
        using var manager = CreateManager(dispatcher: dispatcher, bus: bus);

        NotificationRaisedEvent? observed = null;
        using var subscription = bus.Subscribe<NotificationRaisedEvent>(raised => observed = raised);

        var notification = manager.NotifyWarning("Slow", "The indicator is responding slowly");

        Assert.NotNull(observed);
        Assert.Same(notification, observed.Notification);
    }

    [Fact]
    public void Notification_CarriesTheAmbientCorrelationId()
    {
        using var manager = CreateManager();

        using (CorrelationScope.Begin("weigh-in-42"))
        {
            var notification = manager.NotifySuccess("Captured", "Weight recorded");
            Assert.Equal("weigh-in-42", notification.CorrelationId);
        }
    }

    [Fact]
    public void Dismiss_RemovesFromActiveButKeepsHistory()
    {
        using var manager = CreateManager();
        var notification = manager.NotifyError("Failed", "Print failed");

        manager.Dismiss(notification);

        Assert.Empty(manager.Active);
        Assert.Single(manager.History);
        Assert.True(notification.IsDismissed);
    }

    [Fact]
    public void Dismiss_ById_RemovesTheMatchingNotification()
    {
        using var manager = CreateManager();
        var first = manager.NotifyError("A", "1");
        var second = manager.NotifyError("B", "2");

        manager.Dismiss(first.Id);

        Assert.Same(second, Assert.Single(manager.Active));
    }

    [Fact]
    public void Dismiss_ByUnknownId_DoesNothing()
    {
        using var manager = CreateManager();
        manager.NotifyError("A", "1");

        manager.Dismiss(Guid.NewGuid());

        Assert.Single(manager.Active);
    }

    [Fact]
    public void DismissAll_ClearsActiveOnly()
    {
        using var manager = CreateManager();
        manager.NotifyError("A", "1");
        manager.NotifyError("B", "2");

        manager.DismissAll();

        Assert.Empty(manager.Active);
        Assert.Equal(2, manager.History.Count);
    }

    [Fact]
    public void Dismissed_EventFiresOnce()
    {
        using var manager = CreateManager();
        var notification = manager.NotifyError("Failed", "Print failed");

        var count = 0;
        manager.Dismissed += (_, _) => count++;

        manager.Dismiss(notification);
        manager.Dismiss(notification);   // Already gone: must not fire again.

        Assert.Equal(1, count);
    }

    [Fact]
    public void Notified_EventCarriesTheNotification()
    {
        using var manager = CreateManager();

        Notification? observed = null;
        manager.Notified += (_, args) => observed = args.Notification;

        var notification = manager.NotifyInformation("Saved", "Ticket stored");

        Assert.Same(notification, observed);
    }

    [Fact]
    public void UnreadCount_TracksAdditionsAndReads()
    {
        using var manager = CreateManager();

        var first = manager.NotifyError("A", "1");
        manager.NotifyError("B", "2");
        Assert.Equal(2, manager.UnreadCount);

        first.IsRead = true;
        Assert.Equal(1, manager.UnreadCount);
    }

    [Fact]
    public void MarkAllAsRead_ClearsTheBadge()
    {
        using var manager = CreateManager();
        manager.NotifyError("A", "1");
        manager.NotifyError("B", "2");

        manager.MarkAllAsRead();

        Assert.Equal(0, manager.UnreadCount);
        Assert.All(manager.History, notification => Assert.True(notification.IsRead));
    }

    [Fact]
    public void UnreadCount_RaisesPropertyChanged()
    {
        using var manager = CreateManager();

        var raised = new List<string?>();
        manager.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        manager.NotifyError("A", "1");

        Assert.Contains(nameof(INotificationService.UnreadCount), raised);
    }

    [Fact]
    public void ClearHistory_EmptiesHistoryAndBadge()
    {
        using var manager = CreateManager();
        var notification = manager.NotifyError("A", "1");

        manager.ClearHistory();

        Assert.Empty(manager.History);
        Assert.Equal(0, manager.UnreadCount);

        // Unsubscribed, so a late read on a cleared entry cannot corrupt the badge.
        notification.IsRead = true;
        Assert.Equal(0, manager.UnreadCount);
    }

    [Fact]
    public void MaxActive_EvictsTheOldest()
    {
        using var manager = CreateManager(new NotificationOptions { MaxActive = 2 });

        manager.NotifyWarning("A", "1");
        manager.NotifyWarning("B", "2");
        manager.NotifyWarning("C", "3");

        Assert.Equal(2, manager.Active.Count);
        Assert.Equal("C", manager.Active[0].Title);
        Assert.Equal("B", manager.Active[1].Title);

        // Evicted from the screen, still in history.
        Assert.Equal(3, manager.History.Count);
    }

    [Fact]
    public void MaxActive_DoesNotEvictErrorsWhenProtected()
    {
        using var manager = CreateManager(new NotificationOptions
        {
            MaxActive = 2,
            ProtectErrorsFromEviction = true,
        });

        manager.NotifyError("Cause", "The indicator went offline");
        manager.NotifyWarning("Noise", "1");
        manager.NotifyWarning("Noise", "2");

        // The warning is evicted; the error explaining why stays on screen.
        Assert.Equal(2, manager.Active.Count);
        Assert.Contains(manager.Active, notification => notification.Title == "Cause");
    }

    [Fact]
    public void MaxActive_ExceedsTheCapRatherThanDiscardingAnError()
    {
        using var manager = CreateManager(new NotificationOptions
        {
            MaxActive = 1,
            ProtectErrorsFromEviction = true,
        });

        manager.NotifyError("A", "1");
        manager.NotifyError("B", "2");

        Assert.Equal(2, manager.Active.Count);
    }

    [Fact]
    public void MaxActive_EvictsErrorsWhenNotProtected()
    {
        using var manager = CreateManager(new NotificationOptions
        {
            MaxActive = 1,
            ProtectErrorsFromEviction = false,
        });

        manager.NotifyError("A", "1");
        manager.NotifyError("B", "2");

        Assert.Equal("B", Assert.Single(manager.Active).Title);
    }

    [Fact]
    public void MaxHistory_TrimsTheOldestAndCorrectsTheBadge()
    {
        using var manager = CreateManager(new NotificationOptions { MaxHistory = 2 });

        manager.NotifyError("A", "1");
        manager.NotifyError("B", "2");
        manager.NotifyError("C", "3");

        Assert.Equal(2, manager.History.Count);
        Assert.Equal("C", manager.History[0].Title);
        Assert.Equal(2, manager.UnreadCount);
    }

    [Fact]
    public async Task NonPersistentNotification_DismissesItself()
    {
        using var manager = CreateManager();

        var notification = manager.Notify(new Notification(
            NotificationSeverity.Information,
            "Saved",
            "Ticket stored",
            duration: TimeSpan.FromMilliseconds(30)));

        Assert.Single(manager.Active);

        await WaitUntilAsync(() => manager.Active.Count == 0);

        Assert.Empty(manager.Active);
        Assert.True(notification.IsDismissed);
        Assert.Single(manager.History);
    }

    [Fact]
    public async Task ManualDismiss_CancelsThePendingAutoDismiss()
    {
        using var manager = CreateManager();

        var first = manager.Notify(new Notification(
            NotificationSeverity.Information, "A", "1", duration: TimeSpan.FromMilliseconds(30)));

        manager.Dismiss(first);

        // Raised after the manual dismiss, and must survive the first timer firing.
        var second = manager.NotifyError("B", "2");

        await Task.Delay(120);

        Assert.Same(second, Assert.Single(manager.Active));
    }

    [Fact]
    public async Task Dispose_CancelsPendingTimers()
    {
        var manager = CreateManager();

        manager.Notify(new Notification(
            NotificationSeverity.Information, "A", "1", duration: TimeSpan.FromMilliseconds(30)));

        manager.Dispose();

        // A timer firing into a disposed manager must not throw on a pool thread.
        await Task.Delay(120);

        Assert.Single(manager.Active);
    }

    [Fact]
    public void Notify_AfterDispose_Throws()
    {
        var manager = CreateManager();
        manager.Dispose();

        Assert.Throws<ObjectDisposedException>(() => manager.NotifyInformation("A", "1"));
    }

    [Fact]
    public void Notify_RejectsNull()
    {
        using var manager = CreateManager();

        Assert.Throws<ArgumentNullException>(() => manager.Notify(null!));
    }

    [Fact]
    public void Notification_RejectsAnEmptyTitle()
    {
        // A notification with no headline is a bug at the call site, not a blank toast.
        Assert.Throws<ArgumentException>(() =>
            new Notification(NotificationSeverity.Information, "   ", "body"));
    }

    [Fact]
    public void NotificationAction_IsCarriedThrough()
    {
        using var manager = CreateManager();
        var invoked = false;

        var notification = manager.Notify(new Notification(
            NotificationSeverity.Warning,
            "Unsaved changes",
            "The ticket has not been stored",
            action: new NotificationAction("Retry", () => invoked = true)));

        Assert.NotNull(notification.Action);
        Assert.Equal("Retry", notification.Action.Label);
        Assert.True(notification.Action.DismissesNotification);

        notification.Action.Execute();
        Assert.True(invoked);
    }

    /// <summary>Polls until the condition holds, so a slow agent does not fail the test.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(20);
        }
    }
}
