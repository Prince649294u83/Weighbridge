using System.Collections.ObjectModel;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Events;
using WeighBridge.Core.Events.Catalog;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Tasks;
using WeighBridge.Core.Threading;

namespace WeighBridge.Services.Tasks;

/// <summary>
/// Schedules, runs and supervises every background task in the application.
/// </summary>
/// <remarks>
/// <para>
/// Each registered task gets one long-lived loop. The loop, not the task, owns recurrence:
/// an implementation runs a single pass and returns, and the loop decides whether to wait
/// an interval, retry after a failure, or stop. That keeps the restart policy in one
/// testable place instead of copied into every task.
/// </para>
/// <para>
/// An exception from one task never reaches another. It is caught at the loop boundary,
/// recorded on the registration, logged and published — a faulting log purge cannot take
/// down the indicator reconnect.
/// </para>
/// </remarks>
public sealed class BackgroundTaskManager : IBackgroundTaskManager, IDisposable
{
    private readonly ObservableCollection<TaskRegistration> _tasks = [];
    private readonly Dictionary<string, TaskRunner> _runners = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private readonly BackgroundTaskScheduler _scheduler;
    private readonly IEventPublisher _eventPublisher;
    private readonly IUiDispatcher _dispatcher;
    private readonly IApplicationLogger _logger;
    private readonly BackgroundTaskManagerOptions _options;

    private bool _disposed;

    /// <summary>Creates the manager and the concurrency gate its tasks share.</summary>
    public BackgroundTaskManager(
        IEventPublisher eventPublisher,
        IUiDispatcher dispatcher,
        IApplicationLogger logger,
        IOptions<BackgroundTaskManagerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(eventPublisher);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(logger);

        _eventPublisher = eventPublisher;
        _dispatcher = dispatcher;
        _logger = logger;
        _options = options?.Value ?? new BackgroundTaskManagerOptions();
        _scheduler = new BackgroundTaskScheduler(Math.Max(1, _options.MaxConcurrentTasks));

        Tasks = new ReadOnlyObservableCollection<TaskRegistration>(_tasks);
    }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<TaskRegistration> Tasks { get; }

    /// <summary>The gate limiting how many tasks execute at once.</summary>
    public BackgroundTaskScheduler Scheduler => _scheduler;

    /// <inheritdoc />
    public TaskRegistration Register(IBackgroundTask task, BackgroundTaskOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentException.ThrowIfNullOrWhiteSpace(task.Name);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // A RecurringTask states its own cadence next to the work it governs; an explicit
        // argument still wins so a site can override it without touching the task.
        var schedule = options
            ?? (task as RecurringTask)?.Schedule
            ?? BackgroundTaskOptions.OneShot;

        var registration = new TaskRegistration(task, schedule);

        lock (_gate)
        {
            if (_runners.ContainsKey(task.Name))
            {
                throw new InvalidOperationException(
                    $"A background task named '{task.Name}' is already registered.");
            }

            _runners[task.Name] = new TaskRunner(registration);
        }

        OnUiThread(() => _tasks.Add(registration));

        _logger.Debug(
            "Background task registered: {Task} ({Priority}, {Schedule})",
            task.Name,
            task.Priority,
            schedule.IsRecurring ? $"every {schedule.Interval}" : "one-shot");

        return registration;
    }

    /// <inheritdoc />
    public async Task<bool> UnregisterAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        TaskRunner? runner;

        lock (_gate)
        {
            if (!_runners.Remove(name, out runner))
            {
                return false;
            }
        }

        await StopRunnerAsync(runner).ConfigureAwait(false);

        OnUiThread(() => _tasks.Remove(runner.Registration));
        _logger.Debug("Background task unregistered: {Task}", name);

        return true;
    }

    /// <inheritdoc />
    public TaskRegistration? Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        lock (_gate)
        {
            return _runners.TryGetValue(name, out var runner) ? runner.Registration : null;
        }
    }

    /// <inheritdoc />
    public bool Start(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ObjectDisposedException.ThrowIf(_disposed, this);

        TaskRunner started;

        lock (_gate)
        {
            if (!_runners.TryGetValue(name, out var runner))
            {
                return false;
            }

            // Already going. Two callers racing to start the same watchdog is normal, so
            // this is a no-op rather than an exception.
            if (runner.Loop is { IsCompleted: false })
            {
                return false;
            }

            runner.Cancellation?.Dispose();
            runner.Cancellation = new CancellationTokenSource();
            runner.ClearPause();

            // Marked queued before the loop starts, so a Pause issued immediately after
            // Start finds a state it can act on rather than racing the loop. This is a
            // field write only; the event goes out below, once the lock is released.
            runner.AnnouncedState = BackgroundTaskState.Queued;

            var token = runner.Cancellation.Token;

            // Assigned inside the lock so a concurrent Start cannot see a runner with a
            // fresh token but no loop and start a second one.
            runner.Loop = Task.Run(() => RunLoopAsync(runner, token), CancellationToken.None);
            started = runner;
        }

        // Outside the lock: a subscriber re-entering the manager from its handler would
        // otherwise deadlock against the lock we were holding.
        Announce(started, BackgroundTaskState.Idle, BackgroundTaskState.Queued);

        _logger.Debug("Background task started: {Task}", name);
        return true;
    }

    /// <inheritdoc />
    public void StartAll()
    {
        List<string> names;

        lock (_gate)
        {
            names = [.. _runners.Keys];
        }

        foreach (var name in names)
        {
            Start(name);
        }
    }

    /// <inheritdoc />
    public async Task<bool> StopAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        TaskRunner? runner;

        lock (_gate)
        {
            if (!_runners.TryGetValue(name, out runner))
            {
                return false;
            }
        }

        await StopRunnerAsync(runner).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task StopAllAsync()
    {
        List<Task> loops = [];

        lock (_gate)
        {
            // Cancelled together, then awaited once. Stopping them one at a time would
            // charge the shutdown budget serially and turn a quick exit into a slow one.
            foreach (var runner in _runners.Values)
            {
                runner.ClearPause();
                runner.Cancellation?.Cancel();

                if (runner.Loop is { } loop)
                {
                    loops.Add(loop);
                }
            }
        }

        if (loops.Count == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(loops).WaitAsync(_options.ShutdownTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            LogAbandonedTasks();
        }
        catch (Exception ex)
        {
            // A loop faulting is a manager bug: the loop body already contains its own
            // catch. Logged rather than thrown, because shutdown must still finish.
            _logger.Error(ex, "A background task loop failed while stopping");
        }
    }

    /// <inheritdoc />
    public bool Pause(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_gate)
        {
            if (!_runners.TryGetValue(name, out var runner))
            {
                return false;
            }

            if (runner.AnnouncedState is not (BackgroundTaskState.Running or BackgroundTaskState.Queued))
            {
                return false;
            }

            runner.RequestPause();
        }

        // Logged outside the lock, as with every other call out of this class.
        _logger.Debug("Background task paused: {Task}", name);
        return true;
    }

    /// <inheritdoc />
    public bool Resume(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_gate)
        {
            if (!_runners.TryGetValue(name, out var runner) || !runner.ClearPause())
            {
                return false;
            }
        }

        _logger.Debug("Background task resumed: {Task}", name);
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Blocking is acceptable only because the wait is bounded. The container disposes
        // singletons synchronously, and a loop left running past disposal would go on
        // touching services that have already gone.
        try
        {
            StopAllAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to stop background tasks during disposal");
        }

        lock (_gate)
        {
            foreach (var runner in _runners.Values)
            {
                runner.Cancellation?.Dispose();
            }

            _runners.Clear();
        }

        _scheduler.Dispose();
    }

    /// <summary>Cancels one runner and waits for its loop, within the shutdown budget.</summary>
    private async Task StopRunnerAsync(TaskRunner runner)
    {
        // Released first: a loop parked on the pause gate would never reach a cancellation
        // check, and the stop would wait out the whole timeout for nothing.
        runner.ClearPause();
        runner.Cancellation?.Cancel();

        Task? loop;

        lock (_gate)
        {
            loop = runner.Loop;
        }

        if (loop is null)
        {
            return;
        }

        try
        {
            await loop.WaitAsync(_options.ShutdownTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.Warning(
                "Background task {Task} did not observe cancellation within {Timeout} and was abandoned",
                runner.Registration.Name,
                _options.ShutdownTimeout);
        }
    }

    /// <summary>Names the tasks still running after the shutdown budget expired.</summary>
    /// <remarks>
    /// Named rather than counted: a task that ignores its token is a bug, and abandoning
    /// it silently hides that bug from whoever has to fix it.
    /// </remarks>
    private void LogAbandonedTasks()
    {
        string[] stuck;

        lock (_gate)
        {
            stuck = [.. _runners.Values
                .Where(runner => runner.Loop is { IsCompleted: false })
                .Select(runner => runner.Registration.Name)];
        }

        _logger.Warning(
            "Abandoned {Count} background task(s) that did not observe cancellation within {Timeout}: {Tasks}",
            stuck.Length,
            _options.ShutdownTimeout,
            stuck.Length == 0 ? "(none)" : string.Join(", ", stuck));
    }

    /// <summary>
    /// Drives one task for its whole lifetime: waits, runs, and decides what happens next.
    /// </summary>
    private async Task RunLoopAsync(TaskRunner runner, CancellationToken cancellationToken)
    {
        var registration = runner.Registration;
        var schedule = registration.Options;

        try
        {
            // A synchronisation task that fires the instant the application opens is
            // usually wrong: the network may not be up yet.
            if (schedule.IsRecurring
                && !schedule.RunImmediately
                && !await DelayAsync(registration, schedule.Interval!.Value, cancellationToken).ConfigureAwait(false))
            {
                SetState(runner, BackgroundTaskState.Cancelled);
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!await WaitWhilePausedAsync(runner, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                var outcome = await RunOnceAsync(runner, cancellationToken).ConfigureAwait(false);

                if (outcome is RunOutcome.Aborted || cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // Read from the runner, not the registration: the registration is written
                // through the dispatcher and may not have caught up, which would let a
                // permanently broken task restart past its budget forever.
                if (outcome is RunOutcome.Failed
                    && (!schedule.AutoRestart
                        || runner.ConsecutiveFailures >= schedule.MaxConsecutiveFailures))
                {
                    _logger.Error(
                        runner.LastError ?? new InvalidOperationException("Task failed without an exception."),
                        "Background task {Task} gave up after {Failures} consecutive failure(s)",
                        registration.Name,
                        runner.ConsecutiveFailures);
                    return;
                }

                // A one-shot task that succeeded is finished. One that failed falls through
                // to the restart delay below, which is what AutoRestart means for it.
                if (!schedule.IsRecurring && outcome is RunOutcome.Succeeded)
                {
                    return;
                }

                var wait = outcome is RunOutcome.Succeeded
                    ? schedule.Interval!.Value
                    : schedule.EffectiveRestartDelay;

                SetState(runner, BackgroundTaskState.Queued);

                if (!await DelayAsync(registration, wait, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }

            SetState(runner, BackgroundTaskState.Cancelled);
        }
        catch (Exception ex)
        {
            // The loop itself failing is a manager bug, not a task bug. Logged loudly and
            // contained here, because one broken loop must not stop the others.
            _logger.Critical(ex, "Background task loop for {Task} failed", registration.Name);
            SetState(runner, BackgroundTaskState.Faulted, ex);
        }
        finally
        {
            OnUiThread(() => registration.SetNextRun(null));
        }
    }

    /// <summary>Runs the task once, isolating and recording whatever it throws.</summary>
    private async Task<RunOutcome> RunOnceAsync(TaskRunner runner, CancellationToken cancellationToken)
    {
        var registration = runner.Registration;

        IDisposable slot;

        try
        {
            // Waiting for a slot is part of being queued, so the state stays honest about
            // why nothing is happening yet.
            SetState(runner, BackgroundTaskState.Queued);
            slot = await _scheduler.AcquireAsync(registration.Priority, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return RunOutcome.Aborted;
        }
        catch (ObjectDisposedException)
        {
            // The scheduler went away underneath us during shutdown. Not the task's
            // failure, so it must not count against the restart budget.
            return RunOutcome.Aborted;
        }

        using (slot)
        {
            var startedAt = DateTimeOffset.Now;
            OnUiThread(() => registration.MarkStarted(startedAt));
            SetState(runner, BackgroundTaskState.Running);

            // Progress arrives on whatever thread the task is on, so every report is
            // marshalled before it reaches state a view is bound to.
            var progress = new ProgressRelay(this, registration);

            try
            {
                using var operation = _logger.BeginOperation(registration.Name);

                await registration.Task.ExecuteAsync(progress, cancellationToken).ConfigureAwait(false);

                var completedAt = DateTimeOffset.Now;
                runner.RecordSuccess();
                OnUiThread(() => registration.MarkSucceeded(completedAt));
                SetState(runner, BackgroundTaskState.Completed);

                return RunOutcome.Succeeded;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Requested, not a failure: shutting down must not push a task toward
                // being given up on.
                var cancelledAt = DateTimeOffset.Now;
                OnUiThread(() => registration.MarkCancelled(cancelledAt));
                SetState(runner, BackgroundTaskState.Cancelled);

                return RunOutcome.Aborted;
            }
            catch (Exception ex)
            {
                var failedAt = DateTimeOffset.Now;
                var failures = runner.RecordFailure(ex);
                OnUiThread(() => registration.MarkFailed(failedAt, ex));

                _logger.Error(
                    ex,
                    "Background task {Task} failed (consecutive failure {Failures} of {Max})",
                    registration.Name,
                    failures,
                    registration.Options.MaxConsecutiveFailures);

                SetState(runner, BackgroundTaskState.Faulted, ex);

                return RunOutcome.Failed;
            }
        }
    }

    /// <summary>Waits while the task is paused.</summary>
    /// <returns>False when cancellation ended the wait.</returns>
    private async Task<bool> WaitWhilePausedAsync(TaskRunner runner, CancellationToken cancellationToken)
    {
        var gate = runner.PauseGate;

        if (gate is null)
        {
            return true;
        }

        SetState(runner, BackgroundTaskState.Paused);

        try
        {
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Waits for the next run, publishing when it is due.</summary>
    /// <returns>False when cancellation ended the wait.</returns>
    private async Task<bool> DelayAsync(
        TaskRegistration registration,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        var due = DateTimeOffset.Now + delay;
        OnUiThread(() => registration.SetNextRun(due));

        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            OnUiThread(() => registration.SetNextRun(null));
        }
    }

    /// <summary>Moves a task to a new state and announces the change.</summary>
    /// <remarks>
    /// The previous state is read from the runner rather than the registration. The
    /// registration is updated through the dispatcher and may lag by a message, which would
    /// make two quick transitions both report the same stale predecessor.
    /// </remarks>
    private void SetState(TaskRunner runner, BackgroundTaskState state, Exception? error = null)
    {
        var previous = runner.AnnouncedState;

        if (previous == state)
        {
            return;
        }

        runner.AnnouncedState = state;
        Announce(runner, previous, state, error);
    }

    /// <summary>Mirrors a transition onto the registration and publishes it.</summary>
    private void Announce(
        TaskRunner runner,
        BackgroundTaskState previous,
        BackgroundTaskState current,
        Exception? error = null)
    {
        OnUiThread(() => runner.Registration.SetState(current));

        // Published outside the dispatcher hop so a subscriber sees the transition
        // immediately rather than behind whatever is queued on the UI thread.
        _eventPublisher.Publish(new BackgroundTaskStateChangedEvent(
            runner.Registration.Name,
            previous,
            current,
            error,
            nameof(BackgroundTaskManager)));
    }

    /// <summary>Runs a mutation of bound state on the user-interface thread.</summary>
    /// <remarks>
    /// Every property on <see cref="TaskRegistration"/> raises change notification and a
    /// view may be bound to it. WPF only permits that from the thread that created the
    /// binding, and a task reports progress from a thread pool thread.
    /// </remarks>
    private void OnUiThread(Action action)
    {
        if (_dispatcher.IsOnUiThread)
        {
            action();
            return;
        }

        _dispatcher.Post(action);
    }

    /// <summary>How a single run ended.</summary>
    private enum RunOutcome
    {
        /// <summary>Ran to completion.</summary>
        Succeeded,

        /// <summary>Threw. Counts against the restart budget.</summary>
        Failed,

        /// <summary>Cancelled or never started. The loop ends without blaming the task.</summary>
        Aborted,
    }

    /// <summary>Carries a task's progress reports onto the user-interface thread.</summary>
    /// <remarks>
    /// <see cref="Progress{T}"/> is deliberately not used. The run loop is on a thread pool
    /// thread with no synchronization context, so <see cref="Progress{T}"/> queues each
    /// report as an independent work item: two reports can then be applied out of order and
    /// a bound progress bar jumps backwards. Going straight through
    /// <see cref="IUiDispatcher"/> preserves the order the task reported in.
    /// </remarks>
    private sealed class ProgressRelay(BackgroundTaskManager manager, TaskRegistration registration)
        : IProgress<BackgroundTaskProgress>
    {
        /// <inheritdoc />
        public void Report(BackgroundTaskProgress value)
            => manager.OnUiThread(() => registration.ReportProgress(value));
    }

    /// <summary>The manager's private runtime state for one registered task.</summary>
    /// <remarks>
    /// Kept apart from <see cref="TaskRegistration"/> so the cancellation source, the loop
    /// and the pause gate are never reachable from a view bound to the registration.
    /// </remarks>
    private sealed class TaskRunner(TaskRegistration registration)
    {
        private TaskCompletionSource? _pauseGate;
        private volatile BackgroundTaskState _announcedState = BackgroundTaskState.Idle;

        public TaskRegistration Registration { get; } = registration;

        public CancellationTokenSource? Cancellation { get; set; }

        public Task? Loop { get; set; }

        /// <summary>
        /// Failures since the last success, as the loop sees them.
        /// </summary>
        /// <remarks>
        /// The authoritative count. <see cref="TaskRegistration.ConsecutiveFailures"/>
        /// mirrors it for binding, but is written through the dispatcher and may trail —
        /// deciding the restart budget from the mirror would let a permanently broken task
        /// restart past its limit.
        /// </remarks>
        public int ConsecutiveFailures { get; private set; }

        /// <summary>The exception that ended the most recent run, when one did.</summary>
        public Exception? LastError { get; private set; }

        /// <summary>
        /// The state the manager has most recently announced.
        /// </summary>
        /// <remarks>
        /// The authoritative copy, for the same reason as
        /// <see cref="ConsecutiveFailures"/>.
        /// </remarks>
        public BackgroundTaskState AnnouncedState
        {
            get => _announcedState;
            set => _announcedState = value;
        }

        /// <summary>Non-null while paused; the loop awaits it before the next run.</summary>
        public TaskCompletionSource? PauseGate => Volatile.Read(ref _pauseGate);

        /// <summary>
        /// Records a successful run, clearing the failure count.
        /// </summary>
        /// <remarks>
        /// Unsynchronised because only the task's own loop writes these, one run at a time.
        /// </remarks>
        public void RecordSuccess()
        {
            ConsecutiveFailures = 0;
            LastError = null;
        }

        /// <summary>Records a failed run.</summary>
        /// <returns>The new consecutive failure count.</returns>
        public int RecordFailure(Exception error)
        {
            LastError = error;
            return ++ConsecutiveFailures;
        }

        /// <summary>Parks the task before its next run.</summary>
        public void RequestPause()
            => Interlocked.CompareExchange(
                ref _pauseGate,
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
                null);

        /// <summary>Releases a paused task.</summary>
        /// <returns>True when the task was paused and is now released.</returns>
        public bool ClearPause()
        {
            // Exchanged rather than checked-then-cleared so two callers racing to resume
            // cannot both believe they were the one that did it.
            var gate = Interlocked.Exchange(ref _pauseGate, null);

            if (gate is null)
            {
                return false;
            }

            // Completes asynchronously, so the loop never resumes inline inside a caller
            // that is holding the manager's lock.
            gate.TrySetResult();
            return true;
        }
    }
}
