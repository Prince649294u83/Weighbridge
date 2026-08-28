using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Undo;
using WeighBridge.Services.Undo;
using WeighBridge.Tests.Infrastructure;
using Xunit;

namespace WeighBridge.Tests.Undo;

/// <summary>
/// Two overlapping undo/redo calls used to fetch the same history entry and compensate it
/// twice â€” a double-compensation that corrupts whatever the commands touch. The manager now
/// serialises compensating operations; the second caller waits and sees the honest answer.
/// </summary>
public sealed class UndoConcurrencyTests
{
    private static UndoManager CreateManager() => new(
        Options.Create(new UndoOptions { MaxDepth = 20 }),
        new ApplicationLogger(LoggerFactory.Create(_ => { }), new TestApplicationInfoService()));

    /// <summary>Counts compensations and can hold mid-operation to force overlap.</summary>
    private sealed class CountingCommand : IUndoableCommand
    {
        private readonly TaskCompletionSource? _startedSignal;
        private readonly TimeSpan _holdDuration;
        private int _undoCount;
        private int _redoCount;

        public CountingCommand(string description, TaskCompletionSource? startedSignal = null, TimeSpan holdDuration = default)
        {
            UndoDescription = description;
            _startedSignal = startedSignal;
            _holdDuration = holdDuration;
        }

        public string UndoDescription { get; }
        public int UndoCount => Volatile.Read(ref _undoCount);
        public int RedoCount => Volatile.Read(ref _redoCount);

        public Task ExecuteAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task UndoAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _undoCount);
            _startedSignal?.TrySetResult();
            if (_holdDuration > TimeSpan.Zero)
            {
                await Task.Delay(_holdDuration, cancellationToken);
            }
        }

        public async Task RedoAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _redoCount);
            _startedSignal?.TrySetResult();
            if (_holdDuration > TimeSpan.Zero)
            {
                await Task.Delay(_holdDuration, cancellationToken);
            }
        }
    }

    [Fact]
    public async Task OverlappingUndos_CompensateEachEntryOnce()
    {
        var manager = CreateManager();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new CountingCommand("Slow", started, TimeSpan.FromMilliseconds(80));
        var quick = new CountingCommand("Quick");

        manager.Push(quick, null);
        manager.Push(slow, null);

        var firstUndo = manager.UndoAsync();

        // Wait until the first undo has claimed its entry and is mid-compensation.
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondUndo = manager.UndoAsync();
        await Task.WhenAll(firstUndo, secondUndo).WaitAsync(TimeSpan.FromSeconds(10));
        var secondResult = await secondUndo;

        // The slow entry was compensated exactly once. The overlapping call did NOT
        // re-compensate it — it waited, then honestly moved on to the next entry.
        Assert.Equal(1, slow.UndoCount);
        Assert.Equal(UndoOutcome.Succeeded, secondResult.Outcome);
        Assert.Equal(1, quick.UndoCount);
        Assert.False(manager.CanUndo);
        Assert.True(manager.CanRedo);
    }

    [Fact]
    public async Task OverlappingRedo_DoesNotReapplyTheSameEntryTwice()
    {
        var manager = CreateManager();
        var redoStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observable = new CountingCommand("Observable redo", redoStarted, TimeSpan.FromMilliseconds(80));

        manager.Push(observable, null);
        Assert.Equal(UndoOutcome.Succeeded, (await manager.UndoAsync()).Outcome);

        var first = manager.RedoAsync();
        await redoStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = manager.RedoAsync();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, observable.RedoCount);
        Assert.False(manager.CanRedo);
    }
}
