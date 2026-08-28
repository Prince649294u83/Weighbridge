using System.Diagnostics;
using WeighBridge.Core.Busy;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Diagnostics;
using WeighBridge.Core.Events;
using WeighBridge.Core.Events.Catalog;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Notifications;
using WeighBridge.Core.Security;
using WeighBridge.Core.Undo;
using WeighBridge.Core.Validation;

namespace WeighBridge.Services.Commands;

/// <summary>
/// The one path an operator-initiated action takes through the application.
/// </summary>
/// <remarks>
/// <para>
/// Runs the chain the architecture requires: validate, authorise, hold the busy indicator,
/// execute, log, audit, record for undo, publish, notify. A module writes the command; the
/// nine cross-cutting concerns live here, once.
/// </para>
/// <para>
/// Nothing escapes as an exception except a programming error in the pipeline itself. The
/// command's own exceptions are caught, logged with their stack and returned — never
/// swallowed, which is the distinction that matters: a failure is always in the log and
/// always in the returned result.
/// </para>
/// <para>
/// Holds no per-execution state. It is a singleton and two commands can be in flight at
/// once, so everything one execution needs travels on its stack.
/// </para>
/// </remarks>
public sealed class CommandExecutor(
    IPermissionService permissions,
    IBusyStateService busyState,
    IUndoManager undoManager,
    IEventPublisher events,
    INotificationService notifications,
    IAuditLogger audit,
    UIInteractionLogger interactionLogger) : ICommandExecutor
{
    private readonly IPermissionService _permissions = permissions;
    private readonly IBusyStateService _busyState = busyState;
    private readonly IUndoManager _undoManager = undoManager;
    private readonly IEventPublisher _events = events;
    private readonly INotificationService _notifications = notifications;
    private readonly IAuditLogger _audit = audit;
    private readonly UIInteractionLogger _interactionLogger = interactionLogger;

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(
        IApplicationCommand command,
        CommandExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return RunAsync(command, context => command.ExecuteAsync(context), options, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CommandResult<TValue>> ExecuteAsync<TValue>(
        IApplicationCommand<TValue> command,
        CommandExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The typed overload is what runs; its result travels back through the untyped
        // pipeline unchanged, so the cast succeeds on success. From() covers the outcomes
        // the pipeline itself produced — a denial never reached the command.
        var result = await RunAsync(
            command,
            async context => await command.ExecuteAsync(context).ConfigureAwait(false),
            options,
            cancellationToken).ConfigureAwait(false);

        return result as CommandResult<TValue> ?? CommandResult<TValue>.From(result);
    }

    private async Task<CommandResult> RunAsync(
        IApplicationCommand command,
        Func<CommandContext, Task<CommandResult>> execute,
        CommandExecutionOptions? options,
        CancellationToken cancellationToken)
    {
        var effective = options ?? CommandExecutionOptions.Default;
        var correlationId = CorrelationScope.CurrentOrNew;

        // Everything logged, published and audited below shares this identifier, which is
        // what makes one operator action reconstructable from a day's log.
        using var operation = _interactionLogger.BeginOperation(command.Name, correlationId);

        var stopwatch = Stopwatch.StartNew();

        var (result, auditData) = await RunPipelineAsync(
            command,
            execute,
            effective,
            correlationId,
            cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        Complete(command, result, effective, correlationId, stopwatch.Elapsed, auditData);

        return result;
    }

    /// <summary>Validate, authorise, take the indicator, execute.</summary>
    private async Task<(CommandResult Result, IDictionary<string, object?>? AuditData)> RunPipelineAsync(
        IApplicationCommand command,
        Func<CommandContext, Task<CommandResult>> execute,
        CommandExecutionOptions options,
        string correlationId,
        CancellationToken cancellationToken)
    {
        _interactionLogger.CommandInvoked(command.Name);

        // Validation before authorization: an operator who corrects their typo and is only
        // then told they were never allowed to do it at all has been made to work for
        // nothing. The order is deliberate.
        if (options.Validate && command is IValidatable validatable)
        {
            ValidationResult validation;

            try
            {
                validation = await validatable.ValidateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return (CommandResult.Cancelled(), null);
            }

            if (!validation.IsValid)
            {
                _interactionLogger.Debug(
                    "Command {Command} rejected by validation: {Summary}",
                    command.Name,
                    validation.ToSummary());

                return (CommandResult.Invalid(validation), null);
            }
        }

        if (options.CheckPermissions)
        {
            var authorization = _permissions.Authorize(command);

            if (!authorization.IsAuthorized)
            {
                // Audited, not merely logged. A denial is evidence that someone tried.
                _audit.RecordDenied(
                    command.Name,
                    command.GetType().Name,
                    authorization.Reason ?? "Not permitted.");

                return (CommandResult.Denied(authorization), null);
            }
        }

        if (!options.ShowBusyIndicator)
        {
            return await ExecuteGuardedAsync(command, execute, correlationId, null, cancellationToken)
                .ConfigureAwait(false);
        }

        using var busy = await _busyState
            .BeginAsync(options.BusyTitle ?? command.Name, options.IsCancellable, cancellationToken)
            .ConfigureAwait(false);

        // The scope's token already covers the caller's and shutdown; a timeout narrows it.
        if (options.Timeout is not { } timeout)
        {
            return await ExecuteGuardedAsync(command, execute, correlationId, busy, busy.CancellationToken)
                .ConfigureAwait(false);
        }

        using var timed = CancellationTokenSource.CreateLinkedTokenSource(busy.CancellationToken);
        timed.CancelAfter(timeout);

        return await ExecuteGuardedAsync(command, execute, correlationId, busy, timed.Token)
            .ConfigureAwait(false);
    }

    /// <summary>Runs the command's own work and turns anything thrown into a result.</summary>
    private async Task<(CommandResult Result, IDictionary<string, object?>? AuditData)> ExecuteGuardedAsync(
        IApplicationCommand command,
        Func<CommandContext, Task<CommandResult>> execute,
        string correlationId,
        IBusyScope? busy,
        CancellationToken cancellationToken)
    {
        var context = new CommandContext(
            command.Name,
            correlationId,
            _permissions.CurrentOperator,
            cancellationToken,
            busy);

        // Detail the command attached before it threw is still worth auditing.
        var auditData = context.AuditData;

        try
        {
            var result = await execute(context).ConfigureAwait(false);

            return (
                result ?? CommandResult.Failed(
                    new InvalidOperationException($"{command.GetType().Name} returned no result.")),
                auditData);
        }
        catch (OperationCanceledException)
        {
            _interactionLogger.Information("Command {Command} was cancelled", command.Name);

            return (CommandResult.Cancelled(), auditData);
        }
        catch (Exception exception)
        {
            // Logged here with its stack, where the exception object still exists. The
            // returned result carries it too, but a caller may only read the message.
            _interactionLogger.Error(exception, "Command {Command} failed", command.Name);

            return (CommandResult.Failed(exception), auditData);
        }
    }

    /// <summary>Log, audit, record for undo, publish, notify.</summary>
    private void Complete(
        IApplicationCommand command,
        CommandResult result,
        CommandExecutionOptions options,
        string correlationId,
        TimeSpan duration,
        IDictionary<string, object?>? auditData)
    {
        _interactionLogger.Debug(
            "Command {Command} finished as {Outcome} in {Elapsed} ms",
            command.Name,
            result.Outcome,
            (long)duration.TotalMilliseconds);

        if (options.Audit && result.IsSuccess)
        {
            _audit.Record(
                command.Name,
                command.GetType().Name,
                details: DescribeAuditData(auditData));
        }
        else if (options.Audit && result.Outcome == CommandOutcome.Failed)
        {
            // Failures are exactly when the trail matters: work may have moved halfway and
            // nobody was told. The reason is recorded, never a stack dump — that belongs to
            // the interaction log above.
            _audit.RecordFailed(
                command.Name,
                command.GetType().Name,
                result.Error?.Message ?? result.Message ?? "The operation failed.");
        }

        if (options.RecordUndo && result.IsSuccess && command is IUndoableCommand undoable)
        {
            _undoManager.Push(undoable, correlationId);
        }

        if (options.PublishEvent)
        {
            // Fire and forget: a subscriber must not be able to fail the operation that
            // announced it, and the bus already isolates and logs handler failures.
            _events.Publish(new CommandExecutedEvent(
                command.Name,
                result.Outcome,
                duration,
                correlationId,
                result.Message,
                nameof(CommandExecutor)));
        }

        Notify(command, result, options);
    }

    private static string? DescribeAuditData(IDictionary<string, object?>? auditData)
        => auditData is null || auditData.Count == 0
            ? null
            : string.Join(", ", auditData.Select(pair => $"{pair.Key}={pair.Value}"));

    /// <summary>Tells the operator, in the terms the outcome deserves.</summary>
    private void Notify(IApplicationCommand command, CommandResult result, CommandExecutionOptions options)
    {
        switch (result.Outcome)
        {
            case CommandOutcome.Succeeded when options.NotifyOnSuccess:
                _notifications.NotifySuccess(command.Name, result.Message ?? "Done.");
                break;

            case CommandOutcome.ValidationFailed when options.NotifyOnFailure:
                // A warning, not an error. The operator mistyped something; nothing broke.
                _notifications.NotifyWarning(
                    command.Name,
                    result.Message ?? "The information supplied is not valid.",
                    details: result.Validation?.ToSummary());
                break;

            case CommandOutcome.Denied when options.NotifyOnFailure:
                _notifications.NotifyWarning(command.Name, result.Message ?? "Not permitted.");
                break;

            case CommandOutcome.Failed when options.NotifyOnFailure:
                _notifications.NotifyError(
                    command.Name,
                    result.Message ?? "The operation failed.",
                    details: result.Error?.ToString());
                break;

            // Cancellation is the operator's own doing. Announcing what they just asked for
            // is noise.
            case CommandOutcome.Cancelled:
            default:
                break;
        }
    }
}
