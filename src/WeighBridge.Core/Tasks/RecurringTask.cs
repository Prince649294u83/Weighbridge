namespace WeighBridge.Core.Tasks;

/// <summary>
/// Base class for a background task that repeats on a fixed interval.
/// </summary>
/// <remarks>
/// <para>
/// A recurring task knows its own cadence — a connection watchdog belongs on a
/// five-second interval wherever it is registered, and a log purge belongs on a daily
/// one. Carrying the schedule here rather than at the call site means the interval is
/// stated once, next to the work it governs, instead of being restated by every
/// registration and eventually disagreeing with itself.
/// </para>
/// <para>
/// The manager still accepts an explicit <see cref="BackgroundTaskOptions"/> for the
/// cases that need to override it, so this is a default and not a constraint.
/// </para>
/// <para>
/// Derived classes must observe the cancellation token in
/// <see cref="IBackgroundTask.ExecuteAsync"/>. Recurrence itself is driven by the manager:
/// a derived class implements one pass of the work and never loops or sleeps internally.
/// </para>
/// </remarks>
public abstract class RecurringTask : IBackgroundTask
{
    /// <summary>
    /// Initialises the task's identity and schedule.
    /// </summary>
    /// <param name="name">Stable identifier, unique within the manager.</param>
    /// <param name="description">Description shown in a task list.</param>
    /// <param name="interval">Gap between runs. Must be greater than zero.</param>
    /// <param name="priority">Relative importance when the scheduler is saturated.</param>
    /// <param name="runImmediately">
    /// True to run as soon as the task is started; false to wait one
    /// <paramref name="interval"/> first.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> or <paramref name="description"/> is blank.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="interval"/> is zero or negative, which would make the task spin.
    /// </exception>
    protected RecurringTask(
        string name,
        string description,
        TimeSpan interval,
        BackgroundTaskPriority priority = BackgroundTaskPriority.Normal,
        bool runImmediately = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                interval,
                "A recurring task needs an interval greater than zero, or it would run continuously.");
        }

        Name = name;
        Description = description;
        Priority = priority;
        Schedule = BackgroundTaskOptions.Recurring(interval, runImmediately);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Description { get; }

    /// <inheritdoc />
    public BackgroundTaskPriority Priority { get; }

    /// <summary>
    /// The schedule this task declares for itself, used when it is registered without
    /// explicit options.
    /// </summary>
    public BackgroundTaskOptions Schedule { get; }

    /// <summary>Gap between runs.</summary>
    public TimeSpan Interval => Schedule.Interval!.Value;

    /// <inheritdoc />
    public abstract Task ExecuteAsync(
        IProgress<BackgroundTaskProgress> progress,
        CancellationToken cancellationToken);

    /// <inheritdoc />
    public override string ToString() => $"{Name} (every {Interval})";
}
