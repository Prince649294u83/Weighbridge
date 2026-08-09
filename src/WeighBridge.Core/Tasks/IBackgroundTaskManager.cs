using System.Collections.ObjectModel;

namespace WeighBridge.Core.Tasks;

/// <summary>
/// Owns every piece of background work the application runs.
/// </summary>
/// <remarks>
/// <para>
/// A module implements <see cref="IBackgroundTask"/> and registers it here. Scheduling,
/// cancellation, progress, priority, exception isolation, automatic restart and failure
/// logging are the manager's job, so no module starts its own <see cref="Task"/> or timer.
/// Work started outside the manager is invisible to shutdown, which is how an application
/// ends up with a thread still writing to a database that has already been closed.
/// </para>
/// <para>
/// Pause takes effect between runs: a run already in flight is allowed to finish, and the
/// next one waits for <see cref="Resume"/>. Suspending a thread mid-operation is not
/// something the runtime can do safely, and pretending otherwise would leave a half-written
/// record behind. A task that needs to yield sooner should observe its cancellation token
/// and be stopped instead.
/// </para>
/// </remarks>
public interface IBackgroundTaskManager
{
    /// <summary>
    /// Every registered task with its live state, ordered by registration.
    /// </summary>
    /// <remarks>
    /// Read-only and observable: an activity panel binds to it directly, and each
    /// <see cref="TaskRegistration"/> raises change notifications as its task progresses.
    /// A consumer cannot add or remove entries — that goes through
    /// <see cref="Register"/> and <see cref="UnregisterAsync"/>.
    /// </remarks>
    ReadOnlyObservableCollection<TaskRegistration> Tasks { get; }

    /// <summary>
    /// Registers a task without starting it.
    /// </summary>
    /// <param name="task">The work to register.</param>
    /// <param name="options">
    /// How it should be scheduled. When omitted, a <see cref="RecurringTask"/> contributes
    /// its own <see cref="RecurringTask.Schedule"/> and anything else is treated as
    /// <see cref="BackgroundTaskOptions.OneShot"/>.
    /// </param>
    /// <returns>The registration, for binding or for reading state.</returns>
    /// <exception cref="InvalidOperationException">
    /// A task with the same <see cref="IBackgroundTask.Name"/> is already registered.
    /// Names are the handle every other method takes, so duplicates are rejected at
    /// registration rather than silently shadowing one another.
    /// </exception>
    TaskRegistration Register(IBackgroundTask task, BackgroundTaskOptions? options = null);

    /// <summary>
    /// Stops a task and removes it from <see cref="Tasks"/>.
    /// </summary>
    /// <returns>False when no task by that name was registered.</returns>
    Task<bool> UnregisterAsync(string name);

    /// <summary>The registration with this name, or <c>null</c> when there is none.</summary>
    TaskRegistration? Find(string name);

    /// <summary>
    /// Starts a task, or restarts one that has completed, faulted or been cancelled.
    /// </summary>
    /// <returns>
    /// False when no task by that name was registered, or when it is already active.
    /// Starting a running task is a no-op rather than an error: two callers racing to
    /// start the same watchdog is a normal thing to happen, not a bug to crash on.
    /// </returns>
    bool Start(string name);

    /// <summary>Starts every registered task that is not already active.</summary>
    void StartAll();

    /// <summary>
    /// Signals cancellation and waits for the task to observe it.
    /// </summary>
    /// <returns>False when no task by that name was registered.</returns>
    Task<bool> StopAsync(string name);

    /// <summary>
    /// Stops every active task, waiting for all of them.
    /// </summary>
    /// <remarks>
    /// Called during shutdown. The wait is bounded: a task that ignores its cancellation
    /// token is abandoned rather than allowed to hang the process, and the fact is logged.
    /// </remarks>
    Task StopAllAsync();

    /// <summary>
    /// Suspends scheduling for a task. The current run finishes; the next one waits.
    /// </summary>
    /// <returns>False when no task by that name was registered, or it is not running.</returns>
    bool Pause(string name);

    /// <summary>Resumes a paused task.</summary>
    /// <returns>False when no task by that name was registered, or it is not paused.</returns>
    bool Resume(string name);
}
