using WeighBridge.Core.Tasks;

namespace WeighBridge.Tests.Tasks;

/// <summary>
/// A <see cref="RecurringTask"/> whose body each test supplies.
/// </summary>
/// <remarks>
/// Exists alongside <see cref="StubBackgroundTask"/> because the manager treats a
/// <see cref="RecurringTask"/> specially — it adopts the schedule the task declares — and
/// that path needs a real derived type to exercise.
/// </remarks>
internal sealed class StubRecurringTask(TimeSpan interval, Func<Task>? body = null)
    : RecurringTask("recurring", "Test recurring task", interval)
{
    private readonly Func<Task>? _body = body;

    /// <summary>Completes the first time the body is entered.</summary>
    public TaskCompletionSource FirstRun { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async Task ExecuteAsync(
        IProgress<BackgroundTaskProgress> progress,
        CancellationToken cancellationToken)
    {
        FirstRun.TrySetResult();

        if (_body is not null)
        {
            await _body().ConfigureAwait(false);
        }
    }
}
