using System.ComponentModel;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Diagnostics;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Undo;

namespace WeighBridge.Services.Undo;

/// <summary>
/// The undo/redo history.
/// </summary>
/// <remarks>
/// <para>
/// One entry per completed operation; a compound operation records as one. Redo entries are
/// kept alongside undo entries rather than in a second list â€” undoing three things then
/// redoing the middle one is impossible by design, so one list plus a pointer is the honest
/// model.
/// </para>
/// <para>
/// All state lives under <c>_gate</c>. Nothing is called while the lock is held: the
/// compensating operation itself is invoked outside it, and a command that asked a question
/// about the history mid-undo would deadlock otherwise.
/// </para>
/// <para>
/// No WPF and no dispatcher. <see cref="History"/> hands back a snapshot rather than an
/// observable collection, so there is no bound collection to marshal onto the
/// user-interface thread; a menu binds to <see cref="CanUndo"/> and reads the snapshot when
/// <see cref="HistoryChanged"/> tells it to.
/// </para>
/// </remarks>
public sealed class UndoManager : IUndoManager
{
    private readonly List<UndoEntry> _entries = [];
    private readonly object _gate = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly IApplicationLogger _logger;

    private int _position; // Count of entries before the pointer: how many are undoable.
    private List<UndoEntry>? _group;

    /// <summary>Creates an empty history.</summary>
    public UndoManager(IOptions<UndoOptions> options, IApplicationLogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        MaxDepth = options.Value.MaxDepth;
    }

    private int MaxDepth { get; }

    /// <inheritdoc />
    public bool CanUndo
    {
        get
        {
            lock (_gate)
            {
                return _position > 0;
            }
        }
    }

    /// <inheritdoc />
    public bool CanRedo
    {
        get
        {
            lock (_gate)
            {
                return _position < _entries.Count;
            }
        }
    }

    /// <inheritdoc />
    public string? UndoDescription
    {
        get
        {
            lock (_gate)
            {
                return _position == 0 ? null : _entries[_position - 1].Description;
            }
        }
    }

    /// <inheritdoc />
    public string? RedoDescription
    {
        get
        {
            lock (_gate)
            {
                return _position >= _entries.Count ? null : _entries[_position].Description;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<UndoEntry> History
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries.AsEnumerable().Reverse()];
            }
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc />
    public event EventHandler? HistoryChanged;

    /// <inheritdoc />
    public void Push(IUndoableCommand command, string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(command);

        var description = command.UndoDescription;

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "An undoable command must describe what it undoes.", nameof(command));
        }

        var entry = new UndoEntry(
            command,
            description,
            DateTimeOffset.Now,
            correlationId ?? CorrelationScope.Current);

        int depth;

        lock (_gate)
        {
            // The redo stack is dead the moment new work happens.
            var tail = _entries.Count - _position;
            if (tail > 0)
            {
                _entries.RemoveRange(_position, tail);
            }

            if (_group is not null)
            {
                _group.Add(entry);
            }
            else
            {
                _entries.Add(entry);

                if (_entries.Count > MaxDepth)
                {
                    _entries.RemoveAt(0);
                }

                // Everything in the list is now behind the pointer: a freshly pushed
                // command is the first thing an undo should reach.
                _position = _entries.Count;
            }

            depth = _entries.Count;
        }

        _logger.Debug("Undo entry recorded: {Description} ({Entries} in history)", description, depth);

        RaiseHistoryChanged();
    }

    /// <inheritdoc />
    public async Task<UndoResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        // One compensating operation at a time. Two overlapping undos used to fetch the
        // same entry and compensate it twice; the second caller now waits here, and by
        // the time it runs the history has moved, so it sees the honest answer. The gate
        // deliberately ignores the caller's token: a pre-cancelled token must still flow
        // into the core, which reports Cancelled rather than throwing from the wait.
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await UndoCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<UndoResult> UndoCoreAsync(CancellationToken cancellationToken)
    {
        UndoEntry entry;

        lock (_gate)
        {
            if (_position == 0)
            {
                return UndoResult.Nothing;
            }

            entry = _entries[_position - 1];
        }

        _logger.Information("Undoing: {Description}", entry.Description);

        try
        {
            // Checked before the compensating operation runs, not only inside it: a command
            // that ignores its token would otherwise write to the database during shutdown.
            cancellationToken.ThrowIfCancellationRequested();

            await entry.Command.UndoAsync(cancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                _position--;
            }

            _logger.Information("Undid: {Description}", entry.Description);

            RaiseHistoryChanged();

            return UndoResult.Success(entry.Description);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Undo of {Description} was cancelled", entry.Description);

            return UndoResult.Cancelled(entry.Description);
        }
        catch (Exception error)
        {
            // A failed compensating operation must not sit in the history waiting to be
            // retried against state that has already moved on.
            _logger.Error(error, "Undo of {Description} failed", entry.Description);

            lock (_gate)
            {
                _entries.Remove(entry);
                if (_position > _entries.Count)
                {
                    _position = _entries.Count;
                }
            }

            RaiseHistoryChanged();

            return UndoResult.Failed(entry.Description, error);
        }
    }

    /// <inheritdoc />
    public async Task<UndoResult> RedoAsync(CancellationToken cancellationToken = default)
    {
        // Same serialisation as undo: a redo racing an undo would re-apply the entry the
        // undo is still compensating.
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await RedoCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<UndoResult> RedoCoreAsync(CancellationToken cancellationToken)
    {
        UndoEntry entry;

        lock (_gate)
        {
            if (_position >= _entries.Count)
            {
                return UndoResult.Nothing;
            }

            entry = _entries[_position];
        }

        _logger.Information("Redoing: {Description}", entry.Description);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await entry.Command.RedoAsync(cancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                _position++;
            }

            _logger.Information("Redid: {Description}", entry.Description);

            RaiseHistoryChanged();

            return UndoResult.Success(entry.Description);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Redo of {Description} was cancelled", entry.Description);

            return UndoResult.Cancelled(entry.Description);
        }
        catch (Exception error)
        {
            _logger.Error(error, "Redo of {Description} failed", entry.Description);

            lock (_gate)
            {
                _entries.Remove(entry);
                if (_position > _entries.Count)
                {
                    _position = _entries.Count;
                }
            }

            RaiseHistoryChanged();

            return UndoResult.Failed(entry.Description, error);
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        // Clearing while an undo is mid-flight would pull the list out from under it.
        _operationGate.Wait();
        try
        {
            lock (_gate)
            {
                _entries.Clear();
                _position = 0;
            }
        }
        finally
        {
            _operationGate.Release();
        }

        _logger.Information("Undo history cleared");

        RaiseHistoryChanged();
    }

    /// <inheritdoc />
    public IDisposable BeginCompound(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        lock (_gate)
        {
            if (_group is not null)
            {
                // Nested group: join the outer one rather than close it early.
                return new NoOpDisposable();
            }

            _group = [];
        }

        return new CompoundScope(this, description);
    }

    /// <summary>
    /// Closes a compound scope. The pushed commands were collected into
    /// <c>_group</c> and are sealed into a single entry.
    /// </summary>
    private void EndCompound(string description)
    {
        List<UndoEntry>? collected;
        bool becameCompound = false;

        lock (_gate)
        {
            collected = _group;
            _group = null;

            // One command still becomes an entry, under the group's description. Dropping it
            // would lose the operation from the history entirely, and a scope that happened
            // to collect one item is not a scope that did nothing.
            if (collected is { Count: > 0 })
            {
                var command = new CompoundUndoableCommand(
                    description,
                    collected.Select(entry => entry.Command));

                var entry = new UndoEntry(
                    command,
                    description,
                    DateTimeOffset.Now,
                    CorrelationScope.Current);

                _entries.Add(entry);
                if (_entries.Count > MaxDepth)
                {
                    _entries.RemoveAt(0);
                }

                _position = _entries.Count;

                becameCompound = true;
            }
        }

        if (becameCompound)
        {
            _logger.Debug(
                "Compound undo entry recorded: {Description} ({Count} commands)",
                description,
                collected!.Count);

            RaiseHistoryChanged();
        }
    }

    /// <summary>Announces the history moved, on the caller's thread.</summary>
    private void RaiseHistoryChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanUndo)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRedo)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UndoDescription)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RedoDescription)));

        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class CompoundScope(UndoManager owner, string description) : IDisposable
    {
        private int _closed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
            {
                return;
            }

            owner.EndCompound(description);
        }
    }
}
