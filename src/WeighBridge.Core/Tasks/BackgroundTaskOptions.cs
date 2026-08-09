namespace WeighBridge.Core.Tasks;

/// <summary>
/// How a registered task should be run and what to do when it fails.
/// </summary>
/// <param name="Interval">
/// Gap between runs for a recurring task, or <c>null</c> for one that runs once.
/// </param>
/// <param name="RunImmediately">
/// True to run as soon as it starts; false to wait one <paramref name="Interval"/> first.
/// A synchronisation task that fires the moment the application opens is usually wrong —
/// the network may not be up yet.
/// </param>
/// <param name="AutoRestart">
/// True to schedule the next run after a failure. A recurring task that stops on its first
/// failure is a task that silently stops working three months after installation.
/// </param>
/// <param name="MaxConsecutiveFailures">
/// How many failures in a row before the task is given up on. Prevents a task that fails
/// instantly from spinning, and prevents an unreachable server from filling the log.
/// </param>
/// <param name="RestartDelay">
/// Wait before retrying after a failure. Separate from <paramref name="Interval"/> so a
/// task that runs hourly does not wait an hour to retry a transient failure.
/// </param>
public sealed record BackgroundTaskOptions(
    TimeSpan? Interval = null,
    bool RunImmediately = true,
    bool AutoRestart = true,
    int MaxConsecutiveFailures = 5,
    TimeSpan? RestartDelay = null)
{
    /// <summary>Runs once when started.</summary>
    public static BackgroundTaskOptions OneShot { get; } = new();

    /// <summary>True when this describes a task that repeats.</summary>
    public bool IsRecurring => Interval is { } interval && interval > TimeSpan.Zero;

    /// <summary>Delay before a retry, defaulting to thirty seconds.</summary>
    public TimeSpan EffectiveRestartDelay => RestartDelay ?? TimeSpan.FromSeconds(30);

    /// <summary>Describes a task that repeats on a fixed interval.</summary>
    public static BackgroundTaskOptions Recurring(TimeSpan interval, bool runImmediately = true)
        => new(interval, runImmediately);
}
