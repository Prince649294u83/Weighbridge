# Architecture

## Layering

```
Views  â†’  ViewModels  â†’  Services  â†’  Repositories  â†’  Database
                              â†“
                    Hardware / Printing / Reporting
```

Application execution infrastructure sits beside the service layer, and its dependency
direction is one-way:

```
Validation  Permission  Busy  Dialog  Undo
        â†˜       â†“        â†“      â†“     â†™
             Command pipeline
                    â†“
        Vehicle Entry  (+ modules to come)
```

Nothing in the left-hand row knows the pipeline exists, which is what allows each to be
used on its own and tested without the others.

Three rules hold everywhere, and are the reason the layering survives contact with new
modules:

1. **No business logic in Views.** Code-behind is limited to view concerns â€” a click
   handler that forwards to `SystemCommands`, a template lookup. Everything else is a
   binding to a ViewModel.
2. **No SQL in ViewModels.** ViewModels depend on service interfaces. Data access lives
   behind `IRepository<T>` in `WeighBridge.Infrastructure`.
3. **No direct hardware access from Views.** Serial ports, cameras and printers sit behind
   interfaces in `WeighBridge.Hardware` and `WeighBridge.Printing`.

Dependencies point inward. `WeighBridge.Core` references nothing in the solution; the shell
references everything. A layer never references the one above it, which is what keeps the
test project free of WPF.

## Projects

| Project | Target | Responsibility |
| --- | --- | --- |
| `WeighBridge.Core` | net8.0 | Abstractions, MVVM primitives, domain events, options records. No solution dependencies. |
| `WeighBridge.Domain` | net8.0 | Entities and value objects. `Weighment` aggregate; Masters: `Vehicle`, `Party`, `Material`, `VehicleType`; Security: `User`. |
| `WeighBridge.Infrastructure` | net8.0 | EF Core context, migrations, repositories, unit of work, database initializer with auto-seeding. |
| `WeighBridge.Services` | net8.0 | Navigation, subsystem status monitoring, command pipeline, undo, busy state, background tasks, `WeighmentService`, Master services (`VehicleService`, `PartyService`, `MaterialService`, `VehicleTypeService`), `AuthenticationService`. |
| `WeighBridge.Settings` | net8.0 | Configuration provisioning, user preferences. |
| `WeighBridge.Hardware` | net8.0 | Serial transport, delimited framing, generic ASCII protocol parser, stability detector, weight simulator, camera coordinator, and snapshot generation. |
| `WeighBridge.Printing` | net8.0-windows | Slip printing service (`PrintService`) supporting ASCII, Avery, Toledo slip templates. |
| `WeighBridge.Reporting` | net8.0 | CSV report generation engine (`CsvReportService`). |
| `WeighBridge.App` | net8.0-windows | WPF shell, design system, dialogs, login dialog, view models (`DashboardViewModel`, `VehicleEntryViewModel`, `DuplicateSlipViewModel`, `ReportsViewModel`, `MastersViewModel`, `SettingsViewModel`, `AdministrationViewModel`). |
| `WeighBridge.Tests` | net8.0 | xUnit suite. Headless by design â€” it cannot reference WPF types. |

## Startup pipeline

`App.OnStartup` â†’ `Bootstrapper.StartAsync` â†’ database â†’ login â†’ shell. The order is fixed
because each step depends on the previous one.

`Bootstrapper.StartAsync` brings the process up:

1. **Ensure data folders** â€” `IApplicationPaths.EnsureCreated()`. Everything after this may
   write to disk.
2. **Provision configuration** â€” `ConfigurationProvisioner` creates `appsettings.json` from
   defaults, or repairs one missing keys, or quarantines and regenerates a corrupt one. The
   application never fails to start because configuration is absent or damaged.
3. **Build configuration** â€” JSON file plus `WEIGHBRIDGE_` environment variables. The file is
   still `optional: true`: one deleted between steps 2 and 3 must leave the application
   startable on the options classes' own defaults.
4. **Build the container** â€” with `ValidateOnBuild` and `ValidateScopes`. Logging comes up
   with it, and the startup banner, data root, configuration file and log file path are
   written immediately after.
5. **Load preferences** â€” before the theme, because the theme choice lives in them.
6. **Apply theme** â€” before any window exists, so there is no flash of the wrong theme.
7. **Start background work** â€” `IBackgroundTaskManager` registers and starts
   `HealthRefreshTask`, so every loop in the process has one owner and shutdown has one place
   to stop them all.

`App.OnStartup` then decides whether there is an application to show:

8. **Migrate the database, awaited, and act on the result** â€”
   `Bootstrapper.InitializeDatabaseAsync()`. On failure the operator gets a dialog that names
   the database and the process exits `-1`. It is awaited rather than fire-and-forget because
   authentication queries the database: a login dialog over a database that could not be
   opened is a dialog whose first query throws.
9. **Authenticate** â€” `IDialogService.ShowLoginAsync()`, which runs first-run administrator
   setup when the `Users` table is empty and a sign-in otherwise. Cancelled or failed login
   exits `0`; no shell is created.
10. **Create and show the shell** â€” resolved from the container, persisted placement restored,
    then `MainWindow = shell; shell.Show()`.
11. **Switch `ShutdownMode` to `OnMainWindowClose`** â€” not before. `ShutdownMode` is
    `OnExplicitShutdown` from the constructor, because WPF makes the first `Window` it sees the
    `MainWindow`, and under `OnMainWindowClose` dismissing the *login dialog* would shut the
    application down before the shell could open.

`MainWindowViewModel.InitializeAsync` awaits **the same initialiser** again when the first
module opens. `InitializeAsync` memoizes its task under a lock, so that await joins the run
from step 8 rather than starting a second concurrent migration â€” and it means a module can
never query a table the migration has not created yet.

Shutdown runs in reverse: stop background tasks, stop status monitoring, release the weight
indicator, save window placement, save preferences, flush the log.

## Dependency injection

Constructor injection throughout. There is no service locator and no static mutable state.
The container is private to the `Bootstrapper`; only the composition root resolves from it.

It is built with `ValidateOnBuild` and `ValidateScopes` enabled, so a missing registration
or a captive dependency fails loudly at startup rather than at first use.

Registered: configuration and options, `IApplicationPaths`, logging, `ISettingsService`,
`IThemeService`, `INavigationService`, `IDialogService`, `IWindowPlacementService`,
`IViewLocator`, `ISystemStatusService`, the database context factory, `IRepository<>`,
`IUnitOfWork`, health checks, and the hardware, printing and reporting placeholders.
Execution infrastructure adds `IEventBus`, `INotificationManager`, `IBackgroundTaskManager`,
`IHealthMonitor`, `ICommandExecutor`, `IUndoManager`, `IBusyStateService`,
`IPermissionService`, `IAuthorizationService` and `IValidator<>` implementations. Business
services register alongside them â€” `IWeighmentService` is the first.
ViewModels are transient; the shell and its ViewModel are singletons.

## Navigation

One window. There is no window switching anywhere â€” every module is hosted in the shell's
content area.

`NavigationService` holds back and forward stacks and resolves each destination ViewModel
from the container, so a module always gets its dependencies injected. It has no WPF
reference: it raises `Navigated`, and the shell swaps its content in response. That is what
makes the history logic testable headlessly.

`ViewLocator` maps a ViewModel to its View by convention â€”
`...ViewModels.FooViewModel` â†’ `...Views.FooView` â€” and caches the result.

## Error handling

Three surfaces are hooked, because WPF reports failures on three different paths:

- `Application.DispatcherUnhandledException` â€” UI thread. Logged, shown in a custom error
  dialog, marked handled.
- `AppDomain.CurrentDomain.UnhandledException` â€” background threads. Logged; the process is
  already terminating.
- `TaskScheduler.UnobservedTaskException` â€” faulted fire-and-forget tasks. Logged and
  observed, so a stray `async void` cannot take the process down from the finalizer thread.

The application should not terminate unexpectedly.

## Command pipeline

`ICommandExecutor` is the single path a business operation takes. Every stage is skippable
per command through `CommandExecutionOptions`, but the order is fixed:

```
validate â†’ authorize â†’ busy scope â†’ execute â†’ log + audit â†’ undo â†’ event â†’ notify
```

Each stage failure is a *distinct* outcome on `CommandResult`, never a bare boolean:
`Invalid` carries the `ValidationResult`, `Denied` carries the `AuthorizationResult`, `Failed`
carries the exception, `Cancelled` is its own state. A caller can tell "you may not do this"
apart from "this was wrong" apart from "this broke", which a single `false` cannot express.

Exceptions are caught at the boundary, logged with the correlation id, and returned as
`Failed` â€” never swallowed. The busy scope is released in a `finally`, so success, failure,
cancellation and exception all leave the UI unblocked.

Commands are POCOs. `IApplicationCommand` has no WPF reference anywhere in its closure,
which is why the whole pipeline is tested headlessly.

Undo registration is the last stage before publication and only runs when the command
implements `IUndoableCommand` and reports success â€” a failed command never enters history.

## The weighment â€” the first business domain

A weighment is one vehicle's visit: it arrives, it is weighed, it leaves, it is weighed again,
and the difference is what the slip is about.

```
NEW â”€â”€createâ”€â”€â–¶ FIRST WEIGHMENT â”€â”€first weightâ”€â”€â–¶ WAITING FOR SECOND â”€â”€second weightâ”€â”€â–¶ COMPLETED
                      â”‚                                   â”‚
                      â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ cancel â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
                                       â–¼
                                   CANCELLED
```

`Weighment` (in `WeighBridge.Domain/Weighments/`) is the aggregate root â€” `EntityBase`,
`IAggregateRoot`, `ISoftDeletable`, all three of which already existed. Four rules hold it
together:

1. **Transitions are methods, not setters.** `RecordFirstWeight`, `RecordSecondWeight`,
   `Cancel`. The setters that matter are private, so an invalid sequence cannot be assembled by
   property assignment â€” recording a second weight before a first throws rather than producing a
   record no one can explain.
2. **`NET = GROSS âˆ’ TARE`, computed from the two captures.** Which capture is gross and which is
   tare depends on `WeighmentMode` (`GrossFirst` for a loaded arrival, `TareFirst` for an empty
   one), so `Gross` and `Tare` are projections over `FirstWeight` / `SecondWeight` rather than
   two more stored fields that could disagree.
3. **Every weight carries its provenance.** `WeightCapture` is kilograms + `WeightSource`
   (`Manual` / `Indicator`) + a UTC timestamp. An auditor can tell a typed figure from a read
   one, which matters more than saving a column.
4. **Two identities.** The database allocates the primary key; `SlipNumbers.Format` derives the
   human-readable `WB-000001` from it. That makes the series gap-free under concurrency with no
   counter row to lock, and monotonic in the order weighments were opened. The format lives in
   one class, so a per-year or per-bridge series is one change instead of a dozen.

Vehicle, Party and Material are Domain entities under `WeighBridge.Domain/Masters` (EF configurations, filtered unique indexes, `NOCASE` collations and full repository coverage). Vehicle Entry captures their snapshots into the weighment and also
as text on the weighment, which is what the screen collects; they become tables in the Masters
module, where they are actually maintained.

### Persistence

`WeighmentConfiguration` maps the aggregate to `Weighments`. It is discovered automatically â€”
`OnModelCreating` applies every `IEntityTypeConfiguration<>` in the assembly, so a new table
needs no change to the context.

**Weights are stored as whole grams** through a `ValueConverter<decimal, long>`. SQLite has no
decimal type: stored as text a `decimal` round-trips exactly but sorts lexicographically, so
`SUM` and `WHERE net > 5000` silently return wrong answers; stored as `REAL` comparison works
and exactness fails. On a record an invoice is raised from, the second is the worse trade. An
integer gram count is exact, orders and sums correctly in SQL, and has three orders of
magnitude more resolution than any indicator reports.

The two captures are owned types (`FirstWeightGrams`, `FirstWeightAtUtc`, `FirstWeightSource`,
and the same for the second), so one table holds the whole aggregate. A soft-delete query filter
keeps retired weighments in the table for the audit trail and out of every query;
`IgnoreQueryFilters()` is the deliberate escape hatch for an administrative screen.

### The path a weighment takes

```
VehicleEntryViewModel
   â†’ CreateWeighmentCommand / RecordFirstWeightCommand
     / RecordSecondWeightCommand / CancelWeighmentCommand
       â†’ ICommandExecutor  (validate â†’ authorize â†’ busy â†’ execute â†’ log+audit â†’ undo â†’ event â†’ notify)
         â†’ IWeighmentService
           â†’ IRepository<Weighment> / IUnitOfWork
             â†’ WeighBridgeDbContext
```

Four commands, not one per property. There is no `CompleteWeighmentCommand` because recording
the second weight *is* completion — a weighment holding both weights but not yet `Completed`
would be a state the domain cannot describe.

`NewWeighmentValidator` runs in the pipeline's validate stage, `Permissions.WeighmentCreate` /
`Permissions.WeighmentCancel` in its authorize stage, and five events
(`WeighmentCreatedEvent`, `FirstWeightRecordedEvent`, `SecondWeightRecordedEvent`,
`WeighmentCompletedEvent`, `WeighmentCancelledEvent`) are published from its event stage. The
ViewModel holds no `DbContext` and no SQL.

### F1 / F2 Operational Workflow & Presentation Architecture
- **Single Service Boundary:** `IWeighmentService` is the sole business boundary for weighment mutations. `VehicleEntryViewModel` acts as presentation coordinator without direct database access.
- **Presentation State Machine (`WeighmentWorkflowState`):** Decouples UI presentation states (`Idle`, `F1Entry`, `TicketAllocated`, `AwaitingFirstWeight`, `F2Entry`, `F2Selected`, `AwaitingSecondWeightCapture`, `Completed`) from the persisted domain lifecycle (`WeighmentStatus`: `Created`, `AwaitingSecondWeight`, `Completed`, `Cancelled`).
- **Deterministic Ticket Allocation:** Clicking "1. Allocate Ticket" creates and commits an authoritative record in SQLite (`Status = Created`), generating a durable `SlipNumber` before weight capture. This prevents lost tickets upon crashes.
- **F1 Historical Immutability:** Once the first weight is captured (`AwaitingSecondWeight`), `UpdateDetails(...)` strictly rejects modifications. F2 only modifies dedicated second-entry fields via `UpdateSecondEntryDetails(...)`. UI renders historical F1 data in a read-only locked card.
- **F2 Esc / Clear Safety:** Invoking `Esc` or `ClearContextCommand` resets active UI context (`ActiveWeighmentId = null`, `ActiveVersion = null`, form inputs cleared) while preserving the underlying database record in `AwaitingSecondWeight`.
- **Mode-Aware Net Weight & `NetWeightPolicy`:** GrossFirst and TareFirst modes calculate net weight consistently ($|\text{Gross} - \text{Tare}|$). The domain strictly rejects Gross < Tare; zero net (Gross == Tare) is governed by `NetWeightPolicy` (`RejectZero` vs `AllowZero`).
- **Calculated Bag Invariants:** Inputs `NumberOfBags` and `BagWeightKg` are persisted, while `TotalBagWeightKg` and `ActualWeightKg` are calculated properties. Negative actual material weight is refused by the domain.
- **Exact Integer Storage:** Weights are converted to integer grams (`long NetWeightGrams`, `long BagWeightGrams`), and monetary charges to integer paise (`long ChargesPaise`, `long SecondChargesPaise`).
- **Optimistic Concurrency:** `Weighment.Version` (`Guid`) is regenerated on every material state transition and mapped as an EF Core concurrency token (`IsConcurrencyToken()`). Stale saves trigger `DbUpdateConcurrencyException`. In the UI, concurrency conflicts preserve entered F2 values in memory and present an explicit Reload action.

## Subsystem health

Two services observe the system and are deliberately *not* merged:

- `ISystemStatusService` drives the status bar. It aggregates per-subsystem state for
  display and is what the operator's refresh button invokes.
- `IHealthMonitor` runs `IHealthCheck` probes on a schedule through
  `IBackgroundTaskManager`, for diagnostics rather than for the status bar.

They share the `IHealthCheck` abstraction but not a loop, so there is one scheduler and no
competing database probes.

The division is by ownership, not by subsystem. `SystemStatusService` owns the five fixed
indicators and probes them itself. `IHealthMonitor` starts with **no** registered checks and
exists for modules to register their own at runtime, which is why `Overall` reports `Unknown`
rather than `Healthy` on an empty set â€” nothing has been verified yet, and an empty set is
not a claim of health.

Registering the four existing checks with the monitor as well would double-probe the same
serial port and the same database on two timers. That is the duplication this split exists to
avoid, so it is deliberately not done.

## Window placement

`WindowPlacementService` persists size, position and maximised state.

Two details are load-bearing and were both learned the hard way:

- The tracked rectangle comes from `Window.RestoreBounds`, not from
  `Left`/`Top`/`ActualWidth`/`ActualHeight`. WPF raises `LocationChanged` while
  `WindowState` still reads `Normal`, so reading the live properties during a transition
  captures the *destination* of a maximise (a small negative overshoot) or of a minimise
  (parked near âˆ’32000 device pixels) and stores it as the operator's position.
- Shutdown reads a lock-guarded snapshot, never the `Window`. `App.RunShutdown` blocks the
  UI thread awaiting the shutdown chain, so touching a dispatcher-affine property from that
  chain either throws (off-thread) or deadlocks (if marshalled back).

A stored position is only reused when enough of the window would land on a connected
monitor; otherwise the shell centres itself.
