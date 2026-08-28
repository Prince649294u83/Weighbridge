using System.ComponentModel;

namespace WeighBridge.Core.Undo;

/// <summary>
/// Holds what can be undone and does the undoing.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline pushes onto this; nothing else should. A command that recorded itself here
/// directly could record an undo entry for an operation that then failed.
/// </para>
/// <para>
/// Raises change notification so a menu item binds its enabled state straight to
/// <see cref="CanUndo"/> without a ViewModel relaying it.
/// </para>
/// </remarks>
public interface IUndoManager : INotifyPropertyChanged
{
    /// <summary>True when there is something to undo.</summary>
    bool CanUndo { get; }

    /// <summary>True when something has been undone and can be put back.</summary>
    bool CanRedo { get; }

    /// <summary>What undoing would undo, for a menu item's text.</summary>
    string? UndoDescription { get; }

    /// <summary>What redoing would redo.</summary>
    string? RedoDescription { get; }

    /// <summary>The undo history, most recent first.</summary>
    IReadOnlyList<UndoEntry> History { get; }

    /// <summary>Raised whenever the history changes, including after a clear.</summary>
    event EventHandler? HistoryChanged;

    /// <summary>
    /// Records an executed command as undoable.
    /// </summary>
    /// <remarks>
    /// Clears the redo stack: once new work has happened, redoing something from before it
    /// would apply a change on top of state that has moved.
    /// </remarks>
    void Push(IUndoableCommand command, string? correlationId = null);

    /// <summary>Undoes the most recent entry.</summary>
    Task<UndoResult> UndoAsync(CancellationToken cancellationToken = default);

    /// <summary>Redoes the most recently undone entry.</summary>
    Task<UndoResult> RedoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets everything.
    /// </summary>
    /// <remarks>
    /// Called when the context the history was recorded against is gone — a new shift, a
    /// different vehicle, a module the operator has navigated away from. An undo entry that
    /// outlives its context is worse than no entry at all.
    /// </remarks>
    void Clear();

    /// <summary>
    /// Groups everything pushed while the returned scope is open into one entry.
    /// </summary>
    /// <remarks>
    /// Nested scopes join the outermost group rather than creating their own, so a bulk
    /// operation calling a routine that already groups still produces one entry.
    /// </remarks>
    IDisposable BeginCompound(string description);
}
