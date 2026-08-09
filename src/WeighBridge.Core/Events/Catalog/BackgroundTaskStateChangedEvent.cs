using WeighBridge.Core.Tasks;

namespace WeighBridge.Core.Events.Catalog;

/// <summary>
/// Announces that a background task moved from one state to another.
/// </summary>
/// <remarks>
/// Lets a subscriber watch task lifecycle without holding a reference to the manager —
/// an activity panel counting what is running, or a future health monitor noticing that
/// the server sync has been faulted for an hour.
/// </remarks>
public sealed class BackgroundTaskStateChangedEvent(
    string taskName,
    BackgroundTaskState previousState,
    BackgroundTaskState currentState,
    Exception? error = null,
    string? source = null)
    : ApplicationEvent(source)
{
    /// <summary>Name of the task whose state changed.</summary>
    public string TaskName { get; } = taskName;

    /// <summary>State the task was in before the change.</summary>
    public BackgroundTaskState PreviousState { get; } = previousState;

    /// <summary>State the task is in now.</summary>
    public BackgroundTaskState CurrentState { get; } = currentState;

    /// <summary>
    /// The exception that ended the run, when <see cref="CurrentState"/> is
    /// <see cref="BackgroundTaskState.Faulted"/>.
    /// </summary>
    public Exception? Error { get; } = error;

    /// <inheritdoc />
    public override string ToString() => $"{TaskName}: {PreviousState} -> {CurrentState}";
}
