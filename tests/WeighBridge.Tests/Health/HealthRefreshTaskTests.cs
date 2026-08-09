using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Health;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Tasks;
using WeighBridge.Domain.Enums;
using WeighBridge.Services.Events;
using WeighBridge.Services.Health;
using WeighBridge.Services.Tasks;
using WeighBridge.Tests.Infrastructure;
using Xunit;

namespace WeighBridge.Tests.Health;

/// <summary>
/// Proves the monitor's automatic refresh works through the background task manager rather
/// than a timer of its own — the reason Component 5 has no scheduler in it.
/// </summary>
public sealed class HealthRefreshTaskTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly TestUiDispatcher _dispatcher = new();
    private readonly EventBus _bus;
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(builder => { });
    private readonly HealthMonitorService _monitor;
    private readonly BackgroundTaskManager _manager;

    public HealthRefreshTaskTests()
    {
        _bus = new EventBus(_dispatcher, Microsoft.Extensions.Logging.Abstractions
            .NullLogger<EventBus>.Instance);

        var logger = new ApplicationLogger(_loggerFactory, new TestApplicationInfoService());

        _monitor = new HealthMonitorService(_bus, _dispatcher, logger);

        _manager = new BackgroundTaskManager(
            _bus,
            _dispatcher,
            logger,
            Options.Create(new BackgroundTaskManagerOptions()));
    }

    public void Dispose()
    {
        _manager.Dispose();
        _monitor.Dispose();
        _loggerFactory.Dispose();
    }

    [Fact]
    public void TheTask_DeclaresItsScheduleAndRunsAtLowPriority()
    {
        var task = new HealthRefreshTask(
            _monitor,
            Options.Create(new HealthMonitorOptions { IntervalSeconds = 30 }));

        Assert.Equal(TimeSpan.FromSeconds(30), task.Interval);

        // A diagnostic probe must never take a concurrency slot from work an operator is
        // waiting on at the weighbridge.
        Assert.Equal(BackgroundTaskPriority.Low, task.Priority);
    }

    [Fact]
    public async Task RegisteredWithTheManager_ItProbesOnItsSchedule()
    {
        var check = new StubHealthCheck();
        var entry = _monitor.Register(check);

        var task = new HealthRefreshTask(
            _monitor,
            Options.Create(new HealthMonitorOptions { IntervalSeconds = 5 }));

        var registration = _manager.Register(task);
        Assert.True(registration.Options.IsRecurring);

        // Runs immediately on start, so the first probe needs no wait on the interval.
        var probed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _monitor.HealthChanged += (_, _) => probed.TrySetResult();

        _manager.Start(task.Name);
        await probed.Task.WaitAsync(Timeout);
        await _manager.StopAsync(task.Name);

        Assert.Equal(HealthStatus.Healthy, entry.Status);
        Assert.True(check.Probes >= 1);
    }

    [Fact]
    public async Task StoppingTheTask_StopsTheProbing()
    {
        var check = new StubHealthCheck(state: ConnectionState.Disconnected);
        _monitor.Register(check);

        var task = new HealthRefreshTask(
            _monitor,
            Options.Create(new HealthMonitorOptions { IntervalSeconds = 5 }));

        var registration = _manager.Register(task);

        var probed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _monitor.HealthChanged += (_, _) => probed.TrySetResult();

        _manager.Start(task.Name);
        await probed.Task.WaitAsync(Timeout);

        Assert.True(await _manager.StopAsync(task.Name));

        // The whole point of not owning a timer: shutdown is the manager's single path, and
        // a cancelled pass is recorded as cancelled rather than as a completed run.
        Assert.Equal(BackgroundTaskState.Cancelled, registration.State);
        Assert.Equal(0, registration.ConsecutiveFailures);
    }
}
