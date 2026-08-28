using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Commands;
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
using Xunit;

namespace WeighBridge.Tests.Commands;

/// <summary>
/// Compiles and runs the command pattern published in Documentation/Development.md.
///
/// The point is the compiler, not the assertions: a documented example that names a member
/// the API does not have is worse than no example, and reviewing prose cannot catch that.
/// If a facet interface, an outcome name or a signature is renamed, this fails to build and
/// the documentation gets fixed with the rename instead of drifting away from it.
/// </summary>
public sealed class DocumentedCommandPatternTests
{
    private sealed record Weighment(string VehicleNumber, decimal GrossWeight, decimal TareWeight)
    {
        public decimal NetWeight => GrossWeight - TareWeight;
    }

    /// <summary>The validator exactly as documented: rules declared in the constructor.</summary>
    private sealed class WeighmentValidator : Validator<Weighment>
    {
        public WeighmentValidator()
        {
            AddRule(
                nameof(Weighment.VehicleNumber),
                x => !string.IsNullOrWhiteSpace(x.VehicleNumber),
                "Vehicle number is required.");

            AddRule(
                nameof(Weighment.NetWeight),
                x => x.NetWeight > 0,
                "Net weight must be positive.");

            AddObjectRule(
                x => x.GrossWeight >= x.TareWeight,
                "Gross weight cannot be less than tare weight.");
        }
    }

    /// <summary>Stands in for the repository the documented command injects.</summary>
    private sealed class FakeRepository
    {
        public List<int> Deleted { get; } = [];

        public Task<int> AddAsync(Weighment entity, CancellationToken cancellationToken) => Task.FromResult(41 + entity.VehicleNumber.Length % 2);

        public Task DeleteAsync(int id, CancellationToken cancellationToken)
        {
            Deleted.Add(id);
            return Task.CompletedTask;
        }

        public Task RestoreAsync(int id, Weighment entity, CancellationToken cancellationToken)
        {
            Restored.Add(id);
            return Task.CompletedTask;
        }

        public List<int> Restored { get; } = [];
    }

    /// <summary>The documented command, facet for facet.</summary>
    private sealed class SaveWeighmentCommand(FakeRepository repository, Weighment entity)
        : IApplicationCommand<int>, IValidatable, IRequiresPermission, IUndoableCommand
    {
        private int _savedId;

        public string Name => "Save weighment";

        public Permission? RequiredPermission => Permissions.WeighmentCreate;

        public string UndoDescription => "Remove the saved weighment";

        public Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
            => new WeighmentValidator().ValidateAsync(entity, cancellationToken);

        public async Task<CommandResult<int>> ExecuteAsync(CommandContext context)
        {
            _savedId = await repository.AddAsync(entity, context.CancellationToken);
            return CommandResult<int>.Success(_savedId, $"Weighment {_savedId} saved.");
        }

        Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
            => ExecuteAsync(context).ContinueWith(t => (CommandResult)t.Result, TaskContinuationOptions.ExecuteSynchronously);

        public Task UndoAsync(CancellationToken cancellationToken = default)
            => repository.DeleteAsync(_savedId, cancellationToken);

        // Reuses the id the original execution captured rather than inserting afresh.
        public Task RedoAsync(CancellationToken cancellationToken = default)
            => repository.RestoreAsync(_savedId, entity, cancellationToken);
    }

    private readonly PermissionService _permissions;
    private readonly UndoManager _undo;
    private readonly CommandExecutor _executor;
    private readonly FakeRepository _repository = new();

    public DocumentedCommandPatternTests()
    {
        var applicationInfo = new TestApplicationInfoService();
        var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        var dispatcher = new TestUiDispatcher();
        var applicationLogger = new ApplicationLogger(factory, applicationInfo);
        var events = new EventBus(dispatcher, factory.CreateLogger<EventBus>());

        _undo = new UndoManager(Options.Create(new UndoOptions { MaxDepth = 20 }), applicationLogger);

        _permissions = new PermissionService(
            applicationInfo,
            applicationLogger,
            new SignedInOperator());

        _executor = new CommandExecutor(
            _permissions,
            new BusyStateService(dispatcher, applicationLogger),
            _undo,
            events,
            new NotificationManager(events, dispatcher, Options.Create(new NotificationOptions()), factory.CreateLogger<NotificationManager>()),
            new AuditLogger(factory, applicationInfo),
            new UIInteractionLogger(factory, applicationInfo));
    }

    private void SignInAs(Role role)
        => _permissions.SetOperator(new OperatorIdentity("tester", "Tester", role));

    [Fact]
    public async Task DocumentedCommand_Succeeds_AndIsUndoable()
    {
        SignInAs(Roles.Administrator);
        var command = new SaveWeighmentCommand(_repository, new Weighment("MH12AB1234", 20000m, 8000m));

        var result = await _executor.ExecuteAsync(command);

        Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
        Assert.True(_undo.CanUndo);

        await _undo.UndoAsync();
        Assert.Single(_repository.Deleted);
    }

    /// <summary>
    /// The three failure outcomes the documentation tells callers to distinguish have to be
    /// genuinely distinguishable, or the advice is wrong.
    /// </summary>
    [Fact]
    public async Task DocumentedOutcomes_AreDistinct()
    {
        SignInAs(Roles.Administrator);

        // Blank vehicle number and a negative net weight: validation, not execution.
        var invalid = await _executor.ExecuteAsync(
            new SaveWeighmentCommand(_repository, new Weighment(" ", 100m, 900m)));

        Assert.Equal(CommandOutcome.ValidationFailed, invalid.Outcome);
        Assert.NotNull(invalid.Validation);
        Assert.NotEmpty(invalid.Validation!.Errors);

        // ReadOnly holds no create permission: denial, not validation failure.
        SignInAs(Roles.ReadOnly);
        var denied = await _executor.ExecuteAsync(
            new SaveWeighmentCommand(_repository, new Weighment("MH12AB1234", 20000m, 8000m)));

        Assert.Equal(CommandOutcome.Denied, denied.Outcome);
        Assert.Null(denied.Validation);

        // A denied command must not have run, and must not be undoable.
        Assert.Empty(_repository.Deleted);
        Assert.False(_undo.CanUndo);
    }
}
