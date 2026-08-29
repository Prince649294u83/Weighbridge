# Implementation status

Phase 0.5 — Enterprise Infrastructure Layer, Prompt 7 — Vehicle Entry, Prompt 8 — Master Data Subsystem & Integration, Prompt 9 — Real Weight Indicator + Camera Integration, and Prompt 10 — Complete End-to-End Application Delivery.

**Verified state as of 2026-08-18, after the forensic audit
([FinalAuditReport.md](FinalAuditReport.md)):**

| Measure | Value | How it was obtained |
| --- | --- | --- |
| Build | 0 errors, 0 warnings | `dotnet build WeighBridge.sln --nologo -v q` |
| Tests | 649 passed (639 baseline after cleanup + 10 WeightDecoder tests), 0 failed, 0 skipped | `dotnet test --nologo`, cross-checked across all test suites |
| Hardware Diagnostic Pipeline | Verified (9-byte framing, 8N1 @ 2400 on COM3) | `WeighBridge.SerialDiagnostic` & `WeightIndicatorService` |
| Runtime — shell | verified, exit code 0 | `scripts/runtime-smoke.ps1` |
| Runtime — Vehicle Entry | verified, exit code 0 | `scripts/vehicle-entry-smoke.ps1` |
| Runtime — Master Data | verified, exit code 0 | `scripts/masters-smoke.ps1` |
| Runtime — Hardware & Camera | verified, exit code 0 — but see [F-018](FinalAuditReport.md#f-018): this script was passing on a 328-byte generated JPEG | `scripts/hardware-smoke.ps1` |
| Runtime — Final End-to-End Smoke | verified, exit code 0, on the Debug build **and** the published Release build | `scripts/final-application-smoke.ps1`, `-ExePath` for the published build |
| Runtime — startup | 10 of 10 cycles | `scripts/startup-cycle-audit.ps1` |
| Runtime — navigation | 7 modules × 3 rounds, log-confirmed | `scripts/navigation-audit.ps1` |
| Runtime — corrupt/zero-byte database | 3 of 3 variants refused | `scripts/corrupt-database-startup.ps1` |
| Runtime — service-layer permissions | exit 0 | `scripts/privilege-enforcement-check.ps1` |
| **Production-ready** | **No** | 10 findings remain open; see below |

**The audit found 32 defects and fixed 22 of them.** Every "Verified" in the module table
below means *the module's happy path was exercised*. It does not mean the module was correct:
each of the following was recorded as "Verified" here before the audit reproduced a defect in
it — user-management writes that were silently discarded
([F-001](FinalAuditReport.md#f-001)), a seeded `admin` / `admin123` credential
([F-004](FinalAuditReport.md#f-004)), unsalted SHA-256 password hashing
([F-005](FinalAuditReport.md#f-005)), an audit trail naming the Windows account instead of the
operator ([F-020](FinalAuditReport.md#f-020)), report export and slip reprint enforcing no
permission at all ([F-025](FinalAuditReport.md#f-025),
[F-026](FinalAuditReport.md#f-026)), and a shipped default that weighed vehicles with
generated numbers ([F-027](FinalAuditReport.md#f-027)).

What still blocks real use is
[Before this is used for real work](FinalAuditReport.md#before-this-is-used-for-real-work).
The largest single gap is that **there is no audit table** — the audit trail is a rolling text
file in the operator's own profile ([F-030](FinalAuditReport.md#f-030)).

## Modules

Third column: automated test cases where they exist, and what the audit found where they do
not. "Verified" on its own means a runtime script exercised it, nothing more.

| Module | Status | Tests / audit outcome |
| --- | --- | --- |
| Authentication & Identity | Blocking login dialog; **first-run administrator setup** (no seeded credential); PBKDF2-SHA256 at 600 k iterations; session context; **no sign-out** | 17 cases. F-004, F-005 fixed; F-022 open |
| Administration & Users | User account CRUD, role assignments. **There is no audit log viewer and no audit table** — the row that claimed one was wrong | F-001, F-002, F-008 fixed; F-030 open (P1); no unit tests, App layer is untestable (F-010) |
| Weight Indicator & Camera Hardware | Real serial transport, framing, generic ASCII parser, stability detection; simulator **reachable only by name**; JPEG *generator*, gated behind `Camera:Enabled` | 30 + 10 registration cases. F-027, F-028, F-029 fixed. This installation's own `appsettings.json` still selects the simulator |
| Master Data | Vehicles, Parties, Materials, Vehicle Types + integration | 28. Not re-audited as a feature (P9 not run) |
| Vehicle Entry | First real screen, persisted, master integration, live scale readout, camera captures, duplicate guard | 107. Not walked criterion by criterion (P8 not run) |
| Duplicate Slip | Search completed transactions, reprint slip with duplicate marker; reprint now requires `Weighment.Reprint` | F-026 fixed, and **provable only by `scripts/privilege-enforcement-check.ps1`** — the Printing layer cannot be unit-tested (F-010) |
| Printing Subsystem | `WindowsPrintService` with Generic ASCII, Toledo, Avery templates | No unit tests possible (F-010). No printer attached to this machine: a correctly stamped page has never been produced |
| Reporting Subsystem | `CsvReportService`, parameterised Daily, Material and Party reports; export now requires `Reports.Export` | 5 cases (2 pre-existing + 3 for F-025). F-025, F-032 fixed |
| Dashboard | Operational metrics (completed today, waiting vehicles), live indicator status, quick shortcuts | F-019 fixed (every visit leaked an indicator subscriber and a `DbContext`); figures themselves not audited (P15 not run) |
| Settings | Theme and navigation collapse only. Hardware, camera and printer are **read-only inspection**, contrary to what `Settings.Edit` implies | F-006: one combo box and one check box, confirmed over UIA; nothing else on the screen is editable |

Older counts in that table are `[Fact]`/`[Theory]` **attributes** per test directory, plus 17
in `DependencyInjection/` and 10 in `Navigation/`, 13 in `Settings/`, 9 in `Mvvm/` that predate
Phase 0.5; the Vehicle Entry row is the runner's count (72 attributes). Counts added by the
audit are **cases**, not attributes — a `[Theory]` with six `InlineData` rows is six — because
that is what the runner's 531 is. The two conventions are not comparable, which is why the
audit report asserts only the measured total.

## Baseline

Commit `3077420` (2026-08-09), *"Baseline: Phase 0.5 Components 1-5 verified"* — 207 tests.
That figure is carried from the session that produced the commit; it was not re-measured
against the old tree. Everything below is uncommitted work on top of it.

Test areas present at that commit: `DependencyInjection/CoreRegistrationTests`, `Events`,
`Health`, `Logging`, `Mvvm`, `Navigation`, `Notifications`, `Settings`, `Tasks`.

## Components 6–11 — files

Implemented as one wave, because the dependency direction runs
*Validation, Permission, Busy, Dialog, Undo* → *Command Pipeline*. Building the pipeline
first would have required stub dependencies that then got thrown away.

### Component 6 — Command Pipeline

Added:
- `src/WeighBridge.Core/Commands/CommandContext.cs`
- `src/WeighBridge.Core/Commands/CommandExecutionOptions.cs`
- `src/WeighBridge.Core/Commands/CommandExecutionState.cs`
- `src/WeighBridge.Core/Commands/CommandResult.cs`
- `src/WeighBridge.Core/Commands/IApplicationCommand.cs`
- `src/WeighBridge.Core/Commands/ICommandExecutor.cs`
- `src/WeighBridge.Core/Events/Catalog/CommandExecutedEvent.cs`
- `src/WeighBridge.Services/Commands/CommandExecutor.cs`
- `tests/WeighBridge.Tests/Commands/CommandExecutorTests.cs` — 40
- `tests/WeighBridge.Tests/Commands/DocumentedCommandPatternTests.cs` — 2

### Component 7 — Undo Framework

Added:
- `src/WeighBridge.Core/Undo/IUndoManager.cs`
- `src/WeighBridge.Core/Undo/IUndoableCommand.cs`
- `src/WeighBridge.Core/Undo/UndoEntry.cs`
- `src/WeighBridge.Core/Undo/UndoOptions.cs`
- `src/WeighBridge.Core/Undo/UndoResult.cs`
- `src/WeighBridge.Services/Undo/UndoManager.cs`
- `tests/WeighBridge.Tests/Undo/UndoManagerTests.cs` — 27

### Component 8 — Busy State Manager

Added:
- `src/WeighBridge.Core/Busy/BusyOperation.cs`
- `src/WeighBridge.Core/Busy/IBusyStateService.cs`
- `src/WeighBridge.Services/Busy/BusyStateService.cs`
- `tests/WeighBridge.Tests/Busy/BusyStateServiceTests.cs` — 21

### Component 9 — Dialog Framework

**Completed an existing framework rather than adding a second one.** `src/WeighBridge.App/Dialogs/`
already held `DialogService`, `DialogWindowBase`, `MessageDialog`, `ProgressDialog` and their
ViewModels from Module 0.1. Only the Core abstraction was missing.

Added:
- `src/WeighBridge.Core/Dialogs/IDialogService.cs`
- `src/WeighBridge.Core/Dialogs/IProgressReporter.cs`

**Zero automated tests, and this is structural, not an oversight.** `WeighBridge.Tests`
targets `net8.0` without WPF so it cannot reference `WeighBridge.App`; the implementation is
`Window`-based and therefore unreachable from the suite. Verified instead by driving the real
application and reading the screenshots: all eight dialog kinds (information, success,
warning, error, confirmation, destructive confirmation, loading, progress) render with
severity-distinct glyph and accent, over a modal scrim, with no `MessageBox` anywhere. The
diagnostic path used for that was removed afterwards.

### Component 10 — Validation Framework

Added:
- `src/WeighBridge.Core/Validation/IValidatable.cs`
- `src/WeighBridge.Core/Validation/IValidator.cs`
- `src/WeighBridge.Core/Validation/ValidationContext.cs`
- `src/WeighBridge.Core/Validation/ValidationError.cs`
- `src/WeighBridge.Core/Validation/ValidationResult.cs`
- `src/WeighBridge.Core/Validation/ValidationRule.cs`
- `src/WeighBridge.Core/Validation/ValidationSeverity.cs`
- `src/WeighBridge.Core/Validation/Validator.cs`
- `tests/WeighBridge.Tests/Validation/ValidatorTests.cs` — 9

### Component 11 — Permission Framework

Added:
- `src/WeighBridge.Core/Security/AuthorizationResult.cs`
- `src/WeighBridge.Core/Security/IPermissionService.cs`
- `src/WeighBridge.Core/Security/Permission.cs`
- `src/WeighBridge.Core/Security/Permissions.cs`
- `src/WeighBridge.Core/Security/Role.cs`
- `src/WeighBridge.Core/Security/SecurityOptions.cs`
- `src/WeighBridge.Services/Security/PermissionService.cs`
- `tests/WeighBridge.Tests/Security/PermissionTests.cs` — 15

`Permissions` and `Roles` are static constant holders specifically so no call site ever
writes `"CanEditVehicle"` as a literal.

### Cross-cutting

Modified:
- `src/WeighBridge.Core/DependencyInjection/CoreServiceCollectionExtensions.cs`
- `src/WeighBridge.Services/DependencyInjection/ServicesServiceCollectionExtensions.cs`
- `src/WeighBridge.App/MainWindow.xaml` — undo/busy affordances
- `src/WeighBridge.App/Styles/Buttons.xaml` — icon-font fix, see Architecture.md
- `Documentation/Architecture.md`, `Documentation/Development.md`

Added:
- `tests/WeighBridge.Tests/DependencyInjection/ComponentsRegistrationTests.cs` — 6
- `scripts/runtime-smoke.ps1`, `scripts/uia-dump.ps1`

## Runtime verification — the shell

`scripts/runtime-smoke.ps1` launches the real WPF application under UI Automation and
returns exit code 0 only if every one of these holds:

1. Main window appears and is enabled.
2. All seven navigation targets reached — confirmed by a **log count delta** per target, not
   by presence. The shell restores the last module at startup, so presence is already true
   for one module before any click.
3. Theme toggled twice.
4. Status refresh command invoked.
5. A background health/status tick observed (35 s wait).
6. Process exits code 0 with `==== Shutdown complete ====` in the log.
7. No error-level log entries appended during the run.

Clicks are synthesized mouse input after `SetForegroundWindow`, with one retry per target,
because `SelectionItemPattern.Select()` only sets `IsChecked` and never raises Click.

## Vehicle Entry (Prompt 7) — files

The first business module, and the first code to run through the infrastructure rather than
alongside it. **No infrastructure component was redesigned.** Two defects were found in it and
fixed minimally (both recorded under "Defects found and fixed" below).

### Domain

Added:
- `src/WeighBridge.Domain/Enums/WeighmentMode.cs` — `GrossFirst` / `TareFirst`
- `src/WeighBridge.Domain/Enums/WeighmentStatus.cs` — `Created`, `AwaitingSecondWeight`, `Completed`, `Cancelled`
- `src/WeighBridge.Domain/Enums/WeightSource.cs` — `Manual` / `Indicator`
- `src/WeighBridge.Domain/Weighments/Weighment.cs` — the aggregate root and its state machine
- `src/WeighBridge.Domain/Weighments/WeightCapture.cs` — one weighing: kilograms, source, UTC time
- `src/WeighBridge.Domain/Weighments/SlipNumbers.cs` — `WB-000001` formatting and parsing

`Weighment` derives from `EntityBase` and implements `IAggregateRoot` and `ISoftDeletable` —
all three already existed. Transitions are methods (`RecordFirstWeight`,
`RecordSecondWeight`, `Cancel`); the setters that matter are private, so an invalid sequence
cannot be assembled by property assignment. Net is `Gross − Tare` computed on demand from
`decimal`, never stored as a third independent number that could disagree with the two it
comes from.

Three entities were considered and **not** created: Vehicle, Party and Material. Vehicle
Entry captures them as text on the weighment, which is what the screen collects; they become
tables in the Masters module, where they are actually maintained.

### Core

Added:
- `src/WeighBridge.Core/Abstractions/IWeighmentService.cs`
- `src/WeighBridge.Core/Events/Catalog/WeighmentEvents.cs` — the five events

Modified:
- `src/WeighBridge.Core/Abstractions/IRepository.cs` — added the ordering/paging overload the waiting list needs
- `src/WeighBridge.Core/DependencyInjection/CoreServiceCollectionExtensions.cs`

### Infrastructure

Added:
- `src/WeighBridge.Infrastructure/Migrations/20260815104634_AddVehicleEntry.cs` (+ `.Designer.cs`)
- `src/WeighBridge.Infrastructure/Migrations/WeighBridgeDbContextModelSnapshot.cs`
- `src/WeighBridge.Infrastructure/Persistence/Configurations/WeighmentConfiguration.cs`

Modified:
- `src/WeighBridge.Infrastructure/Repositories/EfRepository.cs`
- `src/WeighBridge.Infrastructure/Persistence/DatabaseInitializer.cs` — memoized, see defects

### Services

Added:
- `src/WeighBridge.Services/Weighments/WeighmentService.cs`
- `src/WeighBridge.Services/Weighments/NewWeighmentValidator.cs`
- `src/WeighBridge.Services/Weighments/WeighmentCommands.cs`

Modified:
- `src/WeighBridge.Services/DependencyInjection/ServicesServiceCollectionExtensions.cs`

### App

Added:
- `src/WeighBridge.App/ViewModels/WeighmentSummary.cs`

Modified:
- `src/WeighBridge.App/ViewModels/VehicleEntryViewModel.cs` — the placeholder became the real ViewModel; not rewritten from scratch
- `src/WeighBridge.App/Views/VehicleEntryView.xaml`
- `src/WeighBridge.App/ViewModels/MainWindowViewModel.cs` — awaits the database initialiser
- `src/WeighBridge.App/Controls/StatusBadge.cs`, `Controls/EmptyState.cs` — automation peers
- `src/WeighBridge.App/Styles/Typography.xaml` — `Text.Numeric.Emphasis`

### Tests

Added:
- `tests/WeighBridge.Tests/Weighments/WeighmentTests.cs` — 27 attributes: transitions, weight arithmetic, invalid sequences
- `tests/WeighBridge.Tests/Weighments/WeighmentPersistenceTests.cs` — 21: real SQLite round-trips
- `tests/WeighBridge.Tests/Weighments/WeighmentCommandTests.cs` — 16: the four commands through the real pipeline
- `tests/WeighBridge.Tests/Weighments/NewWeighmentValidatorTests.cs` — 8
- `tests/WeighBridge.Tests/Weighments/WeighmentHarness.cs` — shared fixture, no tests of its own

### Scripts

Added:
- `scripts/vehicle-entry-smoke.ps1`

## Runtime verification — Vehicle Entry

`scripts/vehicle-entry-smoke.ps1` returns exit code 0 only if all 24 assertions hold. It
starts by **deleting the database**, so the path every new installation takes is the path
under test. It is a development script and it writes test weighments — not for a machine
holding real data.

Run 1 — first launch on no database:
1. Migration applied before the first module reads a table (proved by log ordering:
   `Applying 1 pending migration(s)` at 19:02:32.020 precedes
   `Navigated to VehicleEntryViewModel` at 19:02:32.389).
2. The arrival picker reads as words, and both empty states explain themselves.
3. `WB-000001` allocated and shown; stage reads "First weight pending"; the next action is
   spelled out.
4. Recording with an empty weight box is refused **with a reason on screen**, and the stage
   does not advance.
5. First weight 32,500 kg recorded; stage becomes "Awaiting second weight".
6. A tare of 40,000 kg against a 32,500 kg gross is refused with "must be greater than".
7. Second weight 12,250.5 kg completes it; net reads 20,249.5 — to the half kilo.
8. The finished weighment asks for nothing more ("Record tare weight" is gone).
9. `WB-000002` left waiting at 28,000 kg.
10. `WB-000003` cancelled through the confirmation dialog — **declined first**, and declining
    left the weighment alone.
11. Clean shutdown, no error-level log entries, database file grew.

Run 2 — the application is restarted:
12. `MH14CD5678` and its 28,000 kg are still there.
13. The cancelled weighment is **not** in the waiting list.
14. Second clean shutdown.

Screenshots are written to `%TEMP%\weighbridge-vehicle-entry-*.png` and were read, not just
counted — that is what surfaced the two UI defects the 21 passing assertions had missed.

## Defects found and fixed during Prompt 7

All five are in infrastructure written earlier, found by running the real application. Each
fix is minimal and none changed a component's design.

| Defect | Root cause | Fix |
| --- | --- | --- |
| First launch showed "no such table: Weighments" | `App.OnStartup` fires the database initialiser and does not await it; the first module opened and queried first | `DatabaseInitializer.InitializeAsync` memoizes its task, and `MainWindowViewModel.InitializeAsync` awaits it — joining the run rather than starting a second |
| Net weight rendered as "20 , …" | `Text.Base` sets `TextTrimming=CharacterEllipsis`, inherited by the numerics; the display size needs 192px and a third of the card is 96px | Net moved to its own full-width row in a `Viewbox` with `StretchDirection=DownOnly` |
| `StatusBadge` text unreadable by a screen reader | A `TextBlock` inside a `ControlTemplate` is outside the UIA control view | `StatusBadgeAutomationPeer` |
| `EmptyState` text unreadable, and "Nothing waiting" never rendered at all | Same peer problem; plus an explicit `Style` **replaces** the default style including its `ControlTemplate` | `EmptyStateAutomationPeer`, and `BasedOn="{StaticResource {x:Type controls:EmptyState}}"` |
| The arrival picker read `WeighmentModeOption { Value = GrossFirst, … }` | `DisplayMemberPath` reaches dropdown item containers but not the closed selection box, which renders the object — and a record's generated `ToString` prints its members | `ToString() => Text` on the option record |

## Known gaps

**This list is the Prompt 7 record (2026-08-15) and is kept as history.** Items 3, 4, 6 and 7
have since been implemented — hardware, login, the duplicate guard and printing all exist —
and item 8 is half done: party and material are still typed as text, but the text is matched
against the master tables and the matching `PartyId` / `MaterialId` is recorded on the
weighment. Items 1, 2 and 5 are still true as written. The current, audited gap list is
[Before this is used for real work](FinalAuditReport.md#before-this-is-used-for-real-work).

1. **No toast host.** `INotificationService` is complete, registered and raises all four
   severities at correct log levels, but nothing in `WeighBridge.App` consumes it — there is
   no notification view to render into. Deliberate: adding one is permanent UI, out of scope
   for Phase 0.5. Belongs to whichever phase adds shell chrome.
2. **Dialog framework has no automated coverage** — structural, see Component 9 above. The
   same limit applies to `VehicleEntryViewModel`, `WeighmentSummary`, `StatusBadge` and
   `EmptyState`: they live in `WeighBridge.App` and are covered only by
   `scripts/vehicle-entry-smoke.ps1`.
3. **No hardware, and no simulation either.** `IWeightIndicatorService` is still the
   disconnected placeholder. "Read indicator" is present but disabled, the badge reads
   "No indicator connected — type the weight", and every typed weight is recorded as
   `WeightSource.Manual`. Nothing pretends to be a scale. Prompt 8 brings the real serial
   integration, and any simulator it ships must be identified as one.
4. **Login is configuration, not identity.** `PermissionService` seeds the operator role from
   `SecurityOptions.DefaultRole`. There is no login UI and no operator table. Permissions are
   enforced (`Permissions.WeighmentCreate`, `Permissions.WeighmentCancel`) against that
   seeded role.
5. **Work is uncommitted.** Components 6–11 and all of Prompt 7 sit in the working tree on
   top of `3077420`.
6. **No duplicate-weighment guard.** Nothing stops two open weighments for the same vehicle
   number. The real app's behaviour here is unknown; picking one would be inventing a rule.
7. **"Print slip" is deliberately absent.** The printing service is a placeholder, and a
   button that cannot print is worse than no button. It belongs to Prompt 9.
8. **Masters are free text.** Party, material, driver and transporter are typed onto the
   weighment and validated only for length, until the Masters module gives them tables.

## Change record

| Field | Value |
| --- | --- |
| **Date** | 2026-08-15 |
| **Phase** | Prompt 7 — Vehicle Entry, first business module |
| **Objective** | Prove the eleven finished infrastructure components by building a real, persisted, end-to-end weighment workflow on top of them |
| **Files added** | 6 domain, 2 core, 4 infrastructure (incl. migration + snapshot), 3 services, 1 app, 5 test, 1 script — listed above |
| **Files modified** | `IRepository`, `EfRepository`, `DatabaseInitializer`, both DI extension files, `VehicleEntryViewModel`, `VehicleEntryView.xaml`, `MainWindowViewModel`, `StatusBadge`, `EmptyState`, `Typography.xaml`, four documentation files, four stale code comments |
| **Database changes** | Migration `20260815104634_AddVehicleEntry`; one table `Weighments`; weights stored as whole grams via `ValueConverter<decimal, long>` (`FirstWeightGrams`, `SecondWeightGrams`, `NetWeightGrams`, all INTEGER); unique index on `SlipNumber`, plus indexes on `Status`, `VehicleNumber` and `CreatedAtUtc`; a soft-delete query filter so retired rows stay for the audit trail and out of every query |
| **Test count before** | 330 passed |
| **Test count after** | 437 passed, 0 failed, 0 skipped |
| **Build result** | 0 warnings, 0 errors |
| **Runtime result** | `vehicle-entry-smoke.ps1` exit 0 (24 assertions, from a deleted database); `runtime-smoke.ps1` exit 0 (shell regression) |
| **Known gaps** | The eight items above — chiefly: no hardware integration and no simulation, no authentication, no printing, no duplicate-vehicle guard, no notification toast host |
| **Next recommended step** | Prompt 8 — the weight indicator: real serial protocol behind `IWeightIndicatorService`, with any simulator clearly labelled as a simulator |

## Change record — forensic audit

| Field | Value |
| --- | --- |
| **Dates** | 2026-08-16 to 2026-08-18 |
| **Phase** | Forensic audit of the application as it exists on disk and running, treating every prior "complete", "passed" and "verified" as untrusted. No features added. |
| **Objective** | Reproduce before fixing; fix every P0 and P1; leave a regression test behind for each fix; record what is still wrong. |
| **Findings** | 32 recorded — 22 fixed and verified at runtime, 10 open. Full detail, per finding, in [FinalAuditReport.md](FinalAuditReport.md) |
| **P0** | 2, both fixed: user-management writes silently discarded (F-001); seeded `admin` / `admin123` (F-004) |
| **P1 fixed** | F-002 privilege escalation, F-003 login helper, F-005 password hashing, F-020 audit attribution, F-023 zero-byte database accepted as a fresh install, F-025 `Reports.Export` unenforced, F-026 `Weighment.Reprint` unenforced, F-027 simulator as the shipped default, F-028 generated JPEGs filed as photographs |
| **P1 open** | F-022 no sign-out (dead `Logout()` removed, capability not built); F-030 **no audit table** |
| **Files changed** | Itemised file by file, with the change and the finding it belongs to, in [Changes made so far](FinalAuditReport.md#changes-made-so-far) — source, tests and scripts |
| **Database changes** | **None.** No migration was written. The live database was backed up, exercised, and restored to its pre-audit contents (3 weighments, 1 `admin` user, `integrity_check: ok`) |
| **Test count before** | 497 (as recorded above, unverified by counting) |
| **Test count after** | **531 passed, 0 failed, 0 skipped**, cross-checked with `dotnet test --list-tests`. 34 cases added by the audit; tests were also deleted with `AuthenticationService.Logout()` |
| **Build result** | 0 warnings, 0 errors |
| **Runtime result** | 7 scripts at exit 0, including the published Release build (phase 23). Test-the-tests: 7 deliberate defects injected, each failed exactly the assertions aimed at it and nothing else, each removed and the removal verified by `grep -rn AUDIT-PHASE22 src/ tests/ scripts/` |
| **Not run** | P8–P15, P18–P21, P24 — stated explicitly in [Outstanding](FinalAuditReport.md#outstanding) rather than left looking covered |
| **Production-ready** | **No.** See [Before this is used for real work](FinalAuditReport.md#before-this-is-used-for-real-work) |
| **Next recommended step** | The audit table and the four missing foreign keys in one migration (F-030, F-031), then sign-out (F-022), then a `net8.0-windows` test project so the App and Printing layers stop being provable only by PowerShell (F-010) |
