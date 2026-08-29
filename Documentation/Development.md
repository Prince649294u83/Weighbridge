# Development guide

## Prerequisites

- Windows 10 1809 or later
- .NET 8 SDK
- An IDE with WPF support (Visual Studio 2022 17.8+, JetBrains Rider) â€” optional, the CLI is
  sufficient

`global.json` pins the SDK to 8.0.100 with `rollForward: latestMajor`, so a newer SDK builds
the solution while the target framework stays .NET 8 LTS.

## Everyday commands

```bash
dotnet build WeighBridge.sln          # expect 0 warnings, 0 errors
dotnet run --project src/WeighBridge.App
dotnet test tests/WeighBridge.Tests
```

The build is expected to stay warning-clean. A new warning is a defect.

## Test suite

`WeighBridge.Tests` targets `net8.0` **without** WPF, so it runs headless on CI. The
consequence is that it cannot reference types from `WeighBridge.App` â€” anything worth
testing belongs in a library project, which is a useful forcing function for the layering.

Current coverage: the weighment domain and its state machine, real-SQLite persistence
round-trips, the four weighment commands through the real pipeline, the validation framework,
command pipeline, undo, busy state, events, notifications, health, background tasks,
permissions, settings persistence, configuration provisioning and recovery, navigation history,
and the MVVM command primitives.

Anything living in `WeighBridge.App` â€” the Views, `VehicleEntryViewModel`, `WeighmentSummary`,
`StatusBadge`, `EmptyState`, the dialogs â€” is unreachable from here. Those are covered by the
runtime scripts instead, which is why the scripts are not optional.

`TempDataRoot` gives a test its own throwaway data root by constructing
`ApplicationPaths` with an explicit path. Use it for anything that touches disk; never let
a test write to the real `%LOCALAPPDATA%` folder.

## Runtime verification

Compilation is not verification, and neither is a passing unit suite for anything on screen.

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/runtime-smoke.ps1        # the shell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vehicle-entry-smoke.ps1  # the workflow
```

Both must exit 0. The second **deletes the database** and writes test weighments â€” never run it
on a machine holding real data. Both save screenshots to `%TEMP%\weighbridge-*.png`; look at
them, because an assertion only checks what somebody thought to assert. Two UI defects survived
21 green assertions in this codebase and were found by reading the picture.

`scripts/uia-dump.ps1` dumps the automation tree when an element cannot be found.

## Adding a module

Six of the seven module pages are still placeholders; `VehicleEntryView` is the worked example
of a real one. To make another real:

1. **ViewModel** â€” derive from `ViewModelBase` in `App/ViewModels/`. Take dependencies
   through the constructor. Override `OnNavigatedToAsync` to load data.
2. **View** â€” a `UserControl` in `App/Views/`, named so the convention resolves it:
   `FooViewModel` â†’ `FooView`. Bind only; no logic in code-behind.
3. **Register** the ViewModel as transient in `Bootstrapper.RegisterViewModels`.
4. **Services** â€” put business logic in `WeighBridge.Services` behind an interface, and data
   access behind `IRepository<T>`. Not in the ViewModel.
5. **Navigate** with `INavigationService.NavigateToAsync<FooViewModel>()`.
6. **Persist** by adding an `IEntityTypeConfiguration<T>` and a migration â€” see Database below.
7. **Guard** state changes with a `Permissions.*` constant, never a string literal.

Style the view from existing tokens and controls. If something is genuinely missing from the
design system, add it to the design system â€” not to the view.

Every input needs `AutomationProperties.Name`, and every input the runtime scripts drive needs
`UpdateSourceTrigger=PropertyChanged` so `ValuePattern.SetValue` reaches the ViewModel. If you
add a **templated** custom control that displays text, it needs its own `AutomationPeer` â€”
`TextBlock`s inside a `ControlTemplate` are outside the UIA control view, so a screen reader
never announces them (`StatusBadgeAutomationPeer` and `EmptyStateAutomationPeer` are the
pattern). And a `Style` targeting such a control must say
`BasedOn="{StaticResource {x:Type controls:Foo}}"`, or it replaces the default style, takes the
`ControlTemplate` with it, and the control renders nothing at all.

## Writing an operation

Anything that changes state goes through `ICommandExecutor` rather than running inline in a
ViewModel. The executor is what applies validation, permissions, busy state, logging, undo
and notification, so a command written this way gets all of it for free.

`src/WeighBridge.Services/Weighments/WeighmentCommands.cs` holds the four working examples.
Abridged from `RecordSecondWeightCommand`:

```csharp
public sealed class RecordSecondWeightCommand(
    IWeighmentService weighments,
    long weighmentId,
    decimal kilograms,
    WeightSource source) : IApplicationCommand<Weighment>, IValidatable, IRequiresPermission
{
    public string Name => "Record second weight";

    public Permission? RequiredPermission => Permissions.WeighmentCreate;

    public async Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        // ... status and existence checks first ...

        // Reject what the operator can fix, and name the figure that is wrong. The aggregate
        // would refuse this too, but by throwing â€” which tells them nothing actionable.
        return gross > tare
            ? ValidationResult.Success
            : ValidationResult.Failure(
                nameof(kilograms),
                $"The gross weight ({gross:0.##} kg) must be greater than the tare weight ({tare:0.##} kg). "
                + "Check whether the vehicle arrived loaded or empty.");
    }

    public async Task<CommandResult<Weighment>> ExecuteAsync(CommandContext context)
    {
        context.ReportStatus("Recording weightâ€¦");

        var weighment = await _weighments
            .RecordSecondWeightAsync(weighmentId, kilograms, source, context.CancellationToken)
            .ConfigureAwait(false);

        // Audit entries are key/value, so the log line is still greppable years later.
        context.Audit("SlipNumber", weighment.SlipNumber)
            .Audit("Kilograms", kilograms)
            .Audit("WeightSource", source.ToString())
            .Audit("NetKilograms", weighment.NetWeightKg);

        return CommandResult<Weighment>.Success(
            weighment,
            $"Weighment {weighment.SlipNumber} completed. Net {weighment.NetWeightKg:0.##} kg.");
    }
}
```

Note what it does *not* do: it holds no `DbContext`, writes no SQL, and never touches a
repository. It calls a service. Note also that there is no `CompleteWeighmentCommand` â€” this
command *is* completion. Add commands per operation an operator performs, not per property
setter. None of the four implements `IUndoableCommand`: a recorded weighing is an audit
record, and the reversal an operator is allowed is `CancelWeighmentCommand`, which keeps the
row and stores a reason. Undo history exists for operations that can be silently reversed;
this is not one.

Call it, then branch on the outcome rather than on a boolean:

```csharp
var result = await _executor.ExecuteAsync(command);

switch (result.Outcome)
{
    case CommandOutcome.Succeeded:        break;
    case CommandOutcome.ValidationFailed: ShowErrors(result.Validation!); break;
    case CommandOutcome.Denied:           /* not permitted, not a mistake */ break;
    case CommandOutcome.Cancelled:        break;
    case CommandOutcome.Failed:           /* already logged; result.Error has it */ break;
}
```

`ValidationFailed`, `Denied` and `Failed` mean three different things to an operator.
Collapsing them into one error message is the mistake this shape exists to prevent.

Each facet is optional. Drop `IValidatable` and validation is skipped; drop
`IRequiresPermission` and the authorization stage is skipped. Add only what the operation
actually needs.

## Busy state, validation, permissions, dialogs, undo

Used directly, outside a command:

```csharp
// Busy â€” the scope releases on success, failure, cancellation and exception alike.
using var busy = await _busy.BeginAsync("Printing slip", isCancellable: true);
busy.Report(0.5, "Page 1 of 2");

// Permissions â€” always a constant, never a string literal at the call site.
if (!_permissions.HasPermission(Permissions.WeighmentCreate)) { return; }

// Dialogs â€” awaited, never MessageBox, never .Result.
if (await _dialogs.ShowConfirmationAsync("Cancel this weighment?", "It cannot be restored."))
{
    await _executor.ExecuteAsync(cancelCommand);
}

// Undo â€” history is driven by the executor; this is only for the shell's own affordances.
if (_undo.CanUndo) { await _undo.UndoAsync(); }
```

A validator is a class, not a fluent chain â€” rules are declared once in the constructor so
the rule set is fixed and inspectable through `Rules`. The real one is
`src/WeighBridge.Services/Weighments/NewWeighmentValidator.cs`:

```csharp
public sealed class NewWeighmentValidator : Validator<NewWeighment>
{
    public NewWeighmentValidator()
    {
        AddRule(
            nameof(NewWeighment.VehicleNumber),
            request => !string.IsNullOrWhiteSpace(request.VehicleNumber),
            "Enter the vehicle number.");

        // The length rule tests the NORMALISED value, because that is what reaches the
        // column. Testing what was typed accepts a registration that then fails at the
        // database with a message no operator can act on.
        AddRule(
            nameof(NewWeighment.VehicleNumber),
            request => string.IsNullOrWhiteSpace(request.VehicleNumber)
                || Weighment.NormaliseVehicleNumber(request.VehicleNumber).Length
                    <= Weighment.VehicleNumberMaxLength,
            $"A vehicle number cannot be longer than {Weighment.VehicleNumberMaxLength} characters.");
    }
}
```

The line to hold: a validator carries what the **operator can fix**. Invariants the record
must never violate belong in the aggregate, where they throw. A blank vehicle number is a
mistake to point at a field about; a second weight with no first is a record that must not
exist. Only the first kind is validation.

Two rules worth stating outright, because both have bitten this codebase:

- Never `.Result`, `.Wait()` or `GetAwaiter().GetResult()` on any of these. They are
  UI-affine and will deadlock the dispatcher.
- Never leave a busy scope undisposed on a failure path. `using` handles it; a manual
  `Dispose()` after an `await` that can throw does not.

## Database

SQLite, EF Core, code-first. Four tables so far: `Weighments`, `Materials`/`Parties`/`Vehicles`/`VehicleTypes`, `WeighmentImages`, `Users` and `AuditEntries`, across five migrations
`20260815104634_AddVehicleEntry`.

```bash
dotnet ef migrations add <Name> --project src/WeighBridge.Infrastructure --startup-project src/WeighBridge.App
```

`IDesignTimeDbContextFactory` supplies the design-time connection, so the CLI works without
launching the WPF application. Commit all three generated files â€” the migration, its
`.Designer.cs`, and the regenerated `WeighBridgeDbContextModelSnapshot.cs`; leaving the
snapshot out makes the *next* migration diff against the wrong model.

**Never edit a migration that has already been applied.** Add a new one. An edited migration
leaves every existing database on a schema no migration describes, and `Migrate()` will not
repair it.

A mapping is added by dropping an `IEntityTypeConfiguration<T>` into
`Infrastructure/Persistence/Configurations/` — `OnModelCreating` discovers it, so the context
itself never changes. `WeighmentConfiguration` is the reference, including the
`ValueConverter<decimal, long>` converters that store weights as whole grams (`KilogramsToGrams`)
and charges as whole paise (`RupeesToPaise`) — see [Architecture.md](Architecture.md#persistence)
for why integer storage is used on SQLite.

Migration `20260829144243_AddF1F2WorkflowFields` adds `ChargesPaise`, `SecondChargesPaise`,
`NumberOfBags`, `BagWeightGrams`, `GatePassNumber`, `CustomField1`..`4`, and `Version` (`Guid`
concurrency token), ensuring non-empty GUID versions are seeded for all historical records.

To get back to a first launch, close the application and delete the file with its siblings:

```bash
rm "$LOCALAPPDATA/WeighBridge Modern/Data/weighbridge.db"*
```

Startup shows and *then* starts the migration, so the window is never held off screen by a
slow database. `DatabaseInitializer.InitializeAsync` memoizes its task and the first module to
open awaits it â€” that await is what stops the module querying a table the migration has not
created yet, and the memoization is what stops the two callers migrating concurrently.

## Conventions

- Interfaces for all services; constructor injection everywhere.
- No service locator, no static mutable state.
- `async`/`await` for anything that touches disk, database or hardware. Never block the UI
  thread.
- Nullable reference types are enabled; do not silence a warning with `!` unless the
  invariant is genuinely local and worth a comment.
- Comment the *why*, not the *what*. Several comments in this codebase record a WPF
  behaviour that cost real debugging time â€” those are worth keeping.

## Diagnostics

The log file is the first place to look:

```
%LOCALAPPDATA%\WeighBridge Modern\Logs\weighbridge-{date}.log
```

A healthy startup logs, in order: the version banner, data root, configuration file and its
provisioning outcome, log file path, *Dependency injection container built and validated*,
preferences loaded, theme applied, *Application shell created*, monitoring started, the
first navigation, *Application shell displayed; startup complete*, then database readiness
and the subsystem status transitions.

A clean shutdown ends with `==== Shutdown complete ====`. If that line is missing, the
shutdown path threw before finishing, and window placement or preferences may not have been
saved.

Two symptoms worth recognising:

- A `.tmp` file left in the data root means an atomic write failed part-way.
- An error arriving from the finalizer thread, minutes after the fact, is an unobserved task
  exception â€” a fire-and-forget call whose failure was never awaited.
