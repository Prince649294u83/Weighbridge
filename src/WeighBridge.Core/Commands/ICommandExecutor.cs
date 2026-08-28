namespace WeighBridge.Core.Commands;

/// <summary>
/// Runs commands through the application's pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline is: validate, authorise, take the busy indicator, execute, log, audit,
/// record for undo, publish an event, notify. Every step is skippable through
/// <see cref="CommandExecutionOptions"/> and none of them are a module's job.
/// </para>
/// <para>
/// This is the single place an operator-initiated action goes through, which is what makes
/// the audit trail complete and the failure handling consistent. A module that called a
/// repository directly would work and would be invisible to all of it.
/// </para>
/// </remarks>
public interface ICommandExecutor
{
    /// <summary>Runs a command.</summary>
    /// <remarks>
    /// Does not throw for an ordinary failure. An exception from the command is caught,
    /// logged with its stack and returned as <see cref="CommandOutcome.Failed"/>; the
    /// exception itself is on <see cref="CommandResult.Error"/> for a caller that needs it.
    /// </remarks>
    Task<CommandResult> ExecuteAsync(
        IApplicationCommand command,
        CommandExecutionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Runs a command that produces a value.</summary>
    Task<CommandResult<TValue>> ExecuteAsync<TValue>(
        IApplicationCommand<TValue> command,
        CommandExecutionOptions? options = null,
        CancellationToken cancellationToken = default);
}
