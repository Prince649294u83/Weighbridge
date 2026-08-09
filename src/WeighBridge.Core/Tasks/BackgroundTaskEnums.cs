namespace WeighBridge.Core.Tasks;

/// <summary>
/// Relative importance of a background task, used to decide what runs when the scheduler
/// is saturated.
/// </summary>
public enum BackgroundTaskPriority
{
    /// <summary>Housekeeping. Log purges, cache trims — may wait indefinitely.</summary>
    Low = 0,

    /// <summary>The default.</summary>
    Normal = 1,

    /// <summary>Work the operator is waiting on.</summary>
    High = 2,

    /// <summary>
    /// Runs ahead of everything else. Reserved for work that keeps the terminal usable —
    /// re-establishing the indicator connection, for instance.
    /// </summary>
    Critical = 3,
}

/// <summary>Where a background task is in its lifecycle.</summary>
public enum BackgroundTaskState
{
    /// <summary>Registered but never started.</summary>
    Idle = 0,

    /// <summary>Waiting for a scheduler slot.</summary>
    Queued = 1,

    /// <summary>Executing.</summary>
    Running = 2,

    /// <summary>Paused; will resume where it left off.</summary>
    Paused = 3,

    /// <summary>Finished normally.</summary>
    Completed = 4,

    /// <summary>Cancelled before finishing.</summary>
    Cancelled = 5,

    /// <summary>Ended by an unhandled exception.</summary>
    Faulted = 6,
}
