using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Events.Catalog;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Tasks;
using WeighBridge.Services.Events;
using WeighBridge.Services.Tasks;
using WeighBridge.Tests.Infrastructure;
using Xunit;

namespace WeighBridge.Tests.Tasks;

/// <summary>
/// Covers the manager's lifecycle: start, stop, pause, resume, progress, priority,
/// exception isolation and automatic restart.
/// </summary>
/// <remarks>
/// Every wait is on a signal with a timeout, never a sleep. A test that sleeps passes on a
/// fast machine and fails in CI, and the failure looks like a product bug.
/// </remarks>
public sealed class BackgroundTaskManagerTests : IDisposable
{
    /// <summary>Long enough to be reliable under load, short enough that a hang fails fast.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly TestUiDispatcher _dispatcher = new();
    private readonly EventBus _bus;
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(builder => { });
    private readonly BackgroundTaskManager _manager;

    public BackgroundTaskManagerTests()
    {
        _bus = new EventBus(_dispatcher, Microsoft.Extensions.Logging.Abstractions
            .NullLogger<EventBus>.Instance);

        _manager = new BackgroundTaskManager(
            _bus,
            _dispatcher,
            new ApplicationLogger(_loggerFactory, new TestApplicationInfoService()),
            Options.Create(new BackgroundTaskManagerOptions()));
    }

    public void Dispose()
    {
        _manager.Dispose();
        _loggerFactory.Dispose();
    }

    /// <summary>
    /// Runs <paramref name="start"/> and waits for <paramref name="name"/> to reach
    /// <paramref name="state"/>.
    /// </summary>
    /// <remarks>
    /// The subscription is in place before the task is started, so a state reached
    /// immediately cannot be missed. Stopping a task to make it finish does not work: the
    /// cancellation wins the race against the run loop and the body never executes.
    /// </remarks>
    private async Task WaitForStateAsync(string name, BackgroundTaskState state, Action start)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = _bus.Subscribe<BackgroundTaskStateChangedEvent>(e =>
        {
            if (e.TaskName == name && e.CurrentState == state)
            {
                reached.TrySetResult();
            }
        });

        start();
        await reached.Task.WaitAsync(Timeout);
    }

    [Fact]
    public void Register_AddsToTasksAndStartsIdle()
    {
        var registration = _manager.Register(new StubBackgroundTask());

        Assert.Single(_manager.Tasks);
        Assert.Equal(BackgroundTaskState.Idle, registration.State);
        Assert.Same(registration, _manager.Find("stub"));
    }

    [Fact]
    public void Register_RejectsADuplicateName()
    {
        _manager.Register(new StubBackgroundTask("purge"));

        // Names are the handle every other method takes, so a duplicate has to fail loudly
        // rather than shadow the first task.
        Assert.Throws<InvalidOperationException>(
            () => _manager.Register(new StubBackgroundTask("purge")));
    }

    [Fact]
    public void Register_UsesTheScheduleARecurringTaskDeclares()
    {
        var registration = _manager.Register(new StubRecurringTask(TimeSpan.FromMinutes(5)));

        Assert.True(registration.Options.IsRecurring);
        Assert.Equal(TimeSpan.FromMinutes(5), registration.Options.Interval);
    }

    [Fact]
    public void Find_ReturnsNullForAnUnknownName()
        => Assert.Null(_manager.Find("nothing-registered"));

    [Fact]
    public async Task Start_RunsTheTaskAndReachesCompleted()
    {
        var task = new StubBackgroundTask(body: _ => Task.CompletedTask);
        var registration = _manager.Register(task);

        await WaitForStateAsync("stub", BackgroundTaskState.Completed, () =>
            Assert.True(_manager.Start("stub")));

        await _manager.StopAsync("stub");

        Assert.Equal(1, task.Executions);
        Assert.Equal(1, registration.RunCount);
        Assert.NotNull(registration.LastStarted);
        Assert.NotNull(registration.LastCompleted);
    }

    [Fact]
    public async Task Start_IsANoOpWhileTheTaskIsAlreadyRunning()
    {
        var task = new StubBackgroundTask();
        _manager.Register(task);

        Assert.True(_manager.Start("stub"));
        await task.Started.Task.WaitAsync(Timeout);

        // Two callers racing to start the same watchdog is normal; the second must not
        // start a second loop.
        Assert.False(_manager.Start("stub"));

        task.Gate.TrySetResult();
        await _manager.StopAsync("stub");
    }

    [Fact]
    public void Start_ReturnsFalseForAnUnknownName()
        => Assert.False(_manager.Start("nothing-registered"));

    [Fact]
    public async Task StopAsync_CancelsARunningTask()
    {
        var task = new StubBackgroundTask();
        var registration = _manager.Register(task);

        _manager.Start("stub");
        await task.Started.Task.WaitAsync(Timeout);

        Assert.True(await _manager.StopAsync("stub"));
        Assert.Equal(BackgroundTaskState.Cancelled, registration.State);

        // Cancelling is not failing: it must not push the task toward being given up on.
        Assert.Equal(0, registration.ConsecutiveFailures);
    }

    [Fact]
    public async Task StopAsync_ReturnsFalseForAnUnknownName()
        => Assert.False(await _manager.StopAsync("nothing-registered"));

    [Fact]
    public async Task PauseAndResume_HoldTheTaskBetweenRuns()
    {
        var runs = 0;
        var secondRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var task = new StubRecurringTask(TimeSpan.FromMilliseconds(10), () =>
        {
            if (Interlocked.Increment(ref runs) == 2)
            {
                secondRun.TrySetResult();
            }

            return Task.CompletedTask;
        });

        var registration = _manager.Register(task);
        _manager.Start(task.Name);
        await task.FirstRun.Task.WaitAsync(Timeout);

        Assert.True(_manager.Pause(task.Name));
        Assert.True(_manager.Resume(task.Name));

        await secondRun.Task.WaitAsync(Timeout);
        await _manager.StopAsync(task.Name);

        Assert.True(registration.RunCount >= 2);
    }

    [Fact]
    public void Pause_ReturnsFalseWhenTheTaskIsNotRunning()
    {
        _manager.Register(new StubBackgroundTask());

        // Idle, so there is nothing to pause.
        Assert.False(_manager.Pause("stub"));
    }

    [Fact]
    public void Resume_ReturnsFalseWhenTheTaskIsNotPaused()
    {
        _manager.Register(new StubBackgroundTask());

        Assert.False(_manager.Resume("stub"));
    }

    [Fact]
    public async Task Progress_ReachesTheRegistration()
    {
        var task = new StubBackgroundTask(body: stub =>
        {
            stub.Progress!.Report(new BackgroundTaskProgress("Halfway", 50));
            return Task.CompletedTask;
        });

        var registration = _manager.Register(task);

        await WaitForStateAsync("stub", BackgroundTaskState.Completed, () => _manager.Start("stub"));
        await _manager.StopAsync("stub");

        Assert.Equal("Halfway", registration.Progress.Message);
        Assert.Equal(50, registration.Progress.PercentComplete);
        Assert.False(registration.Progress.IsIndeterminate);
    }

    [Fact]
    public async Task AFailingTask_IsRecordedAndDoesNotEscape()
    {
        var task = new StubBackgroundTask(body: _ => throw new InvalidOperationException("boom"));
        var registration = _manager.Register(task);

        await WaitForStateAsync("stub", BackgroundTaskState.Faulted, () => _manager.Start("stub"));
        await _manager.StopAsync("stub");

        Assert.Equal(1, registration.ConsecutiveFailures);
        Assert.IsType<InvalidOperationException>(registration.LastError);
        Assert.Equal("boom", registration.LastError!.Message);
    }

    [Fact]
    public async Task AFailingTask_StopsAfterMaxConsecutiveFailures()
    {
        var attempts = 0;
        var exhausted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var task = new StubBackgroundTask(body: _ =>
        {
            if (Interlocked.Increment(ref attempts) >= 3)
            {
                exhausted.TrySetResult();
            }

            throw new InvalidOperationException("always fails");
        });

        var registration = _manager.Register(task, new BackgroundTaskOptions(
            AutoRestart: true,
            MaxConsecutiveFailures: 3,
            RestartDelay: TimeSpan.FromMilliseconds(1)));

        _manager.Start("stub");
        await exhausted.Task.WaitAsync(Timeout);
        await _manager.StopAsync("stub");

        // The budget is a ceiling: a permanently broken task must stop retrying, or it
        // fills the log for the life of the shift.
        Assert.Equal(3, attempts);
        Assert.Equal(3, registration.ConsecutiveFailures);
        Assert.Equal(BackgroundTaskState.Faulted, registration.State);
    }

    [Fact]
    public async Task AFailingTask_DoesNotRestartWhenAutoRestartIsOff()
    {
        var task = new StubBackgroundTask(body: _ => throw new InvalidOperationException("boom"));

        _manager.Register(task, new BackgroundTaskOptions(
            AutoRestart: false,
            RestartDelay: TimeSpan.FromMilliseconds(1)));

        await WaitForStateAsync("stub", BackgroundTaskState.Faulted, () => _manager.Start("stub"));
        await _manager.StopAsync("stub");

        Assert.Equal(1, task.Executions);
    }

    [Fact]
    public async Task ASucceedingRun_ClearsTheFailureCount()
    {
        var attempts = 0;
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var task = new StubRecurringTask(TimeSpan.FromMilliseconds(1), () =>
        {
            // Fails once, then succeeds — the intermittent case the reset exists for.
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new InvalidOperationException("transient");
            }

            recovered.TrySetResult();
            return Task.CompletedTask;
        });

        var registration = _manager.Register(task, new BackgroundTaskOptions(
            Interval: TimeSpan.FromMilliseconds(1),
            RestartDelay: TimeSpan.FromMilliseconds(1)));

        _manager.Start(task.Name);
        await recovered.Task.WaitAsync(Timeout);
        await _manager.StopAsync(task.Name);

        Assert.Equal(0, registration.ConsecutiveFailures);
        Assert.Null(registration.LastError);
    }

    [Fact]
    public async Task ARecurringTask_RunsMoreThanOnce()
    {
        var thirdRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = 0;

        var task = new StubRecurringTask(TimeSpan.FromMilliseconds(1), () =>
        {
            if (Interlocked.Increment(ref runs) >= 3)
            {
                thirdRun.TrySetResult();
            }

            return Task.CompletedTask;
        });

        _manager.Register(task);
        _manager.Start(task.Name);

        await thirdRun.Task.WaitAsync(Timeout);
        await _manager.StopAsync(task.Name);

        Assert.True(runs >= 3);
    }

    [Fact]
    public async Task AOneShotTask_RunsOnceAndStops()
    {
        var task = new StubBackgroundTask(body: _ => Task.CompletedTask);
        var registration = _manager.Register(task);

        await WaitForStateAsync("stub", BackgroundTaskState.Completed, () => _manager.Start("stub"));
        await _manager.StopAsync("stub");

        Assert.Equal(1, task.Executions);
        Assert.Equal(BackgroundTaskState.Completed, registration.State);
    }

    [Fact]
    public async Task StateTransitions_ArePublishedOnTheBus()
    {
        List<BackgroundTaskStateChangedEvent> observed = [];

        using var subscription = _bus.Subscribe<BackgroundTaskStateChangedEvent>(
            e => observed.Add(e));

        var task = new StubBackgroundTask(body: _ => Task.CompletedTask);
        _manager.Register(task);

        await WaitForStateAsync("stub", BackgroundTaskState.Completed, () => _manager.Start("stub"));
        await _manager.StopAsync("stub");

        Assert.Contains(observed, e => e.CurrentState == BackgroundTaskState.Running);
        Assert.Contains(observed, e => e.CurrentState == BackgroundTaskState.Completed);

        // A subscriber reconstructing a timeline needs each event's predecessor to be the
        // previous event's successor, not a stale value read back from the registration.
        Assert.All(observed, e => Assert.NotEqual(e.PreviousState, e.CurrentState));
    }

    [Fact]
    public async Task UnregisterAsync_StopsTheTaskAndRemovesIt()
    {
        var task = new StubBackgroundTask();
        _manager.Register(task);

        _manager.Start("stub");
        await task.Started.Task.WaitAsync(Timeout);

        Assert.True(await _manager.UnregisterAsync("stub"));
        Assert.Empty(_manager.Tasks);
        Assert.Null(_manager.Find("stub"));
    }

    [Fact]
    public async Task UnregisterAsync_ReturnsFalseForAnUnknownName()
        => Assert.False(await _manager.UnregisterAsync("nothing-registered"));

    [Fact]
    public async Task StopAllAsync_StopsEverything()
    {
        var first = new StubBackgroundTask("first");
        var second = new StubBackgroundTask("second");

        var firstRegistration = _manager.Register(first);
        var secondRegistration = _manager.Register(second);

        _manager.StartAll();

        await first.Started.Task.WaitAsync(Timeout);
        await second.Started.Task.WaitAsync(Timeout);

        await _manager.StopAllAsync();

        Assert.Equal(BackgroundTaskState.Cancelled, firstRegistration.State);
        Assert.Equal(BackgroundTaskState.Cancelled, secondRegistration.State);
    }

    [Fact]
    public async Task StopAllAsync_AbandonsATaskThatIgnoresItsToken()
    {
        // Deliberately ignores cancellation — the case the bounded wait exists for.
        var stubborn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = new StubBackgroundTask(body: _ => stubborn.Task);

        using var manager = new BackgroundTaskManager(
            _bus,
            _dispatcher,
            new ApplicationLogger(_loggerFactory, new TestApplicationInfoService()),
            Options.Create(new BackgroundTaskManagerOptions { ShutdownTimeoutSeconds = 1 }));

        manager.Register(task);
        manager.Start("stub");
        await task.Started.Task.WaitAsync(Timeout);

        // Returns rather than hanging: a task that ignores its token must not keep the
        // process alive and force an operator to kill it.
        await manager.StopAllAsync().WaitAsync(Timeout);

        stubborn.TrySetResult();
    }

    [Fact]
    public async Task Dispose_StopsRunningTasks()
    {
        var task = new StubBackgroundTask();

        var manager = new BackgroundTaskManager(
            _bus,
            _dispatcher,
            new ApplicationLogger(_loggerFactory, new TestApplicationInfoService()),
            Options.Create(new BackgroundTaskManagerOptions()));

        var registration = manager.Register(task);
        manager.Start("stub");
        await task.Started.Task.WaitAsync(Timeout);

        manager.Dispose();

        // Registrations survive so a shutdown log can still say what each task was doing;
        // what must not survive is the work itself.
        Assert.Equal(BackgroundTaskState.Cancelled, registration.State);
    }

    [Fact]
    public async Task ConcurrencyIsCappedAtTheConfiguredLimit()
    {
        var running = 0;
        var peak = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allStarted = new CountdownEvent(1);

        using var manager = new BackgroundTaskManager(
            _bus,
            _dispatcher,
            new ApplicationLogger(_loggerFactory, new TestApplicationInfoService()),
            Options.Create(new BackgroundTaskManagerOptions { MaxConcurrentTasks = 1 }));

        for (var i = 0; i < 3; i++)
        {
            manager.Register(new StubBackgroundTask($"task{i}", body: async _ =>
            {
                var current = Interlocked.Increment(ref running);
                InterlockedMax(ref peak, current);

                allStarted.Signal();
                await release.Task.ConfigureAwait(false);

                Interlocked.Decrement(ref running);
            }));
        }

        manager.StartAll();

        // One slot, so exactly one task can be inside the body at a time.
        Assert.True(allStarted.Wait(Timeout));
        Assert.Equal(1, peak);

        release.TrySetResult();
        await manager.StopAllAsync();
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;

        while ((current = Volatile.Read(ref target)) < value
            && Interlocked.CompareExchange(ref target, value, current) != current)
        {
            // Another thread moved the peak between the read and the exchange; retry.
        }
    }
}
