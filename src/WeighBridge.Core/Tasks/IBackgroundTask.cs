namespace WeighBridge.Core.Tasks;

/// <summary>
/// A unit of work the application runs away from the user-interface thread.
/// </summary>
/// <remarks>
/// <para>
/// This is the only thing a future module has to implement to get scheduling, cancellation,
/// progress, failure logging and automatic restart. No module starts its own
/// <see cref="Task"/> or timer: work started outside the manager is invisible to shutdown,
/// which is how an application ends up with a thread still writing to a database that has
/// already been closed.
/// </para>
/// <para>
/// Implementations must observe the cancellation token. The manager waits for a bounded
/// period during shutdown and then abandons the task; a task that ignores its token turns
/// a clean exit into a hung process an operator has to kill.
/// </para>
/// </remarks>
public interface IBackgroundTask
{
    /// <summary>Stable identifier, unique within the manager.</summary>
    string Name { get; }

    /// <summary>Description shown in a task list or activity panel.</summary>
    string Description { get; }

    /// <summary>Relative importance when the scheduler is saturated.</summary>
    BackgroundTaskPriority Priority { get; }

    /// <summary>
    /// Runs the work.
    /// </summary>
    /// <param name="progress">
    /// Reports progress. Never <c>null</c> — a task that does not report progress simply
    /// ignores it, rather than having to null-check.
    /// </param>
    /// <param name="cancellationToken">Signalled on stop and on shutdown. Must be observed.</param>
    Task ExecuteAsync(IProgress<BackgroundTaskProgress> progress, CancellationToken cancellationToken);
}
