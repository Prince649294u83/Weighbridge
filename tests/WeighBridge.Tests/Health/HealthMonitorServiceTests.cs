using Microsoft.Extensions.Logging;
using WeighBridge.Core.Events.Catalog;
using WeighBridge.Core.Health;
using WeighBridge.Core.Logging;
using WeighBridge.Domain.Enums;
using WeighBridge.Services.Events;
using WeighBridge.Services.Health;
using WeighBridge.Tests.Infrastructure;
using Xunit;

namespace WeighBridge.Tests.Health;

/// <summary>
/// Covers registration, probing, status transitions and the overall roll-up.
/// </summary>
public sealed class HealthMonitorServiceTests : IDisposable
{
    private readonly TestUiDispatcher _dispatcher = new();
    private readonly EventBus _bus;
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(builder => { });
    private readonly HealthMonitorService _monitor;

    public HealthMonitorServiceTests()
    {
        _bus = new EventBus(_dispatcher, Microsoft.Extensions.Logging.Abstractions
            .NullLogger<EventBus>.Instance);

        _monitor = new HealthMonitorService(
            _bus,
            _dispatcher,
            new ApplicationLogger(_loggerFactory, new TestApplicationInfoService()));
    }

    public void Dispose()
    {
        _monitor.Dispose();
        _loggerFactory.Dispose();
    }

    [Fact]
    public void Register_AddsAnEntryThatHasNotBeenProbedYet()
    {
        var entry = _monitor.Register(new StubHealthCheck());

        Assert.Single(_monitor.Entries);
        Assert.Equal(HealthStatus.Unknown, entry.Status);
        Assert.Null(entry.LastChecked);
        Assert.Same(entry, _monitor.Find("stub-check"));
    }

    [Fact]
    public void Register_RejectsADuplicateName()
    {
        _monitor.Register(new StubHealthCheck("printer"));

        Assert.Throws<InvalidOperationException>(
            () => _monitor.Register(new StubHealthCheck("printer")));
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        var entry = _monitor.Register(new StubHealthCheck("Printer"));

        // The name reaches this method from configuration and from log lines, so matching
        // has to survive a difference in casing.
        Assert.Same(entry, _monitor.Find("printer"));
    }

    [Fact]
    public void Find_ReturnsNullForAnUnknownName()
        => Assert.Null(_monitor.Find("nothing-registered"));

    [Fact]
    public void Unregister_RemovesTheEntry()
    {
        _monitor.Register(new StubHealthCheck());

        Assert.True(_monitor.Unregister("stub-check"));
        Assert.Empty(_monitor.Entries);
        Assert.Null(_monitor.Find("stub-check"));
    }

    [Fact]
    public void Unregister_ReturnsFalseForAnUnknownName()
        => Assert.False(_monitor.Unregister("nothing-registered"));

    [Fact]
    public async Task RefreshAsync_RecordsAHealthyProbe()
    {
        var entry = _monitor.Register(new StubHealthCheck());

        await _monitor.RefreshAsync();

        Assert.Equal(HealthStatus.Healthy, entry.Status);
        Assert.Equal("stub-check is Connected", entry.Detail);
        Assert.NotNull(entry.LastChecked);
        Assert.NotNull(entry.StatusSince);
        Assert.Equal(0, entry.ConsecutiveFailures);
        Assert.False(entry.IsAlerting);
    }

    [Fact]
    public async Task RefreshAsync_MapsEachConnectionStateOntoAVerdict()
    {
        var check = new StubHealthCheck();
        var entry = _monitor.Register(check);

        // The mapping is what every consumer downstream reads, so each arm is pinned.
        foreach (var (state, expected) in new[]
        {
            (ConnectionState.Connected, HealthStatus.Healthy),
            (ConnectionState.Degraded, HealthStatus.Warning),
            (ConnectionState.Disconnected, HealthStatus.Offline),
            (ConnectionState.Disabled, HealthStatus.Disabled),
            (ConnectionState.Connecting, HealthStatus.Unknown),
        })
        {
            check.State = state;
            await _monitor.RefreshAsync();

            Assert.Equal(expected, entry.Status);
        }
    }

    [Fact]
    public async Task RefreshAsync_ProbesEveryRegisteredCheck()
    {
        var first = new StubHealthCheck("first");
        var second = new StubHealthCheck("second");

        _monitor.Register(first);
        _monitor.Register(second);

        await _monitor.RefreshAsync();

        Assert.Equal(1, first.Probes);
        Assert.Equal(1, second.Probes);
    }

    [Fact]
    public async Task RefreshAsync_ByName_ProbesOnlyThatCheck()
    {
        var first = new StubHealthCheck("first");
        var second = new StubHealthCheck("second");

        _monitor.Register(first);
        _monitor.Register(second);

        Assert.True(await _monitor.RefreshAsync("first"));

        Assert.Equal(1, first.Probes);
        Assert.Equal(0, second.Probes);
    }

    [Fact]
    public async Task RefreshAsync_ByName_ReturnsFalseForAnUnknownName()
        => Assert.False(await _monitor.RefreshAsync("nothing-registered"));

    [Fact]
    public async Task AThrowingCheck_IsRecordedAsOfflineAndDoesNotEndThePass()
    {
        var throwing = new StubHealthCheck("throwing") { Throws = new InvalidOperationException("boom") };
        var healthy = new StubHealthCheck("healthy");

        var throwingEntry = _monitor.Register(throwing);
        var healthyEntry = _monitor.Register(healthy);

        await _monitor.RefreshAsync();

        // One badly behaved check must not cost the verdict on every other subsystem.
        Assert.Equal(HealthStatus.Offline, throwingEntry.Status);
        Assert.Equal("boom", throwingEntry.Detail);
        Assert.Equal(HealthStatus.Healthy, healthyEntry.Status);
    }

    [Fact]
    public async Task ConsecutiveFailures_CountUpAndResetOnRecovery()
    {
        var check = new StubHealthCheck(state: ConnectionState.Disconnected);
        var entry = _monitor.Register(check);

        await _monitor.RefreshAsync();
        await _monitor.RefreshAsync();

        Assert.Equal(2, entry.ConsecutiveFailures);

        check.State = ConnectionState.Connected;
        await _monitor.RefreshAsync();

        Assert.Equal(0, entry.ConsecutiveFailures);
    }

    [Fact]
    public async Task StatusSince_TracksTheChangeRatherThanTheLastProbe()
    {
        var check = new StubHealthCheck();
        var entry = _monitor.Register(check);

        await _monitor.RefreshAsync();
        var firstSeen = entry.StatusSince;

        await _monitor.RefreshAsync();

        // Same verdict twice: the operator has been told nothing new, so the clock on the
        // current status must not restart. "Offline for two hours" is the useful reading.
        Assert.Equal(firstSeen, entry.StatusSince);
        Assert.NotNull(entry.LastChecked);
    }

    [Fact]
    public async Task HealthChanged_FiresOnlyWhenTheStatusMoves()
    {
        List<HealthChangedEventArgs> observed = [];

        var check = new StubHealthCheck();
        _monitor.Register(check);
        _monitor.HealthChanged += (_, e) => observed.Add(e);

        await _monitor.RefreshAsync();
        await _monitor.RefreshAsync();

        var single = Assert.Single(observed);
        Assert.Equal(HealthStatus.Unknown, single.PreviousStatus);
        Assert.Equal(HealthStatus.Healthy, single.CurrentStatus);
    }

    [Fact]
    public async Task ARecovery_IsFlaggedAsOne()
    {
        List<HealthChangedEventArgs> observed = [];

        var check = new StubHealthCheck(state: ConnectionState.Disconnected);
        _monitor.Register(check);
        _monitor.HealthChanged += (_, e) => observed.Add(e);

        await _monitor.RefreshAsync();

        check.State = ConnectionState.Connected;
        await _monitor.RefreshAsync();

        Assert.Equal(2, observed.Count);
        Assert.False(observed[0].IsRecovery);
        Assert.True(observed[1].IsRecovery);
    }

    [Fact]
    public async Task AStatusChange_IsPublishedOnTheBus()
    {
        List<HealthStatusChangedEvent> observed = [];

        using var subscription = _bus.Subscribe<HealthStatusChangedEvent>(e => observed.Add(e));

        _monitor.Register(new StubHealthCheck());
        await _monitor.RefreshAsync();

        var published = Assert.Single(observed);
        Assert.Equal("stub-check", published.CheckName);
        Assert.Equal(HealthStatus.Unknown, published.PreviousStatus);
        Assert.Equal(HealthStatus.Healthy, published.CurrentStatus);
        Assert.True(published.IsRecovery);
    }

    [Fact]
    public void Overall_IsUnknownWhenNothingIsRegistered()
        => Assert.Equal(HealthStatus.Unknown, _monitor.Overall);

    [Fact]
    public async Task Overall_ReportsTheWorstStatus()
    {
        _monitor.Register(new StubHealthCheck("healthy"));
        _monitor.Register(new StubHealthCheck("degraded", ConnectionState.Degraded));

        await _monitor.RefreshAsync();

        Assert.Equal(HealthStatus.Warning, _monitor.Overall);

        _monitor.Register(new StubHealthCheck("down", ConnectionState.Disconnected));
        await _monitor.RefreshAsync();

        Assert.Equal(HealthStatus.Offline, _monitor.Overall);
    }

    [Fact]
    public async Task Overall_IsNotDraggedDownByADisabledSubsystem()
    {
        _monitor.Register(new StubHealthCheck("healthy"));
        _monitor.Register(new StubHealthCheck("off", ConnectionState.Disabled));

        await _monitor.RefreshAsync();

        // A camera switched off in configuration is not an outage.
        Assert.Equal(HealthStatus.Healthy, _monitor.Overall);
    }

    [Fact]
    public async Task RefreshAsync_ObservesCancellation()
    {
        var check = new StubHealthCheck { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var entry = _monitor.Register(check);

        using var cancellation = new CancellationTokenSource();
        var refresh = _monitor.RefreshAsync(cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);

        // Cancelling is shutdown, not an outage: it must not leave a false failure behind.
        Assert.Equal(HealthStatus.Unknown, entry.Status);
        Assert.Equal(0, entry.ConsecutiveFailures);
    }

    [Fact]
    public async Task RefreshAsync_ThrowsOnceDisposed()
    {
        var monitor = new HealthMonitorService(
            _bus,
            _dispatcher,
            new ApplicationLogger(_loggerFactory, new TestApplicationInfoService()));

        monitor.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => monitor.RefreshAsync());
    }
}
