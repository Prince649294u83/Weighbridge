using WeighBridge.Core.Tasks;
using WeighBridge.Services.Tasks;
using Xunit;

namespace WeighBridge.Tests.Tasks;

/// <summary>
/// Covers the admission gate: how many tasks run at once, and which one is admitted next.
/// </summary>
/// <remarks>
/// The ordering rule is the reason this exists at all rather than a bare
/// <see cref="SemaphoreSlim"/>: a semaphore releases in roughly arrival order, so a
/// critical task would queue behind whatever housekeeping happened to ask first.
/// </remarks>
public sealed class BackgroundTaskSchedulerTests
{
    /// <summary>Long enough to be reliable under load, short enough that a hang fails fast.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task AcquireAsync_AdmitsUpToTheLimitWithoutQueueing()
    {
        using var scheduler = new BackgroundTaskScheduler(2);

        using var first = await scheduler.AcquireAsync(BackgroundTaskPriority.Normal, default);
        using var second = await scheduler.AcquireAsync(BackgroundTaskPriority.Normal, default);

        Assert.Equal(2, scheduler.RunningCount);
        Assert.Equal(0, scheduler.QueuedCount);
    }

    [Fact]
    public async Task AcquireAsync_QueuesOnceTheLimitIsReached()
    {
        using var scheduler = new BackgroundTaskScheduler(1);

        using var held = await scheduler.AcquireAsync(BackgroundTaskPriority.Normal, default);

        var queued = scheduler.AcquireAsync(BackgroundTaskPriority.Normal, default).AsTask();

        await WaitForQueueAsync(scheduler, 1);
        Assert.False(queued.IsCompleted);

        held.Dispose();

        using var admitted = await queued.WaitAsync(Timeout);
        Assert.Equal(1, scheduler.RunningCount);
    }

    [Fact]
    public async Task ReleasingASlot_AdmitsTheHighestPriorityWaiterFirst()
    {
        using var scheduler = new BackgroundTaskScheduler(1);

        var held = await scheduler.AcquireAsync(BackgroundTaskPriority.Normal, default);

        // Queued worst-first, so arrival order alone would admit them backwards.
        var low = scheduler.AcquireAsync(BackgroundTaskPriority.Low, default).AsTask();
        var normal = scheduler.AcquireAsync(BackgroundTaskPriority.Normal, default).AsTask();
        var critical = scheduler.AcquireAsync(BackgroundTaskPriority.Critical, default).AsTask();

        await WaitForQueueAsync(scheduler, 3);

        held.Dispose();

        // Critical jumps the queue: an alarm relay must not wait on a log purge.
        (await critical.WaitAsync(Timeout)).Dispose();
        Assert.False(low.IsCompleted);

        (await normal.WaitAsync(Timeout)).Dispose();
        (await low.WaitAsync(Timeout)).Dispose();
    }

    [Fact]
    public async Task WaitersOfEqualPriority_AreAdmittedInArrivalOrder()
    {
        using var scheduler = new BackgroundTaskScheduler(1);

        var held = await scheduler.AcquireAsync(BackgroundTaskPriority.Normal, default);

        var first = scheduler.AcquireAsync(BackgroundTaskPriority.Normal, default).AsTask();
        var second = scheduler.AcquireAsync(BackgroundTaskPriority.Normal, default).AsTask();

        await WaitForQueueAsync(scheduler, 2);

        held.Dispose();

        // Equal priority falls back to fairness, or a task could be starved indefinitely.
        (await first.WaitAsync(Timeout)).Dispose();
        (await second.WaitAsync(Timeout)).Dispose();
    }

    [Fact]
    public async Task ACancelledWaiter_IsSkippedAndItsSlotGoesToTheNextOne()
    {
        using var scheduler = new BackgroundTaskScheduler(1);

        var held = await scheduler.AcquireAsync(BackgroundTaskPriority.Normal, default);

        using var cancellation = new CancellationTokenSource();
        var abandoned = scheduler
            .AcquireAsync(BackgroundTaskPriority.Critical, cancellation.Token).AsTask();
        var survivor = scheduler.AcquireAsync(BackgroundTaskPriority.Low, default).AsTask();

        await WaitForQueueAsync(scheduler, 2);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

        held.Dispose();

        // The released slot must not vanish with the waiter that no longer wants it.
        using var admitted = await survivor.WaitAsync(Timeout);
        Assert.Equal(1, scheduler.RunningCount);
    }

    [Fact]
    public async Task AcquireAsync_ThrowsWhenTheTokenIsAlreadyCancelled()
    {
        using var scheduler = new BackgroundTaskScheduler(1);
        using var cancellation = new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scheduler.AcquireAsync(BackgroundTaskPriority.Normal, cancellation.Token).AsTask());
    }

    [Fact]
    public async Task DisposingASlotTwice_DoesNotReleaseTwoSlots()
    {
        using var scheduler = new BackgroundTaskScheduler(2);

        var slot = await scheduler.AcquireAsync(BackgroundTaskPriority.Normal, default);

        slot.Dispose();
        slot.Dispose();

        // Double disposal is easy to write in a using-plus-finally; it must not hand out
        // capacity the scheduler does not have.
        Assert.Equal(0, scheduler.RunningCount);
    }

    [Fact]
    public async Task Dispose_FailsWaitersRatherThanLeavingThemHanging()
    {
        var scheduler = new BackgroundTaskScheduler(1);

        using var held = await scheduler.AcquireAsync(BackgroundTaskPriority.Normal, default);

        var waiting = scheduler.AcquireAsync(BackgroundTaskPriority.Normal, default).AsTask();
        await WaitForQueueAsync(scheduler, 1);

        scheduler.Dispose();

        // Shutdown has to end the wait, or a run loop parks here and the process never exits.
        await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting);
    }

    /// <summary>Spins until the scheduler reports <paramref name="count"/> waiters.</summary>
    /// <remarks>
    /// Queueing happens on the calling thread, so this settles immediately in practice; the
    /// timeout is a guard against a hang rather than a timing assumption.
    /// </remarks>
    private static async Task WaitForQueueAsync(BackgroundTaskScheduler scheduler, int count)
    {
        using var timeout = new CancellationTokenSource(Timeout);

        while (scheduler.QueuedCount < count)
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }
}
