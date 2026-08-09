namespace WeighBridge.Core.Tasks;

/// <summary>
/// Tuning for the background task manager.
/// </summary>
/// <remarks>
/// Bound to the <c>BackgroundTasks</c> configuration section. A site whose terminal is
/// slower, or whose network share makes every task wait on I/O, can change these without
/// a rebuild.
/// </remarks>
public sealed class BackgroundTaskManagerOptions
{
    /// <summary>Configuration section this class binds to.</summary>
    public const string SectionName = "BackgroundTasks";

    /// <summary>
    /// How many tasks may execute at once.
    /// </summary>
    /// <remarks>
    /// Deliberately small. Background work here is slow I/O — database probes, server
    /// pings, log purges — and the machine's real job is staying responsive to an
    /// operator, not finishing housekeeping quickly.
    /// </remarks>
    public int MaxConcurrentTasks { get; set; } = 2;

    /// <summary>
    /// Seconds to wait for tasks to observe cancellation during shutdown.
    /// </summary>
    /// <remarks>
    /// Bounded on purpose. A task that ignores its token is abandoned and the fact
    /// logged; the alternative is a process that never exits and an operator who has to
    /// kill it from Task Manager.
    /// </remarks>
    public int ShutdownTimeoutSeconds { get; set; } = 10;

    /// <summary>Shutdown wait as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan ShutdownTimeout => TimeSpan.FromSeconds(Math.Max(1, ShutdownTimeoutSeconds));
}
