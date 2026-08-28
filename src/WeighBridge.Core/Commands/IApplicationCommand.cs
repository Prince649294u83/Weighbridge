using WeighBridge.Core.Security;

namespace WeighBridge.Core.Commands;

/// <summary>
/// Something the application can be asked to do, on behalf of an operator.
/// </summary>
/// <remarks>
/// <para>
/// A command is the unit the pipeline validates, authorises, runs, logs, audits, announces
/// and can undo. Writing one is how a module gets all of that without repeating any of it.
/// </para>
/// <para>
/// A command must not touch WPF. It runs on whatever thread the pipeline is on, often a
/// worker; the busy indicator, the dialogs and the notifications are the pipeline's job, and
/// a command that opened a window could not be tested or run from a background task.
/// </para>
/// <para>
/// Optional facets are separate interfaces a command opts into:
/// <see cref="Validation.IValidatable"/> to be checked first,
/// <see cref="IRequiresPermission"/> to be gated,
/// <see cref="Undo.IUndoableCommand"/> to be undoable.
/// </para>
/// </remarks>
public interface IApplicationCommand
{
    /// <summary>
    /// What this command is, for the log, the audit trail and the busy indicator.
    /// </summary>
    /// <remarks>
    /// Phrased as the action in progress — "Save weighment", not "SaveWeighmentCommand" —
    /// because it is shown to the operator, not only written to a file.
    /// </remarks>
    string Name { get; }

    /// <summary>
    /// Runs the command.
    /// </summary>
    /// <remarks>
    /// Returns a result for an expected outcome and throws for an unexpected one. A
    /// database that refuses a write is a thrown exception, which the pipeline catches,
    /// logs with its stack and turns into a failed result — nothing is swallowed.
    /// </remarks>
    Task<CommandResult> ExecuteAsync(CommandContext context);
}

/// <summary>
/// A command that produces something its caller needs.
/// </summary>
/// <typeparam name="TValue">Type of the produced value.</typeparam>
public interface IApplicationCommand<TValue> : IApplicationCommand
{
    /// <summary>Runs the command and returns what it produced.</summary>
    new Task<CommandResult<TValue>> ExecuteAsync(CommandContext context);
}

/// <summary>
/// A command that can say whether it is currently runnable.
/// </summary>
/// <remarks>
/// For binding a button's enabled state. Distinct from validation: a command may be
/// perfectly runnable and still fail validation once the operator submits it — the
/// difference is whether the operator can even attempt the action.
/// </remarks>
public interface IConditionalCommand
{
    /// <summary>Whether the command may currently be attempted.</summary>
    bool CanExecute();
}
