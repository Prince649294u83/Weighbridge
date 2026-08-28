using Microsoft.Extensions.Logging.Abstractions;
using WeighBridge.Core.Busy;
using WeighBridge.Core.Logging;
using WeighBridge.Services.Busy;
using WeighBridge.Tests.Infrastructure;

namespace WeighBridge.Tests.Busy;

/// <summary>
/// Covers the busy state manager: nesting, release on every exit path, progress
/// reporting, cancellation and the change notifications the shell binds to.
/// </summary>
public sealed class BusyStateServiceTests
{
    private static BusyStateService CreateService()
        => new(
            new TestUiDispatcher(),
            new ApplicationLogger(NullLoggerFactory.Instance, new TestApplicationInfoService()));

    [Fact]
    public async Task BeginAsync_MakesTheApplicationBusy()
    {
        var service = CreateService();

        using var busy = await service.BeginAsync("Saving weighment…");

        Assert.True(service.IsBusy);
        Assert.Equal(1, service.ActiveCount);
        Assert.Equal("Saving weighment…", service.Current!.Title);
    }

    [Fact]
    public async Task Dispose_ReleasesBusyState()
    {
        var service = CreateService();

        using (await service.BeginAsync("Saving weighment…"))
        {
        }

        Assert.False(service.IsBusy);
        Assert.Equal(0, service.ActiveCount);
        Assert.Null(service.Current);
        Assert.Empty(service.Active);
    }

    [Fact]
    public async Task NestedOperations_StayBusyUntilTheOutermostEnds()
    {
        var service = CreateService();

        var save = await service.BeginAsync("Saving weighment…");
        var print = await service.BeginAsync("Printing slip…");

        Assert.Equal(2, service.ActiveCount);

        // The indicator describes what the operator just did, not what started first.
        Assert.Equal("Printing slip…", service.Current!.Title);

        print.Dispose();

        // Still busy: the save is the one that matters and it has not finished.
        Assert.True(service.IsBusy);
        Assert.Equal(1, service.ActiveCount);
        Assert.Equal("Saving weighment…", service.Current!.Title);

        save.Dispose();

        Assert.False(service.IsBusy);
    }

    [Fact]
    public async Task Active_IsOrderedOutermostFirst()
    {
        var service = CreateService();

        using var save = await service.BeginAsync("Saving weighment…");
        using var print = await service.BeginAsync("Printing slip…");

        Assert.Equal(
            ["Saving weighment…", "Printing slip…"],
            service.Active.Select(operation => operation.Title));
    }

    [Fact]
    public async Task NestedOperations_ReleasedOutOfOrder_StillClearBusyState()
    {
        var service = CreateService();

        var save = await service.BeginAsync("Saving weighment…");
        var print = await service.BeginAsync("Printing slip…");

        // A print that outlives the save it was started from is a real shape, and the
        // indicator must not be left stuck by it.
        save.Dispose();
        print.Dispose();

        Assert.False(service.IsBusy);
        Assert.Equal(0, service.ActiveCount);
    }

    [Fact]
    public async Task Exception_StillReleasesBusyState()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var busy = await service.BeginAsync("Saving weighment…");
            throw new InvalidOperationException("The indicator has to be released anyway.");
        });

        // The whole reason BeginAsync hands back a disposable: a flag can be left set, and
        // an application stuck in busy needs restarting.
        Assert.False(service.IsBusy);
        Assert.Equal(0, service.ActiveCount);
    }

    [Fact]
    public async Task Dispose_Twice_DoesNotDoubleRelease()
    {
        var service = CreateService();

        var outer = await service.BeginAsync("Saving weighment…");
        var inner = await service.BeginAsync("Printing slip…");

        inner.Dispose();
        inner.Dispose();

        // A second dispose must not decrement the outer operation's hold away.
        Assert.True(service.IsBusy);
        Assert.Equal(1, service.ActiveCount);

        outer.Dispose();
        Assert.False(service.IsBusy);
    }

    [Fact]
    public async Task Scope_IsCompleted_AfterDispose()
    {
        var service = CreateService();

        var busy = await service.BeginAsync("Saving weighment…");
        Assert.False(busy.IsCompleted);

        busy.Dispose();
        Assert.True(busy.IsCompleted);
    }

    [Fact]
    public async Task BusyStateChanged_ReportsEveryChangeToTheOperationSet()
    {
        var service = CreateService();
        var seen = new List<(bool IsBusy, int ActiveCount, string? Title)>();
        service.BusyStateChanged += (_, args) => seen.Add((args.IsBusy, args.ActiveCount, args.Current?.Title));

        var save = await service.BeginAsync("Saving weighment…");
        var print = await service.BeginAsync("Printing slip…");
        print.Dispose();
        save.Dispose();

        // Every begin and release, because Current and ActiveCount changed each time even
        // where IsBusy did not. IsBusy itself flips exactly twice.
        Assert.Equal(
            [
                (true, 1, "Saving weighment…"),
                (true, 2, "Printing slip…"),
                (true, 1, "Saving weighment…"),
                (false, 0, null),
            ],
            seen);
    }

    [Fact]
    public async Task PropertyChanged_AnnouncesIsBusyOnlyWhenItFlipped()
    {
        var service = CreateService();
        var changed = new List<string?>();
        service.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        var save = await service.BeginAsync("Saving weighment…");
        var print = await service.BeginAsync("Printing slip…");
        print.Dispose();
        save.Dispose();

        // Twice — idle to busy and back. A nested operation must not make the shell
        // re-evaluate a binding that did not change.
        Assert.Equal(2, changed.Count(name => name == nameof(IBusyStateService.IsBusy)));

        // Those two always do change, on every begin and release.
        Assert.Equal(4, changed.Count(name => name == nameof(IBusyStateService.ActiveCount)));
        Assert.Equal(4, changed.Count(name => name == nameof(IBusyStateService.Current)));
    }

    [Fact]
    public async Task PropertyChanged_RaisedForIsBusy()
    {
        var service = CreateService();
        var changed = new List<string?>();
        service.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        using var busy = await service.BeginAsync("Saving weighment…");

        Assert.Contains(nameof(IBusyStateService.IsBusy), changed);
        Assert.Contains(nameof(IBusyStateService.ActiveCount), changed);
    }

    [Fact]
    public async Task NewOperation_IsIndeterminateUntilItReportsProgress()
    {
        var service = CreateService();

        using var busy = await service.BeginAsync("Saving weighment…");

        Assert.True(busy.Operation.IsIndeterminate);

        busy.ReportProgress(40);

        Assert.False(busy.Operation.IsIndeterminate);
        Assert.Equal(40, busy.Operation.Percentage);
    }

    [Fact]
    public async Task ReportProgress_ClampsOutOfRangeValues()
    {
        var service = CreateService();

        using var busy = await service.BeginAsync("Saving weighment…");

        busy.ReportProgress(-10);
        Assert.Equal(0, busy.Operation.Percentage);

        busy.ReportProgress(250);
        Assert.Equal(100, busy.Operation.Percentage);
    }

    [Fact]
    public async Task Report_UpdatesPercentageAndStatusTogether()
    {
        var service = CreateService();

        using var busy = await service.BeginAsync("Saving weighment…");

        busy.Report(60, "Writing the slip…");

        Assert.Equal(60, busy.Operation.Percentage);
        Assert.Equal("Writing the slip…", busy.Operation.Status);
    }

    [Fact]
    public async Task ReportIndeterminate_GoesBackToIndeterminate()
    {
        var service = CreateService();

        using var busy = await service.BeginAsync("Saving weighment…");

        busy.ReportProgress(50);
        busy.ReportIndeterminate("Waiting for the indicator…");

        Assert.True(busy.Operation.IsIndeterminate);
        Assert.Equal("Waiting for the indicator…", busy.Operation.Status);
    }

    [Fact]
    public async Task Cancel_OnCancellableOperation_SignalsTheToken()
    {
        var service = CreateService();

        using var busy = await service.BeginAsync("Weighing…", isCancellable: true);

        busy.Cancel();

        Assert.True(busy.CancellationToken.IsCancellationRequested);
        Assert.True(busy.Operation.IsCancelling);
    }

    [Fact]
    public async Task Cancel_OnNonCancellableOperation_DoesNothing()
    {
        var service = CreateService();

        using var busy = await service.BeginAsync("Saving weighment…");

        busy.Cancel();

        // A save that is halfway through writing rows must not be stoppable just because
        // something called Cancel.
        Assert.False(busy.CancellationToken.IsCancellationRequested);
        Assert.False(busy.Operation.IsCancelling);
    }

    [Fact]
    public async Task CallersToken_CancelsTheScope()
    {
        var service = CreateService();
        using var cancellation = new CancellationTokenSource();

        using var busy = await service.BeginAsync("Weighing…", isCancellable: true, cancellation.Token);

        await cancellation.CancelAsync();

        // Shutdown cancels the operation as well as the operator's cancel button does.
        Assert.True(busy.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Operation_CarriesIdentityAndStartTime()
    {
        var service = CreateService();
        var before = DateTimeOffset.UtcNow;

        using var busy = await service.BeginAsync("Saving weighment…");

        Assert.NotEqual(Guid.Empty, busy.Operation.Id);
        Assert.InRange(busy.Operation.StartedAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task ConcurrentOperations_LeaveTheServiceIdle()
    {
        var service = CreateService();

        await Task.WhenAll(Enumerable.Range(0, 50).Select(async index =>
        {
            using var busy = await service.BeginAsync($"Operation {index}");
            busy.ReportProgress(index);
        }));

        // Fifty overlapping scopes, all released. A lost decrement anywhere shows up here
        // as a service that believes it is still working.
        Assert.False(service.IsBusy);
        Assert.Equal(0, service.ActiveCount);
        Assert.Empty(service.Active);
    }

    [Fact]
    public async Task BeginAsync_BlankTitle_Throws()
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.BeginAsync("  "));
    }
}
