# AI agent handoff

For the next AI coding agent picking up this project. Read this before anything else.

Then read [ProjectReference.md](ProjectReference.md), which is the whole system in one file:
the nine projects, the domain model, the five migrations, the security model, the runtime
paths, every verification script and what it proves, and the verified-vs-unverified split.
This file is the *handoff* — what is broken, what not to touch, and what bit us. That one is
the *reference*.

**If you read one thing before touching code:** confirmation dialogs do not display. See
[Known unresolved issues](#known-unresolved-issues) item 1.

## Read this first: verify state before you trust a prompt

This project is deliberately built across multiple AI agents and models. The failure mode
that has already happened once is a **prompt describing a state the repository has moved past**
— an instruction to "implement Component 6" arriving after Components 6–11 were already
complete, which if followed literally would have deleted 120 passing tests.

So: before implementing anything a prompt asks for, confirm it is not already there.

```bash
cat Documentation/ImplementationStatus.md      # what is actually done
cat Documentation/LegacyParitySpecification.md # authoritative permanent parity contract
ls src/WeighBridge.Core/                       # Commands/ Undo/ Busy/ Validation/ Security/ ?
ls src/WeighBridge.Domain/Weighments/          # is Vehicle Entry's domain already here?
ls src/WeighBridge.Infrastructure/Migrations/   # which migrations have been applied?
dotnet test WeighBridge.sln --nologo -v q      # the real test count (660 passed)
git log --oneline                              # note: work may be uncommitted
```

If the prompt's premise and the repository disagree, **say so and stop** rather than
rebuilding. The repository is the source of truth, not the prompt, and not this file.

### Hardware Diagnostic & Measurement Acceptance (Branch `hardware-diagnostic`)
- **Transport & Framing:** Verified 9-byte packet extraction (`0x5B` ... `0x00`) on `COM3` @ `2400` 8N1 with DTR/RTS asserted.
- **Settings Binding:** Single authoritative `Text` binding with `UpdateSourceTrigger=PropertyChanged` on editable ComboBox; competing `SelectedItem` removed.
- **Service Lifecycle:** `WeightIndicatorService` maintains single background worker task and transport owner with robust `ConnectAsync` coordination and cancellation guards.
- **Active Measurement Profile:** Locked to `DecimalPlaces = 1`, `WeightDigits = 7`, `ScaleFactor = 1.0` following empirical correlation across 0–145 kg (`0000150` $\rightarrow$ `15.0 kg`, `0000900` $\rightarrow$ `90.0 kg`, `0001450` $\rightarrow$ `145.0 kg`).
- **Test Metric:** **660 unit tests passing** (639 baseline + 21 decoder/lifecycle tests, 0 failed, 0 skipped).
- **Physical Stream Status:** Live physical streaming verified on COM3; HUD and captured indicator readings match physical display with zero UI secondary conversions.

## What this application is

A native Windows desktop application for weighbridge operations — .NET 8 LTS, WPF, MVVM.
It runs continuously in an industrial environment for years. It is **not** a website.

Being re-engineered from a legacy Weighbridge Entry application, preserving the original
business behaviour while modernising the architecture.

**RFID is permanently out of scope.** Do not reintroduce it.

## Current position

Phase 0.5 (infrastructure) Components 1–11 complete. Vehicle Entry, Master Data, Hardware,
Printing, Reporting, Duplicate Slip, Dashboard, Settings and Administration all exist.

**Then a forensic audit (2026-08-16 → 2026-08-18) found 32 defects in that work and fixed 22
of them.** Read [FinalAuditReport.md](FinalAuditReport.md) before you trust any "verified" in
this file or in [ImplementationStatus.md](ImplementationStatus.md). The short version:

- **The application is not production-ready**, and the reasons are listed in
  [Before this is used for real work](FinalAuditReport.md#before-this-is-used-for-real-work).
- Two P0s: every user-management write was silently discarded (F-001), and a fixed
  `admin` / `admin123` credential was seeded into any empty database (F-004). Both fixed;
  first-run administrator setup replaced the seed, and hashing is now PBKDF2-SHA256 at 600 k
  iterations.
- Nine more P1s fixed, including two security controls that were **declared and enforced
  nowhere**: `Reports.Export` (F-025) and `Weighment.Reprint` (F-026).
- The shipped default weight source was the **simulator**, and any unrecognised `DriverType`
  fell back to it silently (F-027). Cameras generate their JPEGs and were resolved regardless
  of `Camera:Enabled` (F-028). Both fixed — but the `appsettings.json` **already on this
  machine** still selects the simulator and still has cameras on. Configuration is the
  operator's data; the fix changes the template for new installations only.
- Two P1s remain open: there is **no audit table** (F-030 — the audit trail is a rolling text
  file in the operator's own `%LOCALAPPDATA%`), and there is **no sign-out** (F-022).
- **Test suite: 531 passing, 0 failed, 0 skipped**, cross-checked with
  `dotnet test --list-tests`. Earlier counts in this file (437, 495, 497) are superseded.
- Runtime scripts at exit 0: `runtime-smoke`, `vehicle-entry-smoke`, `masters-smoke`,
  `hardware-smoke`, `final-application-smoke`, `startup-cycle-audit` (10 of 10 cycles),
  `navigation-audit`, `corrupt-database-startup`, `privilege-enforcement-check`,
  `serial-absent-log-check` — and `final-application-smoke` and `navigation-audit` again
  against the **published Release build**.


## Architecture in one screen

```
Views  →  ViewModels  →  Services  →  Repositories  →  Database
                              ↓
                    Hardware / Printing / Reporting

Validation  Permission  Busy  Dialog  Undo
        ↘       ↓        ↓      ↓     ↙
             Command pipeline
                    ↓
        Vehicle Entry  (+ modules to come)
```

Abstractions live in `WeighBridge.Core` (which references nothing else in the solution).
Implementations live in `Services` / `Infrastructure` / `App`. The test project targets
`net8.0` **without** WPF, which is a deliberate forcing function: if something cannot be
tested, it is in the wrong project.

Full detail in [Architecture.md](Architecture.md). Day-to-day patterns in
[Development.md](Development.md).

## Architectural decisions, and why — do not undo these

**1. The command pipeline has a fixed stage order, not composable middleware.**

```
validate → authorize → busy scope → execute → log + audit → undo → event → notify
```

Stages are skippable per execution through `CommandExecutionOptions`, but they cannot be
reordered and no `IPipelineBehavior` abstraction exists.

This is intentional and it is the decision most likely to be "fixed" by a future agent.
A middleware/interceptor abstraction earns its keep when the stages are unknown or supplied
by third parties. Here all five are known, all are already built, and the order is a
correctness property — authorising before validating would leak the existence of records the
operator may not see. An interceptor chain would make that order configurable, which is not
a feature. Do not add one without a concrete third-party extension requirement.

**2. `CommandResult` carries a distinct outcome, never a boolean.**

`Succeeded` / `ValidationFailed` / `Denied` / `Cancelled` / `Failed`. `ValidationFailed`
carries the `ValidationResult`, `Denied` carries the `AuthorizationResult`, `Failed` carries
the exception. "You may not do this", "this was wrong" and "this broke" are three different
things to an operator and collapsing them into one error message is the mistake this shape
exists to prevent.

**3. `ISystemStatusService` and `IHealthMonitor` are deliberately not merged.**

`SystemStatusService` owns the five fixed status-bar indicators and probes them itself.
`IHealthMonitor` starts with **no** registered checks, for modules to register their own at
runtime — which is why `Overall` reports `Unknown` rather than `Healthy` on an empty set.
An empty set is not a claim of health; nothing has been verified yet.

Registering the four existing checks with the monitor as well would double-probe the same
serial port and the same database on two timers. That is the duplication the split exists to
avoid. It looks like a gap. It is not.

**4. Validation is a class, not a fluent chain.** `Validator<T>` with rules declared in the
constructor, so the rule set is fixed and inspectable via `Rules`.

**5. Permissions are constants.** `Permissions.WeighmentCreate`, never `"CanEditVehicle"`.

**6. `CommandExecutionOptions` can disable validation and permission checks, and this lives
at the call site — not as a property on the command.** A command must not be able to exempt
itself from authorization.

**7. There is no `CompleteWeighmentCommand`, and that is not an omission.** Recording the
second weight *is* completion — a weighment with both weights and no `Completed` status would
be a state the domain cannot describe. Splitting them would create a window where the net is
known but the slip is not closed, and something would eventually have to reconcile it. If a
future requirement genuinely needs an approval step between the two, that is a new state, not
a second command against this one.

**8. Weights are `decimal`, and the database stores whole grams.** SQLite has no decimal type.
As text, `decimal` round-trips exactly but sorts lexicographically, so `SUM` and
`WHERE net > 5000` quietly return wrong answers. As `REAL`, comparison works and exactness
fails — on a record an invoice is raised from, that is the worse trade. An integer gram count
is exact, orders and sums correctly, and has far more resolution than any indicator reports.
The converter is in `WeighmentConfiguration`. Do not "simplify" it to `HasColumnType`.

**9. Net weight is computed, never stored as an independent figure.** `NET = GROSS − TARE` from
the two captures. It is persisted (as `NetWeightGrams`) so SQL can filter and sum on it, but it
is only ever written from the computation — there is no setter a caller could use to make the
three columns disagree.

**10. `WeighmentSummary` is a record of primitives, not a wrapper around the entity.** The
screen binds a list to it and a selection back out of it. Value equality means re-reading the
list after a save leaves the operator's selection on the same row, instead of pointing at an
instance the change tracker has replaced.

**11. No weighment command is undoable.** The undo framework is complete and the pipeline
registers any command implementing `IUndoableCommand` — none of the four weighment commands
does. A recorded weighing is an audit record; the reversal an operator is allowed is
`CancelWeighmentCommand`, which keeps the row and stores a reason. Adding undo here would let
a weight silently disappear from a record an invoice is raised from.

## Pitfalls this codebase has actually been bitten by

Each of these cost real debugging time. The comments recording them in the source are worth
keeping.

**`async void` + a modal dialog defers everything after the discard.** `_ = SomeAsync()` runs
synchronously until its first real `await`. If that await is a modal `ShowDialog`, it pumps
its own message loop, so the *rest of the calling method* does not run until the dialog
closes. This produced a phantom "Background database initialisation failed" — the container
had been torn down by the time `OnStartup` reached its next line.

**A WPF style setter outranks an inherited property value.** An implicit `TextBlock` style
beat the inherited icon font and made every icon button render as an empty box.

**Never publish events while holding a lock.** Handlers can re-enter.

**Never `.Result`, `.Wait()` or `GetAwaiter().GetResult()`** in application flows. The one
exception is `App.RunShutdown`, which blocks deliberately because the process is ending and
`ShutdownAsync` never resumes on the UI thread — that comment is load-bearing.

**Window placement must come from `Window.RestoreBounds`,** not live
`Left`/`Top`/`ActualWidth`/`ActualHeight`. WPF raises `LocationChanged` while `WindowState`
still reads `Normal`, so reading live properties mid-transition stores a minimised window's
position of roughly −32000 as the operator's preference.

**Shutdown order is load-bearing.** `StopAllAsync()` runs before settings and window-placement
saves. Do not reorder it.

**`System.Threading.Lock` and `Interlocked.Exchange(ref bool, bool)` are .NET 9.** This is
.NET 8 — use `object` and an `int` flag.

**Tests must not sleep.** Use `TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)`
plus `WaitAsync(timeout)`. `Task.Delay` in a test is a future intermittent failure.

**UIA `SelectionItemPattern.Select()` does not click.** It only sets `IsChecked`. Real
verification needs synthesized mouse input after `SetForegroundWindow`.

**Verify an action by its logged effect as a count delta, not by presence.** The shell
restores the last module at startup, so "a Navigated line exists" is already true for one
module before any click happens.

**A `TextBlock` inside a `ControlTemplate` is invisible to UI Automation.** It is excluded from
the control view, so `FindAll(Descendants, ControlType.Text)` never sees it and a screen reader
never announces it. This bit `StatusBadge` and `EmptyState`: their entire content is templated
text. Any custom templated control that displays text needs its own `AutomationPeer`
(`StatusBadgeAutomationPeer`, `EmptyStateAutomationPeer` are the pattern). Directly-created
elements are fine — which is why `DataGridTextColumn` assertions pass and these did not.

**An explicit `Style` replaces the control's default style, including its `ControlTemplate`.**
A style targeting a templated custom control must say
`BasedOn="{StaticResource {x:Type controls:Foo}}"` or the control renders *nothing at all* —
no error, no warning, just blank. Cost: an empty state that silently never appeared.

**`DisplayMemberPath` does not reach a ComboBox's closed selection box.** The dropdown items go
through the item container, which honours it; the closed box renders the selected object
directly, so a record shows its generated `ToString` — `Foo { Value = …, Text = … }` — to the
operator. Override `ToString()` on the option type. Also: a non-editable WPF ComboBox exposes no
`ValuePattern`; read the selection with
`SelectionPattern.Current.GetSelection()[0].Current.Name`.

**`Text.Base` sets `TextTrimming=CharacterEllipsis`, and the numeric styles inherit it.** A
weight that does not fit is silently rendered as `20 , …`. For a figure that must not be
misread, put it in a `Viewbox` with `StretchDirection="DownOnly"` — smaller is safe, truncated
is not.

**A fire-and-forget initialiser is a race, not a background task.** `App.OnStartup` starts the
database initialisation without awaiting it so a slow network DB cannot hold the window off
screen. The first module to open then queried a table the migration had not created yet. The
fix is not to await it at startup: `DatabaseInitializer.InitializeAsync` memoizes its task, so
the module's `await` **joins** the existing run instead of starting a second concurrent
migration. Any other consumer of a fire-and-forget startup task needs the same shape.

**Read the screenshot, not just the exit code.** Twenty-one assertions passed on a screen that
was showing the operator a C# record's `ToString` and a blank panel where an empty state should
have been. Assertions only check what somebody thought to assert.

**PowerShell variables are case-insensitive,** so a parameter named `$scope` shadows a
script-level `$Scope`. And a line cannot begin with a method call — parenthesise the receiver:
`($x.GetCurrentPattern(...)).Foo`.

**A PowerShell `function` that does `$failures += $x` writes to its own local copy.** Reading
an out-of-scope variable works, so `+=` reads the script-level array, appends, and assigns the
result to a **new function-scoped** variable. The script-level one is never touched, every
failure the function records is discarded, and the script exits 0. This is how the audit's own
privilege check reported a pass while a permission gate was wide open. The fix is the scope
modifier plus one funnel: `function Add-Failure($text) { $script:failures += $text }`, and
every recording site calls it — 15 of them in
`scripts/privilege-enforcement-check.ps1`.

**Do not report your own instrument's reading as the application's behaviour.**
`PRAGMA foreign_keys` returned `0` on the audit's read-only python connection. It is a
**per-connection** setting and says nothing about what `Microsoft.Data.Sqlite` does in the
running application. That near-miss is recorded in the audit report as explicitly *not* a
finding, because it would have been a fabricated defect.

**`-wal` and `-shm` belong to a specific `.db`.** Restoring a database by copying a `.db` over
a live one while its siblings remain is how you get a corrupt or silently stale database. The
audit's backup had a **0-byte** `-wal`, which proved it was fully checkpointed and the `.db`
alone was complete; a non-empty one would have had to be copied too. A read-only connection
recreates `-shm`/`-wal` afterwards, so their reappearance is not evidence of a problem.

**A code fix to a configuration *template* cannot fix an existing installation.**
`DefaultConfiguration` only writes an `appsettings.json` that does not exist yet. Changing a
default there fixes the next installation and no current one. Say so in the report, and do not
silently rewrite an operator's configuration file to make a claim true.

**Assertions inherit defaults.** `hardware-smoke.ps1` never set `DriverType`, so it inherited
the shipped default and asserted the *simulator* — passing on a 328-byte generated JPEG while
believing it had verified a camera. A test that does not pin the configuration it depends on is
testing whatever the product happens to default to.

**Do not use `PublishSingleFile` for this application.** In single-file mode
`Assembly.Location` is empty, and `ApplicationInfoService.ReadBuildDate` falls back to
`DateTime.Now` — so every terminal would report *today* as its build date, in the one field
you check when diagnosing a site. `scripts/publish-app.ps1` publishes a self-contained folder
plus a shortcut instead, which is a double-click either way.

## What not to change

- Components 1–11. They are complete, tested and verified. Extend; do not rewrite.
- The `Weighment` aggregate's state machine and its private setters. Transitions are methods
  for a reason: an invalid sequence must not be assemblable by property assignment.
- Migration `20260815104634_AddVehicleEntry`. **Never edit an applied migration** — add a new
  one. Editing it leaves every existing database on a schema no migration describes.
- The fixed pipeline stage order, and the health-monitoring split — see above.
- `RelayCommand` / `AsyncRelayCommand`. They stay as lightweight UI commands. A ViewModel
  command may *delegate into* `ICommandExecutor`; it must not be replaced by it.
- The layering. No business logic in Views, no SQL in ViewModels, no hardware access from
  Views. `VehicleEntryViewModel` is the reference: it holds no `DbContext` and no SQL.
- One window. No window switching — everything hosts inside the shell's content area.
- No `MessageBox`. Use `IDialogService`.
- No service locator, no static mutable state, constructor injection everywhere.
- Do not add NuGet packages without a concrete reason. Do not add web technologies.

## Build, test, verify

```bash
dotnet build WeighBridge.sln --nologo -v q     # expect 0 warnings, 0 errors
dotnet test --nologo                           # expect 531 passed, 0 failed, 0 skipped
dotnet run --project src/WeighBridge.App
```

The build is expected to stay warning-clean. A new warning is a defect.

If the build fails with MSB3021/MSB3027 file locks, a previous run left the application open:
`taskkill //F //IM WeighBridge.App.exe`.

Runtime verification — mandatory, and **compilation is not verification**:

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/runtime-smoke.ps1
echo $?     # must be 0 — shell regression

powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vehicle-entry-smoke.ps1
echo $?     # must be 0 — the business workflow, end to end
```

The first drives startup, all seven navigations (by log count delta), theme toggle, status
refresh, a background tick, graceful shutdown, and the absence of error-level log entries.

The second **deletes the database** and drives the real Vehicle Entry screen: open a weighment,
refuse an empty weight, record both weights, refuse an impossible tare, complete, cancel one
through the confirmation dialog (declining first), then restart the application and check the
work survived. It writes test weighments — do not run it on a machine holding real data.

`scripts/uia-dump.ps1` dumps the automation tree when an element cannot be found.

Both scripts save screenshots to `%TEMP%\weighbridge-*.png`. **Look at them.** Exit code 0
means every assertion passed, not that the screen is right.

Log file:

```
%LOCALAPPDATA%\WeighBridge Modern\Logs\weighbridge-{date}.log
```

Database file — delete it (with its `-wal`/`-shm` siblings) to get back to a first launch:

```
%LOCALAPPDATA%\WeighBridge Modern\Data\weighbridge.db
```

A clean shutdown ends with `==== Shutdown complete ====`. If that line is missing, shutdown
threw and preferences may not have been saved.

Note: the machine clock and the assistant's reported date have been observed to disagree.
Resolve `{date}` from the newest file on disk, not from what you believe today is.

## The installed application, and how to refresh it

There is a real double-clickable installation, produced by
`scripts/publish-app.ps1` (added 2026-08-18, phase 23 of the audit):

```bash
powershell -ExecutionPolicy Bypass -File scripts/publish-app.ps1
```

| | |
|---|---|
| Executable | `%LOCALAPPDATA%\Programs\WeighBridge Modern\WeighBridge.App.exe` |
| Shortcut | `<Desktop>\WeighBridge.lnk` — rewritten on every publish, so it follows a moved install |
| Data | `%LOCALAPPDATA%\WeighBridge Modern` — **never touched by the script** |
| Size | ~154 MB: `-r win-x64 --self-contained`, so a site PC needs no .NET runtime installed |

Re-run the script to bring the installed app up to date with the source. It refuses to
overwrite a running instance unless given `-Force`, because the operator may be mid-weighment.
The published build and a Debug build **share the same data root** — the same database, users
and settings — so a smoke script that stashes the database affects both.

Both audits can be pointed at the published build instead of the Debug one:

```bash
powershell -ExecutionPolicy Bypass -File scripts/final-application-smoke.ps1 -ExePath "$LOCALAPPDATA\Programs\WeighBridge Modern\WeighBridge.App.exe"
powershell -ExecutionPolicy Bypass -File scripts/navigation-audit.ps1 -ExePath "..."
```

Not verified, and worth doing before any site install: **installation on a machine that has
never had .NET or this application on it.** Everything above was verified on the development
machine.

## The state of this specific machine (2026-08-18)

Recorded because it is not derivable from the repository, and because two of these will
produce wrong data if not changed:

1. `%LOCALAPPDATA%\WeighBridge Modern\appsettings.json` predates the audit. It still has
   `Hardware:WeightIndicator:DriverType = "Simulator"` and `Camera:Enabled = true`, so this
   installation **generates its weights and its capture images** (F-027, F-028). Change those
   two keys before weighing anything real. It also has **no `Security` section at all**, which
   is why `SecurityOptions.DefaultRole`'s code default was what actually applied (F-014).
2. The database was restored to its pre-audit contents after the audit finished: weighments
   `WB-000001` (MH12AB1234), `WB-000002` (MH14CD5678), `WB-000003` (MH99ZZ0001), and a single
   `admin` user. `integrity_check: ok`, `foreign_key_check` clean, 4 migrations applied.
   **The `admin` password is therefore whatever it was before the audit began**, not what any
   audit script set. If it is unknown: there is no reset path — first-run administrator setup
   only runs when the `Users` table is empty.
3. The post-audit database was kept rather than discarded, at
   `%LOCALAPPDATA%\WeighBridge Modern\_audit-backup\weighbridge.db.post-audit`, so neither
   state was lost. The audit's `audit_readonly` probe account existed only in that one.
4. The two 328/329-byte generated JPEGs from F-028 are still in `Captures\` and still
   referenced by `WeighmentImages`. They are not photographs of anything.
5. Tooling: PowerShell **5.1** only (`pwsh` absent), no `sqlite3` CLI. Database inspection was
   done with python's bundled `sqlite3` over a read-only `file:…?mode=ro` connection. Python
   cannot resolve Git-Bash `/tmp` paths — pass Windows paths from `pwd -W`.

## Known unresolved issues

Every item here is a finding in [FinalAuditReport.md](FinalAuditReport.md) unless marked
otherwise. Ordered by what it costs to leave alone.

1. **No audit table (F-030, P1).** `IAuditLogger.Record(...)` is called on every master write,
   weighment transition, report and user-management action — and every one of those records
   goes to `%LOCALAPPDATA%\WeighBridge Modern\Logs\weighbridge-{date}.log` and nowhere else.
   The schema is `__EFMigrationsHistory, Weighments, Materials, Parties, VehicleTypes,
   Vehicles, WeighmentImages, Users`. So the trail is not queryable, it rolls on a 20 MB
   housekeeping policy, it sits in the profile of the account being audited, and `Audit.View`
   has nothing to show. This is the largest single gap in the product.
2. **No sign-out (F-022, P1).** A weighbridge terminal is shared across shifts and the only way
   to end a session is to close the application. The old `Logout()` was deleted rather than
   used: it had zero callers and its body installed a usable `ReadOnly` session named after the
   *Windows* account, which would have forged an identity into the audit trail.
3. **This machine's `appsettings.json` still selects the simulator and enables cameras**
   (F-027, F-028). See the previous section. Not a code defect — a configuration file that
   predates the fix.
4. **Only two foreign keys in the whole schema (F-031, P2).** `Weighments.PartyId`,
   `MaterialId`, `VehicleId` and `VehicleTypeId` are indexed and unconstrained. Nothing dangling
   exists today because master deletion is soft, so the invariant rests entirely on application
   code. Add the four constraints in the same migration as the audit table.
5. **`WeighBridge.App` and `WeighBridge.Printing` have no unit tests at all (F-010, P2), and
   this bounded the audit.** Both are `net8.0-windows`; the test project is `net8.0` and cannot
   reference them. `tests/WeighBridge.Tests/App/` is empty. The consequence is concrete: the
   reprint permission gate (F-026) **cannot** have a unit test, and its only regression net is
   `scripts/privilege-enforcement-check.ps1`. The 531 passing tests cover no view model, no WPF
   behaviour, and not the print service.
6. **`INotificationService` has no UI consumer.** Complete and registered, raises all four
   severities correctly, but there is no toast host in `WeighBridge.App` to render into.
   Deliberately deferred. Vehicle Entry works around it with an on-screen status line, which is
   why that line is the operator's only report of success or failure.
7. **Administration bypasses the command/service pattern (F-011, P2).** User management writes
   through a repository from the view model instead of through `ICommandExecutor`, which is why
   its permission checks had to be added by hand (F-002) rather than being enforced by the
   pipeline like every other operation.
8. **`Settings.Edit` has no capability behind it (F-006, P3).** The Settings screen exposes
   exactly one combo box (theme) and one check box (collapsed navigation) — confirmed by
   enumerating the module's controls over UIA. Hardware, camera and printer are read-only text.
   The permission implies an editing capability that does not exist.
9. **Three smoke scripts delete the operator's live database (F-012, P2).** The audit's own
   scripts move it aside and put it back; `vehicle-entry-smoke.ps1`, `masters-smoke.ps1` and
   `hardware-smoke.ps1` still delete. Do not run them on a machine holding real data.
10. **No `AutomationProperties.Name` in 5 of 7 views (F-009, P2)**, and capture filenames mix
    UTC and local time (F-016, P3).
11. **All work is uncommitted** on top of `3077420` — including the audit's fixes, this file,
    `ImplementationStatus.md` and `FinalAuditReport.md`.


## Next steps

**Components 12–14 were skipped on purpose, and the reason should survive this handoff.** The
decision (2026-08-15) was to prove the eleven finished components against a real business module
rather than build three more. Vehicle Entry needed no workflow engine, no module registry and no
plugin host — that is the finding. Build them when a module actually needs one, not because they
were once numbered 12, 13 and 14.

Prompts 8, 9 and 10 are done: hardware, masters, printing, duplicate slip, reports,
administration. **The next work is not a new module.** In order:

1. **The audit table, with the four missing foreign keys in the same migration** (F-030, F-031).
   An entity, a migration, a write path behind `IAuditLogger.Record`, and enough of a viewer to
   make `Audit.View` mean something. This is what stands between the product and any site with a
   dispute-resolution or compliance requirement — which is most of them, since a weighbridge slip
   is a commercial document.
2. **Sign-out** (F-022), because the terminal is shared across shifts and everything after a
   shift change is currently attributed to whoever signed in first.
3. **A `net8.0-windows` test project** for the App and Printing layers (F-010). Until it exists,
   two security controls are provable only by PowerShell UIA scripts, and deleting a script
   deletes the only thing that would catch a regression. Note the deliberate constraint this
   relaxes: the headless test project was a forcing function to keep logic out of the UI. Add a
   *second, windows-targeted* project for what genuinely cannot be headless; do not retarget the
   existing one.
4. **The rest of the audit's open findings**, in the order given in
   [Known unresolved issues](#known-unresolved-issues).

And the phases the audit did not run — Vehicle Entry criterion by criterion, master CRUD, report
*content* correctness, dashboard figures, audit-log content, concurrency (two instances against
one database was never tried), UI/UX and performance. They are listed as not run in
[Outstanding](FinalAuditReport.md#outstanding) rather than left looking covered.

Whatever is chosen, the operating pattern is:

**Inspect → Plan → Implement → Test → Debug → Verify → Document → Report**

And the audit's own rule, which is worth keeping: **reproduce before fixing.** A defect that was
never reproduced was never understood, and the fix for it cannot be verified. Every finding in
`FinalAuditReport.md` has a reproduction, and the ones that could not be reproduced were
downgraded or retracted — including one of the auditor's own.

Documentation is part of the definition of done. Update
[ImplementationStatus.md](ImplementationStatus.md) and this file as part of the work, with
real measured numbers. Do not fabricate dates, test counts or runtime results, and do not
claim runtime verification that was not performed. **And do not trust the smoke tests**: four
separate instances were found during the audit of a script reporting a pass that the
application did not earn.
