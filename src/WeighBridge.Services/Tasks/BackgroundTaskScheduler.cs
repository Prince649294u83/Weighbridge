using WeighBridge.Core.Tasks;

namespace WeighBridge.Services.Tasks;

/// <summary>
/// Limits how many background tasks execute at once, admitting the most important
/// waiter first.
/// </summary>
/// <remarks>
/// <para>
/// A weighbridge terminal is a modest machine that also has to stay responsive while an
/// operator is mid-transaction. Letting every registered task run the moment it is due
/// would put a log purge, a server sync and a database probe on the CPU together at the
/// worst possible time. This gate keeps that to a fixed number.
/// </para>
/// <para>
/// A plain <see cref="SemaphoreSlim"/> would do the counting, but it releases waiters in
/// roughly arrival order — so a <see cref="BackgroundTaskPriority.Critical"/> reconnect
/// queued behind six housekeeping tasks would wait for all six. Waiters are held in a
/// list here and the highest priority is admitted first, first-come-first-served within
/// a priority so nothing at a given level can starve.
/// </para>
/// </remarks>
public sealed class BackgroundTaskScheduler : IDisposable
{
    private readonly List<Waiter> _waiters = [];
    private readonly object _gate = new();
    private readonly int _maxConcurrency;

    private int _running;
    private bool _disposed;

    /// <summary>
    /// Creates a scheduler admitting at most <paramref name="maxConcurrency"/> tasks at once.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxConcurrency"/> is less than one, which would admit nothing.
    /// </exception>
    public BackgroundTaskScheduler(int maxConcurrency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);
        _maxConcurrency = maxConcurrency;
    }

    /// <summary>Most tasks allowed to run at the same time.</summary>
    public int MaxConcurrency => _maxConcurrency;

    /// <summary>Tasks executing right now.</summary>
    public int RunningCount
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    /// <summary>Tasks admitted but waiting for a slot.</summary>
    public int QueuedCount
    {
        get
        {
            lock (_gate)
            {
                return _waiters.Count;
            }
        }
    }

    /// <summary>
    /// Waits for a slot and returns a lease that frees it when disposed.
    /// </summary>
    /// <param name="priority">Decides position in the queue when the scheduler is full.</param>
    /// <param name="cancellationToken">Abandons the wait.</param>
    /// <returns>
    /// A lease. Dispose it — in a <c>finally</c> or a <c>using</c> — or the slot is never
    /// returned and the scheduler deadlocks after <see cref="MaxConcurrency"/> leaks.
    /// </returns>
    /// <exception cref="OperationCanceledException">The wait was cancelled.</exception>
    public ValueTask<IDisposable> AcquireAsync(
        BackgroundTaskPriority priority,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        Waiter waiter;

        lock (_gate)
        {
            // Fast path: a slot is free, so no queue entry and no allocation beyond
            // the lease itself. This is the common case with a handful of tasks.
            if (_running < _maxConcurrency)
            {
                _running++;
                return new ValueTask<IDisposable>(new Lease(this));
            }

            waiter = new Waiter(priority);
            _waiters.Add(waiter);
        }

        return new ValueTask<IDisposable>(WaitAsync(waiter, cancellationToken));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        List<Waiter> pending;

        lock (_gate)
        {
            pending = [.. _waiters];
            _waiters.Clear();
        }

        // Anything still queued is failed rather than left hanging: a task awaiting a
        // slot that will never come would keep shutdown waiting forever.
        foreach (var waiter in pending)
        {
            waiter.Completion.TrySetException(
                new ObjectDisposedException(nameof(BackgroundTaskScheduler)));
        }
    }

    /// <summary>
    /// Awaits admission, removing the waiter from the queue if the wait is cancelled.
    /// </summary>
    private async Task<IDisposable> WaitAsync(Waiter waiter, CancellationToken cancellationToken)
    {
        // The registration is disposed before the result is returned, so a token
        // cancelled later cannot touch a waiter that has already been admitted.
        await using var registration = cancellationToken.Register(static state =>
        {
            var (scheduler, cancelled) = ((BackgroundTaskScheduler, Waiter))state!;
            scheduler.Cancel(cancelled);
        }, (this, waiter));

        await waiter.Completion.Task.ConfigureAwait(false);
        return new Lease(this);
    }

    /// <summary>Removes a cancelled waiter, but only if it was not already admitted.</summary>
    private void Cancel(Waiter waiter)
    {
        lock (_gate)
        {
            if (!_waiters.Remove(waiter))
            {
                // Already admitted. Its slot is held, so the lease must still be
                // handed out and disposed; failing here would leak the slot.
                return;
            }
        }

        waiter.Completion.TrySetCanceled();
    }

    /// <summary>
    /// Returns a slot and admits the highest-priority waiter, if there is one.
    /// </summary>
    private void Release()
    {
        while (true)
        {
            Waiter? next;

            lock (_gate)
            {
                next = TakeNextWaiter();

                if (next is null)
                {
                    _running--;
                    return;
                }

                // The slot passes straight to the waiter, so _running is unchanged.
            }

            // Completed outside the lock: a continuation running inline must not be
            // holding the scheduler's lock when it calls back in to acquire again.
            if (next.Completion.TrySetResult())
            {
                return;
            }

            // The waiter was cancelled between being taken and being completed. Loop
            // to find another rather than dropping the slot on the floor.
        }
    }

    /// <summary>
    /// Removes and returns the highest-priority waiter. Caller holds the lock.
    /// </summary>
    private Waiter? TakeNextWaiter()
    {
        if (_waiters.Count == 0)
        {
            return null;
        }

        var bestIndex = 0;

        for (var i = 1; i < _waiters.Count; i++)
        {
            // Strictly greater keeps arrival order within a priority, so a task at a
            // given level cannot be starved by later arrivals at the same level.
            if (_waiters[i].Priority > _waiters[bestIndex].Priority)
            {
                bestIndex = i;
            }
        }

        var waiter = _waiters[bestIndex];
        _waiters.RemoveAt(bestIndex);
        return waiter;
    }

    /// <summary>A queued request for a slot.</summary>
    private sealed class Waiter(BackgroundTaskPriority priority)
    {
        public BackgroundTaskPriority Priority { get; } = priority;

        /// <remarks>
        /// Runs continuations asynchronously so completing a waiter never executes the
        /// resumed task's body inside <see cref="Release"/>.
        /// </remarks>
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Holds a slot until disposed.</summary>
    private sealed class Lease(BackgroundTaskScheduler scheduler) : IDisposable
    {
        private BackgroundTaskScheduler? _scheduler = scheduler;

        public void Dispose()
        {
            // Exchanged rather than flagged: a double dispose would otherwise release
            // two slots and let the scheduler exceed its own limit.
            var owner = Interlocked.Exchange(ref _scheduler, null);
            owner?.Release();
        }
    }
}
