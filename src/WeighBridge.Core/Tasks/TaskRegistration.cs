using WeighBridge.Core.Mvvm;

namespace WeighBridge.Core.Tasks;

/// <summary>
/// A task known to the manager, together with everything observable about it.
/// </summary>
/// <remarks>
/// <para>
/// Observable so an activity panel binds to it directly. The manager owns the state
/// transitions; a consumer reads them and calls the manager to change them, which keeps
/// the state machine in one place rather than spread across whoever holds a reference.
/// </para>
/// <para>
/// Every property is therefore read-only from outside. The manager records changes through
/// the <c>Mark…</c> and <c>Set…</c> methods below, each of which is one meaningful
/// lifecycle transition rather than a raw setter — so the shell may bind to
/// <see cref="State"/> but cannot invent a state the manager never entered. The methods are
/// public rather than <c>internal</c> because the manager lives in another assembly, the
/// same reason <see cref="Notifications.Notification.MarkDismissed"/> is a method.
/// </para>
/// </remarks>
public sealed class TaskRegistration : ObservableObject
{
    private BackgroundTaskState _state = BackgroundTaskState.Idle;
    private BackgroundTaskProgress _progress;
    private DateTimeOffset? _lastStarted;
    private DateTimeOffset? _lastCompleted;
    private DateTimeOffset? _nextRun;
    private Exception? _lastError;
    private int _consecutiveFailures;
    private int _runCount;

    /// <summary>
    /// Creates a registration for a task and its schedule.
    /// </summary>
    /// <remarks>
    /// Obtain one from <see cref="IBackgroundTaskManager.Register"/> rather than
    /// constructing it. A registration built by hand is inert: nothing runs it.
    /// </remarks>
    public TaskRegistration(IBackgroundTask task, BackgroundTaskOptions options)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(options);

        Task = task;
        Options = options;
        _progress = new BackgroundTaskProgress(string.Empty);
    }

    /// <summary>The work itself.</summary>
    public IBackgroundTask Task { get; }

    /// <summary>How it is scheduled and what happens when it fails.</summary>
    public BackgroundTaskOptions Options { get; }

    /// <summary>Identifier, taken from the task.</summary>
    public string Name => Task.Name;

    /// <summary>Description shown in a task list.</summary>
    public string Description => Task.Description;

    /// <summary>Relative importance when the scheduler is saturated.</summary>
    public BackgroundTaskPriority Priority => Task.Priority;

    /// <summary>Where the task is in its lifecycle.</summary>
    public BackgroundTaskState State
    {
        get => _state;
        private set => SetProperty(ref _state, value);
    }

    /// <summary>Most recent progress report.</summary>
    public BackgroundTaskProgress Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    /// <summary>When the current or most recent run began.</summary>
    public DateTimeOffset? LastStarted
    {
        get => _lastStarted;
        private set => SetProperty(ref _lastStarted, value);
    }

    /// <summary>When the most recent run finished, however it ended.</summary>
    public DateTimeOffset? LastCompleted
    {
        get => _lastCompleted;
        private set => SetProperty(ref _lastCompleted, value);
    }

    /// <summary>When a recurring task is next due.</summary>
    public DateTimeOffset? NextRun
    {
        get => _nextRun;
        private set => SetProperty(ref _nextRun, value);
    }

    /// <summary>The exception that ended the most recent run, when one did.</summary>
    public Exception? LastError
    {
        get => _lastError;
        private set => SetProperty(ref _lastError, value);
    }

    /// <summary>
    /// Failures since the last success. Reset on success, so a task that fails
    /// intermittently is never given up on.
    /// </summary>
    public int ConsecutiveFailures
    {
        get => _consecutiveFailures;
        private set => SetProperty(ref _consecutiveFailures, value);
    }

    /// <summary>How many times the task has run since registration.</summary>
    public int RunCount
    {
        get => _runCount;
        private set => SetProperty(ref _runCount, value);
    }

    /// <summary>True while the task is queued, running or paused.</summary>
    public bool IsActive => State is BackgroundTaskState.Queued
        or BackgroundTaskState.Running
        or BackgroundTaskState.Paused;

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({State})";

    /// <summary>
    /// Moves the task to a new lifecycle state.
    /// </summary>
    /// <param name="state">The state now in effect.</param>
    /// <returns>
    /// True when the state actually changed, so a caller can skip announcing a transition
    /// that did not happen.
    /// </returns>
    public bool SetState(BackgroundTaskState state)
    {
        if (_state == state)
        {
            return false;
        }

        State = state;

        // IsActive is computed from State, so a bound "is anything running" indicator
        // would never refresh without this.
        OnPropertyChanged(nameof(IsActive));
        return true;
    }

    /// <summary>Records the most recent progress report from the running task.</summary>
    public void ReportProgress(BackgroundTaskProgress progress) => Progress = progress;

    /// <summary>Records that a run has begun, clearing the previous run's error.</summary>
    public void MarkStarted(DateTimeOffset startedAt)
    {
        LastStarted = startedAt;
        LastError = null;
    }

    /// <summary>
    /// Records a run that completed normally.
    /// </summary>
    /// <remarks>
    /// Clears <see cref="ConsecutiveFailures"/>: the count exists to catch a task that is
    /// permanently broken, not one that fails when the network happens to be down.
    /// </remarks>
    public void MarkSucceeded(DateTimeOffset completedAt)
    {
        LastCompleted = completedAt;
        RunCount++;
        ConsecutiveFailures = 0;
    }

    /// <summary>Records a run that ended in an exception.</summary>
    public void MarkFailed(DateTimeOffset completedAt, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        LastCompleted = completedAt;
        LastError = error;
        RunCount++;
        ConsecutiveFailures++;
    }

    /// <summary>
    /// Records a run that stopped because cancellation was requested.
    /// </summary>
    /// <remarks>
    /// Deliberately touches neither <see cref="RunCount"/> nor
    /// <see cref="ConsecutiveFailures"/>. Shutting the application down is not a failure of
    /// the task, and counting it as one would make a task look unhealthy after a few
    /// ordinary restarts.
    /// </remarks>
    public void MarkCancelled(DateTimeOffset completedAt) => LastCompleted = completedAt;

    /// <summary>Records when the task is next due, or <c>null</c> when nothing is scheduled.</summary>
    public void SetNextRun(DateTimeOffset? nextRun) => NextRun = nextRun;
}
