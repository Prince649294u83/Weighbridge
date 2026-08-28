using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Undo;
using WeighBridge.Services.Undo;
using WeighBridge.Tests.Infrastructure;

namespace WeighBridge.Tests.Undo;

/// <summary>
/// Covers the undo framework: history order, redo invalidation, depth trimming,
/// compound grouping, failure handling and cancellation.
/// </summary>
public sealed class UndoManagerTests
{
    private static UndoManager CreateManager(int maxDepth = 20)
        => new(
            Options.Create(new UndoOptions { MaxDepth = maxDepth }),
            new ApplicationLogger(NullLoggerFactory.Instance, new TestApplicationInfoService()));

    /// <summary>A command that records what was asked of it, in order.</summary>
    private sealed class RecordingCommand(string description, List<string> log) : IUndoableCommand
    {
        public string UndoDescription { get; } = description;

        public int UndoCount { get; private set; }

        public int RedoCount { get; private set; }

        public Task UndoAsync(CancellationToken cancellationToken = default)
        {
            UndoCount++;
            log.Add($"undo:{UndoDescription}");
            return Task.CompletedTask;
        }

        public Task RedoAsync(CancellationToken cancellationToken = default)
        {
            RedoCount++;
            log.Add($"redo:{UndoDescription}");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingCommand(string description) : IUndoableCommand
    {
        public string UndoDescription { get; } = description;

        public Task UndoAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The weighment was already closed.");

        public Task RedoAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class CancellingCommand : IUndoableCommand
    {
        public string UndoDescription => "Cancel weighment";

        public Task UndoAsync(CancellationToken cancellationToken = default)
            => Task.FromCanceled(new CancellationToken(canceled: true));

        public Task RedoAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    [Fact]
    public void NewManager_HasNothingToUndoOrRedo()
    {
        var manager = CreateManager();

        Assert.False(manager.CanUndo);
        Assert.False(manager.CanRedo);
        Assert.Null(manager.UndoDescription);
        Assert.Null(manager.RedoDescription);
        Assert.Empty(manager.History);
    }

    [Fact]
    public void Push_MakesTheCommandUndoable()
    {
        var manager = CreateManager();
        var log = new List<string>();

        manager.Push(new RecordingCommand("Cancel weighment", log));

        Assert.True(manager.CanUndo);
        Assert.Equal("Cancel weighment", manager.UndoDescription);
        Assert.Single(manager.History);
    }

    [Fact]
    public void History_IsMostRecentFirst()
    {
        var manager = CreateManager();
        var log = new List<string>();

        manager.Push(new RecordingCommand("First", log));
        manager.Push(new RecordingCommand("Second", log));

        Assert.Equal(["Second", "First"], manager.History.Select(entry => entry.Description));
    }

    [Fact]
    public void Push_CarriesCorrelationId()
    {
        var manager = CreateManager();
        var log = new List<string>();

        manager.Push(new RecordingCommand("Cancel weighment", log), "corr-42");

        // So an entry in the history can be traced to the log lines and audit record.
        Assert.Equal("corr-42", manager.History[0].CorrelationId);
    }

    [Fact]
    public async Task UndoAsync_RunsTheCompensatingOperation()
    {
        var manager = CreateManager();
        var log = new List<string>();
        var command = new RecordingCommand("Cancel weighment", log);
        manager.Push(command);

        var result = await manager.UndoAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("Cancel weighment", result.Description);
        Assert.Equal(1, command.UndoCount);
        Assert.False(manager.CanUndo);
        Assert.True(manager.CanRedo);
    }

    [Fact]
    public async Task UndoAsync_TakesTheMostRecentFirst()
    {
        var manager = CreateManager();
        var log = new List<string>();
        manager.Push(new RecordingCommand("First", log));
        manager.Push(new RecordingCommand("Second", log));

        await manager.UndoAsync();
        await manager.UndoAsync();

        Assert.Equal(["undo:Second", "undo:First"], log);
    }

    [Fact]
    public async Task UndoAsync_EmptyHistory_ReportsNothingToDo()
    {
        var result = await CreateManager().UndoAsync();

        Assert.Equal(UndoOutcome.NothingToDo, result.Outcome);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RedoAsync_PutsTheChangeBack()
    {
        var manager = CreateManager();
        var log = new List<string>();
        var command = new RecordingCommand("Cancel weighment", log);
        manager.Push(command);
        await manager.UndoAsync();

        var result = await manager.RedoAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, command.RedoCount);
        Assert.True(manager.CanUndo);
        Assert.False(manager.CanRedo);
    }

    [Fact]
    public async Task RedoAsync_EmptyRedoStack_ReportsNothingToDo()
    {
        var manager = CreateManager();
        manager.Push(new RecordingCommand("Cancel weighment", []));

        var result = await manager.RedoAsync();

        Assert.Equal(UndoOutcome.NothingToDo, result.Outcome);
    }

    [Fact]
    public async Task Push_AfterUndo_DiscardsTheRedoStack()
    {
        var manager = CreateManager();
        var log = new List<string>();
        manager.Push(new RecordingCommand("First", log));
        await manager.UndoAsync();

        Assert.True(manager.CanRedo);

        manager.Push(new RecordingCommand("Second", log));

        // Redoing the first now would apply a change on top of state that has moved.
        Assert.False(manager.CanRedo);
        Assert.Null(manager.RedoDescription);
    }

    [Fact]
    public async Task UndoAsync_Failing_DropsTheEntryRatherThanLeavingItToRetry()
    {
        var manager = CreateManager();
        manager.Push(new ThrowingCommand("Cancel weighment"));

        var result = await manager.UndoAsync();

        Assert.Equal(UndoOutcome.Failed, result.Outcome);
        Assert.IsType<InvalidOperationException>(result.Error);

        // Dropped, not retained: retrying it would run against state that has moved on.
        Assert.False(manager.CanUndo);
        Assert.False(manager.CanRedo);
        Assert.Empty(manager.History);
    }

    [Fact]
    public async Task UndoAsync_Failing_DoesNotSwallowTheReason()
    {
        var manager = CreateManager();
        manager.Push(new ThrowingCommand("Cancel weighment"));

        var result = await manager.UndoAsync();

        Assert.Equal("The weighment was already closed.", result.Error!.Message);
    }

    [Fact]
    public async Task UndoAsync_Cancelled_ReportsCancelledAndKeepsTheEntry()
    {
        var manager = CreateManager();
        manager.Push(new CancellingCommand());

        var result = await manager.UndoAsync();

        Assert.Equal(UndoOutcome.Cancelled, result.Outcome);

        // A cancelled undo never ran, so the entry is still valid and stays put.
        Assert.True(manager.CanUndo);
    }

    [Fact]
    public async Task UndoAsync_AlreadyCancelledToken_DoesNotRunTheCommand()
    {
        var manager = CreateManager();
        var log = new List<string>();
        var command = new RecordingCommand("Cancel weighment", log);
        manager.Push(command);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await manager.UndoAsync(cancellation.Token);

        Assert.Equal(UndoOutcome.Cancelled, result.Outcome);
        Assert.Equal(0, command.UndoCount);
    }

    [Fact]
    public void Push_BeyondMaxDepth_DropsTheOldest()
    {
        var manager = CreateManager(maxDepth: 3);
        var log = new List<string>();

        foreach (var index in Enumerable.Range(1, 5))
        {
            manager.Push(new RecordingCommand($"Change {index}", log));
        }

        Assert.Equal(3, manager.History.Count);
        Assert.Equal(["Change 5", "Change 4", "Change 3"], manager.History.Select(entry => entry.Description));
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var manager = CreateManager();
        var log = new List<string>();
        manager.Push(new RecordingCommand("Cancel weighment", log));

        manager.Clear();

        Assert.False(manager.CanUndo);
        Assert.False(manager.CanRedo);
        Assert.Empty(manager.History);
    }

    [Fact]
    public async Task Clear_AlsoForgetsTheRedoStack()
    {
        var manager = CreateManager();
        manager.Push(new RecordingCommand("Cancel weighment", []));
        await manager.UndoAsync();

        manager.Clear();

        Assert.False(manager.CanRedo);
    }

    [Fact]
    public async Task BeginCompound_GroupsPushesIntoOneEntry()
    {
        var manager = CreateManager();
        var log = new List<string>();

        using (manager.BeginCompound("Import 3 vehicles"))
        {
            manager.Push(new RecordingCommand("Vehicle 1", log));
            manager.Push(new RecordingCommand("Vehicle 2", log));
            manager.Push(new RecordingCommand("Vehicle 3", log));
        }

        var entry = Assert.Single(manager.History);
        Assert.Equal("Import 3 vehicles", entry.Description);

        await manager.UndoAsync();

        // Reverse order: undoing forwards would remove a record something later in the
        // batch still points at.
        Assert.Equal(["undo:Vehicle 3", "undo:Vehicle 2", "undo:Vehicle 1"], log);
    }

    [Fact]
    public async Task BeginCompound_RedoRunsInForwardOrder()
    {
        var manager = CreateManager();
        var log = new List<string>();

        using (manager.BeginCompound("Import 2 vehicles"))
        {
            manager.Push(new RecordingCommand("Vehicle 1", log));
            manager.Push(new RecordingCommand("Vehicle 2", log));
        }

        await manager.UndoAsync();
        log.Clear();
        await manager.RedoAsync();

        Assert.Equal(["redo:Vehicle 1", "redo:Vehicle 2"], log);
    }

    [Fact]
    public void BeginCompound_Nested_JoinsTheOutermostGroup()
    {
        var manager = CreateManager();
        var log = new List<string>();

        using (manager.BeginCompound("Outer"))
        {
            manager.Push(new RecordingCommand("A", log));

            using (manager.BeginCompound("Inner"))
            {
                manager.Push(new RecordingCommand("B", log));
            }

            manager.Push(new RecordingCommand("C", log));
        }

        // A bulk operation calling a routine that already groups still produces one entry.
        var entry = Assert.Single(manager.History);
        Assert.Equal("Outer", entry.Description);
        Assert.Equal(3, Assert.IsType<CompoundUndoableCommand>(entry.Command).Count);
    }

    [Fact]
    public void BeginCompound_WithNothingPushed_RecordsNoEntry()
    {
        var manager = CreateManager();

        using (manager.BeginCompound("Import that found nothing to do"))
        {
        }

        Assert.Empty(manager.History);
        Assert.False(manager.CanUndo);
    }

    [Fact]
    public void BeginCompound_SinglePush_StillProducesTheGroupDescription()
    {
        var manager = CreateManager();

        using (manager.BeginCompound("Import 1 vehicle"))
        {
            manager.Push(new RecordingCommand("Vehicle 1", []));
        }

        Assert.Equal("Import 1 vehicle", manager.UndoDescription);
    }

    [Fact]
    public void HistoryChanged_RaisedOnPushAndClear()
    {
        var manager = CreateManager();
        var raised = 0;
        manager.HistoryChanged += (_, _) => raised++;

        manager.Push(new RecordingCommand("Cancel weighment", []));
        manager.Clear();

        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task PropertyChanged_AnnouncesCanUndoAndCanRedo()
    {
        var manager = CreateManager();
        var changed = new List<string?>();
        manager.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        manager.Push(new RecordingCommand("Cancel weighment", []));
        await manager.UndoAsync();

        Assert.Contains(nameof(IUndoManager.CanUndo), changed);
        Assert.Contains(nameof(IUndoManager.CanRedo), changed);
    }

    [Fact]
    public async Task ConcurrentPushes_AllLand()
    {
        var manager = CreateManager(maxDepth: 100);

        await Task.WhenAll(Enumerable.Range(0, 50).Select(index =>
            Task.Run(() => manager.Push(new RecordingCommand($"Change {index}", [])))));

        Assert.Equal(50, manager.History.Count);
    }

    [Fact]
    public void Push_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => CreateManager().Push(null!));

    [Fact]
    public void BeginCompound_BlankDescription_Throws()
        => Assert.ThrowsAny<ArgumentException>(() => CreateManager().BeginCompound("  "));
}
