using WeighBridge.Core.Tasks;

namespace WeighBridge.Tests.Tasks;

/// <summary>
/// A background task whose behaviour each test dictates.
/// </summary>
/// <remarks>
/// Signals rather than sleeps: a test waits for <see cref="Started"/> and releases
/// <see cref="Gate"/> when it chooses, so nothing depends on how quickly the machine
/// running the suite happens to schedule a thread.
/// </remarks>
internal sealed class StubBackgroundTask(
    string name = "stub",
    BackgroundTaskPriority priority = BackgroundTaskPriority.Normal,
    Func<StubBackgroundTask, Task>? body = null) : IBackgroundTask
{
    private readonly Func<StubBackgroundTask, Task>? _body = body;

    public string Name { get; } = name;

    public string Description => "Test task";

    public BackgroundTaskPriority Priority { get; } = priority;

    /// <summary>Completes the first time the task body is entered.</summary>
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Held by the task body until a test completes it.</summary>
    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>How many times the body has been entered.</summary>
    public int Executions;

    /// <summary>Progress reporter handed to the body, for tests that report through it.</summary>
    public IProgress<BackgroundTaskProgress>? Progress { get; private set; }

    public async Task ExecuteAsync(
        IProgress<BackgroundTaskProgress> progress,
        CancellationToken cancellationToken)
    {
        Progress = progress;
        Interlocked.Increment(ref Executions);
        Started.TrySetResult();

        if (_body is not null)
        {
            await _body(this).ConfigureAwait(false);
            return;
        }

        // No body supplied: wait to be released, or for cancellation.
        await Gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
