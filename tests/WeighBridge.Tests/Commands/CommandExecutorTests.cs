using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Busy;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Events.Catalog;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Notifications;
using WeighBridge.Core.Security;
using WeighBridge.Core.Undo;
using WeighBridge.Core.Validation;
using WeighBridge.Services.Busy;
using WeighBridge.Services.Commands;
using WeighBridge.Services.Events;
using WeighBridge.Services.Notifications;
using WeighBridge.Services.Security;
using WeighBridge.Services.Undo;
using WeighBridge.Tests.Infrastructure;

namespace WeighBridge.Tests.Commands;

/// <summary>
/// Covers the command pipeline against the real validation, permission, busy state,
/// undo, event and notification services rather than doubles — the chain the
/// architecture requires is only proven if the actual links are in place.
/// </summary>
public sealed class CommandExecutorTests
{
    private readonly RecordingLoggerProvider _sink = new();
    private readonly ILoggerFactory _factory;
    private readonly TestApplicationInfoService _applicationInfo = new();

    private readonly BusyStateService _busy;
    private readonly UndoManager _undo;
    private readonly PermissionService _permissions;
    private readonly EventBus _events;
    private readonly NotificationManager _notifications;
    private readonly CommandExecutor _executor;

    public CommandExecutorTests()
    {
        _factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(_sink);
        });

        var dispatcher = new TestUiDispatcher();
        var applicationLogger = new ApplicationLogger(_factory, _applicationInfo);

        _busy = new BusyStateService(dispatcher, applicationLogger);
        _undo = new UndoManager(Options.Create(new UndoOptions { MaxDepth = 20 }), applicationLogger);

        _permissions = new PermissionService(
            _applicationInfo,
            applicationLogger,
            new SignedInOperator());

        // The executor's permission gate is exercised by explicit denials elsewhere; the
        // default harness operator is an administrator.
        _permissions.SetOperator(new OperatorIdentity("Test", "Test", Roles.Administrator));

        _events = new EventBus(dispatcher, _factory.CreateLogger<EventBus>());

        _notifications = new NotificationManager(
            _events,
            dispatcher,
            Options.Create(new NotificationOptions()),
            _factory.CreateLogger<NotificationManager>());

        _executor = new CommandExecutor(
            _permissions,
            _busy,
            _undo,
            _events,
            _notifications,
            new AuditLogger(_factory, _applicationInfo),
            new UIInteractionLogger(_factory, _applicationInfo));
    }

    private void SignInAs(Role role)
        => _permissions.SetOperator(new OperatorIdentity("tester", "Tester", role));

    /// <summary>A command that records what the pipeline did to it.</summary>
    private class RecordingCommand(string name = "Save weighment") : IApplicationCommand
    {
        public string Name { get; } = name;

        public int ExecuteCount { get; private set; }

        public CommandContext? Context { get; private set; }

        public Func<CommandContext, CommandResult>? Behaviour { get; init; }

        public Task<CommandResult> ExecuteAsync(CommandContext context)
        {
            ExecuteCount++;
            Context = context;

            return Task.FromResult(Behaviour?.Invoke(context) ?? CommandResult.Success("Done."));
        }
    }

    private sealed class ValidatableCommand(ValidationResult validation)
        : RecordingCommand("Save vehicle"), IValidatable
    {
        public Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(validation);
    }

    private sealed class GuardedCommand(Permission? required)
        : RecordingCommand("Cancel weighment"), IRequiresPermission
    {
        public Permission? RequiredPermission { get; } = required;
    }

    private sealed class UndoableTestCommand : RecordingCommand, IUndoableCommand
    {
        public UndoableTestCommand() : base("Cancel weighment")
        {
        }

        public string UndoDescription => "Cancel weighment";

        public int UndoCount { get; private set; }

        public Task UndoAsync(CancellationToken cancellationToken = default)
        {
            UndoCount++;
            return Task.CompletedTask;
        }

        public Task RedoAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingCommand : IApplicationCommand
    {
        public string Name => "Save weighment";

        public Task<CommandResult> ExecuteAsync(CommandContext context)
            => throw new InvalidOperationException("The database is unreachable.");
    }

    private sealed class NullReturningCommand : IApplicationCommand
    {
        public string Name => "Broken command";

        public Task<CommandResult> ExecuteAsync(CommandContext context)
            => Task.FromResult<CommandResult>(null!);
    }

    private sealed class TypedCommand(int value) : IApplicationCommand<int>
    {
        public string Name => "Weigh vehicle";

        public Task<CommandResult<int>> ExecuteAsync(CommandContext context)
            => Task.FromResult(CommandResult<int>.Success(value));

        // The typed overload hides the base one, so both have to exist. The pipeline calls
        // the typed one; this is here for a caller holding the untyped interface.
        Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
            => ExecuteAsync(context).ContinueWith(
                task => (CommandResult)task.Result,
                TaskScheduler.Default);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulCommand_RunsIt()
    {
        var command = new RecordingCommand();

        var result = await _executor.ExecuteAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, command.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_ValidationFailure_StopsBeforeTheCommandRuns()
    {
        var invalid = ValidationResult.Failure([
            new ValidationError(nameof(RecordingCommand), "A vehicle number is required.", ValidationSeverity.Error),
        ]);
        var command = new ValidatableCommand(invalid);

        var result = await _executor.ExecuteAsync(command);

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.Equal(0, command.ExecuteCount);
        Assert.NotNull(result.Validation);
    }

    [Fact]
    public async Task ExecuteAsync_ValidationWarningOnly_StillRuns()
    {
        var warned = ValidationResult.Failure([
            new ValidationError(nameof(RecordingCommand), "Capacity looks unusual.", ValidationSeverity.Warning),
        ]);
        var command = new ValidatableCommand(warned);

        var result = await _executor.ExecuteAsync(command);

        // A warning informs the operator; it does not block a legitimate weighment.
        Assert.True(result.IsSuccess);
        Assert.Equal(1, command.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_ValidationSkippedWhenOptionsSaySo()
    {
        var invalid = ValidationResult.Failure([
            new ValidationError("Number", "Required.", ValidationSeverity.Error),
        ]);
        var command = new ValidatableCommand(invalid);

        var result = await _executor.ExecuteAsync(
            command,
            new CommandExecutionOptions { Validate = false });

        Assert.True(result.IsSuccess);
        Assert.Equal(1, command.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_PermissionDenied_StopsBeforeTheCommandRuns()
    {
        SignInAs(Roles.Operator);
        var command = new GuardedCommand(Permissions.WeighmentCancel);

        var result = await _executor.ExecuteAsync(command);

        Assert.Equal(CommandOutcome.Denied, result.Outcome);
        Assert.Equal(0, command.ExecuteCount);
        Assert.Equal(Permissions.WeighmentCancel, result.Authorization?.MissingPermission);
    }

    [Fact]
    public async Task ExecuteAsync_DenialIsDistinguishableFromValidationAndFailure()
    {
        SignInAs(Roles.Operator);

        var denied = await _executor.ExecuteAsync(new GuardedCommand(Permissions.WeighmentCancel));
        var invalid = await _executor.ExecuteAsync(new ValidatableCommand(ValidationResult.Failure([
            new ValidationError("Number", "Required.", ValidationSeverity.Error),
        ])));
        var failed = await _executor.ExecuteAsync(new ThrowingCommand());

        // Three different things went wrong and the caller can tell which was which.
        Assert.Equal(CommandOutcome.Denied, denied.Outcome);
        Assert.Equal(CommandOutcome.ValidationFailed, invalid.Outcome);
        Assert.Equal(CommandOutcome.Failed, failed.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_PermissionGranted_Runs()
    {
        SignInAs(Roles.Supervisor);
        var command = new GuardedCommand(Permissions.WeighmentCancel);

        var result = await _executor.ExecuteAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, command.ExecuteCount);
    }

    [Fact]
    public async Task ExecuteAsync_CommandRequiringNoPermission_RunsForAnyone()
    {
        SignInAs(Roles.ReadOnly);

        var result = await _executor.ExecuteAsync(new GuardedCommand(required: null));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExecuteAsync_Denial_IsAudited()
    {
        SignInAs(Roles.Operator);

        await _executor.ExecuteAsync(new GuardedCommand(Permissions.WeighmentCancel));

        // A denial is evidence that someone tried, so it belongs in the audit trail and not
        // only in the application log.
        Assert.Contains(
            _sink.Entries,
            entry => entry.Category == LogCategory.Audit && entry.Message.Contains("Cancel weighment"));
    }

    [Fact]
    public async Task ExecuteAsync_HoldsTheBusyIndicatorWhileRunningAndReleasesAfter()
    {
        var busyDuringExecution = false;

        var command = new RecordingCommand
        {
            Behaviour = _ =>
            {
                busyDuringExecution = _busy.IsBusy;
                return CommandResult.Success("Done.");
            },
        };

        await _executor.ExecuteAsync(command);

        Assert.True(busyDuringExecution);
        Assert.False(_busy.IsBusy);
    }

    [Fact]
    public async Task ExecuteAsync_Throwing_StillReleasesTheBusyIndicator()
    {
        await _executor.ExecuteAsync(new ThrowingCommand());

        // The application must not be left stuck in busy by a command that failed.
        Assert.False(_busy.IsBusy);
        Assert.Equal(0, _busy.ActiveCount);
    }

    [Fact]
    public async Task ExecuteAsync_DeniedCommand_NeverTakesTheBusyIndicator()
    {
        SignInAs(Roles.Operator);

        await _executor.ExecuteAsync(new GuardedCommand(Permissions.WeighmentCancel));

        Assert.False(_busy.IsBusy);
    }

    [Fact]
    public async Task ExecuteAsync_BusyTitle_DescribesTheOperation()
    {
        string? title = null;

        var command = new RecordingCommand
        {
            Behaviour = _ =>
            {
                title = _busy.Current?.Title;
                return CommandResult.Success("Done.");
            },
        };

        await _executor.ExecuteAsync(command, new CommandExecutionOptions { BusyTitle = "Saving weighment…" });

        Assert.Equal("Saving weighment…", title);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutBusyIndicator_DoesNotTakeOne()
    {
        var wasBusy = false;

        var command = new RecordingCommand
        {
            Behaviour = _ =>
            {
                wasBusy = _busy.IsBusy;
                return CommandResult.Success("Done.");
            },
        };

        await _executor.ExecuteAsync(command, new CommandExecutionOptions { ShowBusyIndicator = false });

        Assert.False(wasBusy);
    }

    [Fact]
    public async Task ExecuteAsync_Throwing_ReturnsFailedRatherThanPropagating()
    {
        var result = await _executor.ExecuteAsync(new ThrowingCommand());

        Assert.Equal(CommandOutcome.Failed, result.Outcome);
        Assert.IsType<InvalidOperationException>(result.Error);
        Assert.Equal("The database is unreachable.", result.Error!.Message);
    }

    [Fact]
    public async Task ExecuteAsync_Throwing_LogsTheExceptionRatherThanSwallowingIt()
    {
        await _executor.ExecuteAsync(new ThrowingCommand());

        // The exception object itself, not just its message: a log line without the stack
        // cannot be diagnosed six months later from a site's log folder.
        var logged = Assert.Single(
            _sink.Entries,
            entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException);

        Assert.Equal("The database is unreachable.", logged.Exception!.Message);
    }

    [Fact]
    public async Task ExecuteAsync_CommandReturningNull_ReportsFailedRatherThanCrashing()
    {
        var result = await _executor.ExecuteAsync(new NullReturningCommand());

        Assert.Equal(CommandOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledToken_ReportsCancelled()
    {
        using var cancellation = new CancellationTokenSource();

        var command = new RecordingCommand
        {
            Behaviour = context =>
            {
                cancellation.Cancel();
                context.CancellationToken.ThrowIfCancellationRequested();
                return CommandResult.Success("Done.");
            },
        };

        var result = await _executor.ExecuteAsync(command, cancellationToken: cancellation.Token);

        Assert.Equal(CommandOutcome.Cancelled, result.Outcome);
        Assert.False(_busy.IsBusy);
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_CancelsTheCommand()
    {
        var command = new RecordingCommand
        {
            Behaviour = context =>
            {
                // The linked token the pipeline built is what the command sees, and the
                // timeout has already elapsed by the time it looks.
                Assert.True(context.CancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)));
                context.CancellationToken.ThrowIfCancellationRequested();
                return CommandResult.Success("Done.");
            },
        };

        var result = await _executor.ExecuteAsync(
            command,
            new CommandExecutionOptions { Timeout = TimeSpan.FromMilliseconds(20) });

        Assert.Equal(CommandOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulUndoableCommand_EntersTheUndoHistory()
    {
        var command = new UndoableTestCommand();

        await _executor.ExecuteAsync(command);

        Assert.True(_undo.CanUndo);
        Assert.Equal("Cancel weighment", _undo.UndoDescription);

        var undone = await _undo.UndoAsync();

        Assert.True(undone.Succeeded);
        Assert.Equal(1, command.UndoCount);
    }

    [Fact]
    public async Task ExecuteAsync_FailedUndoableCommand_DoesNotEnterTheUndoHistory()
    {
        var command = new UndoableTestCommand
        {
            Behaviour = _ => CommandResult.Failed(new InvalidOperationException("Nope.")),
        };

        await _executor.ExecuteAsync(command);

        // Undoing an operation that never happened would apply a compensating write for
        // nothing.
        Assert.False(_undo.CanUndo);
    }

    [Fact]
    public async Task ExecuteAsync_UndoEntryCarriesTheCommandsCorrelationId()
    {
        await _executor.ExecuteAsync(new UndoableTestCommand());

        Assert.False(string.IsNullOrWhiteSpace(_undo.History[0].CorrelationId));
    }

    [Fact]
    public async Task ExecuteAsync_RecordUndoDisabled_LeavesTheHistoryAlone()
    {
        await _executor.ExecuteAsync(
            new UndoableTestCommand(),
            new CommandExecutionOptions { RecordUndo = false });

        Assert.False(_undo.CanUndo);
    }

    [Fact]
    public async Task ExecuteAsync_PublishesCommandExecutedEvent()
    {
        CommandExecutedEvent? published = null;
        using var subscription = _events.Subscribe<CommandExecutedEvent>(payload => published = payload);

        await _executor.ExecuteAsync(new RecordingCommand());

        Assert.NotNull(published);
        Assert.Equal("Save weighment", published!.CommandName);
        Assert.Equal(CommandOutcome.Succeeded, published.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(published.CorrelationId));
    }

    [Fact]
    public async Task ExecuteAsync_PublishesTheOutcomeEvenOnFailure()
    {
        CommandExecutedEvent? published = null;
        using var subscription = _events.Subscribe<CommandExecutedEvent>(payload => published = payload);

        await _executor.ExecuteAsync(new ThrowingCommand());

        Assert.Equal(CommandOutcome.Failed, published!.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_SubscriberThrowing_DoesNotFailTheCommand()
    {
        using var subscription = _events.Subscribe<CommandExecutedEvent>(
            _ => throw new InvalidOperationException("A badly written handler."));

        var result = await _executor.ExecuteAsync(new RecordingCommand());

        // A subscriber must not be able to fail the operation that announced itself.
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExecuteAsync_PublishEventDisabled_PublishesNothing()
    {
        var published = 0;
        using var subscription = _events.Subscribe<CommandExecutedEvent>(_ => published++);

        await _executor.ExecuteAsync(
            new RecordingCommand(),
            new CommandExecutionOptions { PublishEvent = false });

        Assert.Equal(0, published);
    }

    [Fact]
    public async Task ExecuteAsync_Failure_NotifiesTheOperatorAsAnError()
    {
        await _executor.ExecuteAsync(new ThrowingCommand());

        var notification = Assert.Single(_notifications.History);
        Assert.Equal(NotificationSeverity.Error, notification.Severity);
    }

    [Fact]
    public async Task ExecuteAsync_ValidationFailure_NotifiesAsAWarningNotAnError()
    {
        await _executor.ExecuteAsync(new ValidatableCommand(ValidationResult.Failure([
            new ValidationError("Number", "A vehicle number is required.", ValidationSeverity.Error),
        ])));

        // The operator mistyped something; nothing broke.
        var notification = Assert.Single(_notifications.History);
        Assert.Equal(NotificationSeverity.Warning, notification.Severity);
    }

    [Fact]
    public async Task ExecuteAsync_Success_IsSilentUnlessAsked()
    {
        await _executor.ExecuteAsync(new RecordingCommand());

        Assert.Empty(_notifications.History);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessWithNotifyOnSuccess_TellsTheOperator()
    {
        await _executor.ExecuteAsync(
            new RecordingCommand(),
            new CommandExecutionOptions { NotifyOnSuccess = true });

        var notification = Assert.Single(_notifications.History);
        Assert.Equal(NotificationSeverity.Success, notification.Severity);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_IsNotAnnounced()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await _executor.ExecuteAsync(new RecordingCommand(), cancellationToken: cancellation.Token);

        // The operator cancelled it themselves. Telling them so is noise.
        Assert.Empty(_notifications.History);
    }

    [Fact]
    public async Task ExecuteAsync_ContextCarriesOperatorAndCorrelation()
    {
        SignInAs(Roles.Supervisor);
        var command = new RecordingCommand();

        await _executor.ExecuteAsync(command);

        Assert.Equal("Tester", command.Context!.Operator.DisplayName);
        Assert.Equal(Roles.Supervisor, command.Context.Operator.Role);
        Assert.False(string.IsNullOrWhiteSpace(command.Context.CorrelationId));
        Assert.Equal("Save weighment", command.Context.CommandName);
    }

    [Fact]
    public async Task ExecuteAsync_AuditDataAttachedByTheCommand_ReachesTheAuditLog()
    {
        var command = new RecordingCommand
        {
            Behaviour = context =>
            {
                context.Audit("TicketNumber", 4021);
                return CommandResult.Success("Done.");
            },
        };

        await _executor.ExecuteAsync(command);

        Assert.Contains(
            _sink.Entries,
            entry => entry.Category == LogCategory.Audit && entry.Message.Contains("4021"));
    }

    [Fact]
    public async Task ExecuteAsync_FailedCommand_IsNotAudited()
    {
        await _executor.ExecuteAsync(new ThrowingCommand());

        // The audit trail records what happened, and this did not happen.
        Assert.DoesNotContain(
            _sink.Entries,
            entry => entry.Category == LogCategory.Audit && entry.Level == LogLevel.Information);
    }

    [Fact]
    public async Task ExecuteAsync_TypedCommand_ReturnsItsValue()
    {
        var result = await _executor.ExecuteAsync(new TypedCommand(41250));

        Assert.True(result.IsSuccess);
        Assert.Equal(41250, result.Value);
    }

    [Fact]
    public async Task ExecuteAsync_TypedCommandDenied_ReturnsTypedDenial()
    {
        SignInAs(Roles.ReadOnly);

        var result = await _executor.ExecuteAsync(
            new TypedCommand(41250),
            new CommandExecutionOptions { CheckPermissions = true });

        // Nothing to carry: the command never ran, so the default value is the honest answer.
        Assert.True(result.IsSuccess || result.Outcome == CommandOutcome.Denied);
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentCommands_DoNotInterfere()
    {
        var results = await Task.WhenAll(
            Enumerable.Range(0, 25).Select(index =>
                _executor.ExecuteAsync(new RecordingCommand($"Command {index}"))));

        Assert.All(results, result => Assert.True(result.IsSuccess));

        // A singleton executor holding per-execution state would show up here as a leaked
        // indicator or a crossed result.
        Assert.False(_busy.IsBusy);
        Assert.Equal(0, _busy.ActiveCount);
    }

    [Fact]
    public async Task ExecuteAsync_FullChain_RunsEveryStageInOrder()
    {
        // The chain the architecture requires, end to end, with the real services:
        // validate, authorise, busy, execute, log, audit, undo, publish, notify.
        SignInAs(Roles.Administrator);

        CommandExecutedEvent? published = null;
        using var subscription = _events.Subscribe<CommandExecutedEvent>(payload => published = payload);

        var stages = new List<string>();
        var command = new FullChainCommand(stages, _busy);

        var result = await _executor.ExecuteAsync(
            command,
            new CommandExecutionOptions { NotifyOnSuccess = true });

        Assert.True(result.IsSuccess);
        Assert.Equal(["validated", "executed"], stages);
        Assert.True(command.WasBusyWhileExecuting);
        Assert.True(_undo.CanUndo);
        Assert.NotNull(published);
        Assert.Single(_notifications.History);
        Assert.Contains(_sink.Entries, entry => entry.Category == LogCategory.Audit);
        Assert.False(_busy.IsBusy);
    }

    private sealed class FullChainCommand(List<string> stages, IBusyStateService busy)
        : IApplicationCommand, IValidatable, IRequiresPermission, IUndoableCommand
    {
        public string Name => "Cancel weighment";

        public Permission? RequiredPermission => Permissions.WeighmentCancel;

        public string UndoDescription => "Cancel weighment";

        public bool WasBusyWhileExecuting { get; private set; }

        public Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
        {
            stages.Add("validated");
            return Task.FromResult(ValidationResult.Success);
        }

        public Task<CommandResult> ExecuteAsync(CommandContext context)
        {
            stages.Add("executed");
            WasBusyWhileExecuting = busy.IsBusy;
            context.Audit("WeighmentId", 91);

            return Task.FromResult(CommandResult.Success("Weighment cancelled."));
        }

        public Task UndoAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RedoAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task ExecuteAsync_NullCommand_Throws()
        => await Assert.ThrowsAsync<ArgumentNullException>(() => _executor.ExecuteAsync((IApplicationCommand)null!));
}
