namespace WeighBridge.Core.Undo;

/// <summary>
/// A command that can put back what it changed.
/// </summary>
/// <remarks>
/// <para>
/// Undo is not a rollback. By the time a command is undoable it has already committed, and
/// undoing it means running a second, compensating operation — a cancelled weighment is
/// reinstated by writing it back, not by rewinding a transaction. That compensating
/// operation is audited like any other, because it is one.
/// </para>
/// <para>
/// A command should only claim to be undoable if it genuinely can be. Printing a slip
/// cannot: the paper is out of the printer. Such a command implements
/// <c>IApplicationCommand</c> alone and simply never enters the history.
/// </para>
/// </remarks>
public interface IUndoableCommand
{
    /// <summary>
    /// What the operator sees in an undo affordance. Phrased as the action, so the shell
    /// can render "Undo cancel weighment" without composing sentences.
    /// </summary>
    string UndoDescription { get; }

    /// <summary>
    /// Puts back what the command changed.
    /// </summary>
    /// <remarks>
    /// Throwing here means the undo failed, and the manager treats it as a hard failure: an
    /// entry that could not be undone is removed from the history rather than left to be
    /// retried against state that has already moved on.
    /// </remarks>
    Task UndoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the change again after an undo.
    /// </summary>
    /// <remarks>
    /// Separate from the original execution because redo is not always a re-run: the
    /// original may have captured an identity or a timestamp that must be reused rather
    /// than regenerated.
    /// </remarks>
    Task RedoAsync(CancellationToken cancellationToken = default);
}
