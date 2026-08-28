# WeighBridge Modern — Project Reference

The complete picture of this codebase in one file: what it is, how it is built, what runs
where, what is verified and what is not. Written to be the first thing a new person reads.

Last updated 2026-08-27, against a clean build with 618 passing tests.

---

## 1. What the product is

A Windows desktop application for a **truck weighbridge**. A vehicle drives onto a platform,
a weight indicator reports its weight over a serial port, an operator records the load, and
the application prints a weighment slip and keeps a permanent record.

The essential domain concept is the **two-pass weighment**:

| Pass | What happens | Stored as |
|------|--------------|-----------|
| First weighment | Vehicle arrives, is weighed loaded or empty | `Weighment` with one `WeightCapture` |
| Second weighment | Vehicle returns after loading/unloading, weighed again | Same `Weighment`, second `WeightCapture` |
| Net | `Gross − Tare`, computed, never typed | Derived on the `Weighment` |

A weighment with only one pass is **open**; one with both is **complete**. `WeighmentStatus`
carries this. The slip number (`SlipNumbers`) is the operator-facing identifier and is unique
per non-deleted record.

---

## 2. Solution layout

Nine projects, `.NET 8`, WPF for the UI. Dependencies point inward — `Domain` references
nothing, `App` references everything.

```
src/
  WeighBridge.Domain          entities, enums, invariants. No dependencies at all.
  WeighBridge.Core            abstractions, MVVM primitives, dialog/permission contracts.
  WeighBridge.Infrastructure  EF Core + SQLite, DbContext, migrations, repositories.
  WeighBridge.Services        business services: weighments, masters, auth, status, audit.
  WeighBridge.Hardware        serial weight indicator, simulator, COM port detection, camera.
  WeighBridge.Printing        slip rendering and printer output.
  WeighBridge.Reporting       CsvReportService.
  WeighBridge.Settings        appsettings.json + user preferences persistence.
  WeighBridge.App             WPF shell, views, view models, DI composition root.
tests/
  WeighBridge.Tests           618 tests across 60 files. xUnit.
scripts/                      17 PowerShell runtime/UIA verification scripts.
Documentation/                this folder.
```

`Directory.Packages.props` centrally pins every NuGet version. Individual `.csproj` files
carry no version numbers.

### Layering rules that are actually enforced

- **`Domain` has no dependencies.** Not even EF Core. Entities protect their own invariants
  through methods (`User.Create`, `user.ChangePassword`, `user.Deactivate`) rather than public
  setters.
- **`App` knows nothing about Entity Framework.** Where a database concern has to surface in
  the UI layer it is matched on message, not on type — see
  `AdministrationViewModel.IsDuplicateUsername`, which walks the `InnerException` chain
  looking for `UNIQUE` + `Username` instead of catching `DbUpdateException`.
- **No `MessageBox` anywhere.** Every prompt goes through `IDialogService`.
- **One window.** Modules are views swapped inside the shell, never separate windows.
  Dialogs are the only exception.

---

## 3. Domain model

```
Domain/Masters      Material, Party, Vehicle, VehicleType
Domain/Security     User
Domain/Weighments   Weighment, WeightCapture, WeighmentImage, SlipNumbers
Domain/Enums        ApplicationModule, CameraSource, ConnectionState,
                    WeighmentMode, WeighmentStatus, WeightSource
Domain/Common       shared base types, auditing fields
```

`WeightSource` is worth calling out: it records whether a weight came from the **indicator**
or was **entered by hand**. A slip must be able to say which, because a hand-typed weight on
a commercial document is a different thing from a measured one.

### Database

SQLite, via EF Core. Five migrations, applied in order at startup:

| Migration | Adds |
|-----------|------|
| `20260815104634_AddVehicleEntry` | `Weighments`, `WeightCaptures` |
| `20260815160237_AddMasters` | `Vehicles`, `VehicleTypes`, `Materials`, `Parties` |
| `20260815164609_AddWeighmentImages` | `WeighmentImages` |
| `20260815173101_AddUsers` | `Users` |
| `20260826115121_HardenConstraintsAndAuditTrail` | unique indexes, check constraints, `AuditEntries` |

The last migration is the important one for data integrity. It carries the **filtered unique
indexes** that make uniqueness a database guarantee rather than a hopeful application check:

```
HasIndex(u => u.Username).IsUnique().HasFilter("IsDeleted = 0")
```

The filter is what makes soft delete and uniqueness coexist — a deleted `admin` must not
block a new `admin`, but two live ones must be impossible.

**Check-then-insert is not an invariant.** Every uniqueness-sensitive save in this codebase
does the friendly pre-check *and* handles the constraint violation, because between the check
and the insert another terminal can win. Masters route this through
`UniquenessGuardedSave`; users through `AdministrationViewModel.IsDuplicateUsername`.

---

## 4. Where things live at runtime

| What | Path |
|------|------|
| Installed application | `%LOCALAPPDATA%\Programs\WeighBridge Modern\WeighBridge.App.exe` |
| Desktop shortcut | `%USERPROFILE%\OneDrive\Desktop\WeighBridge.lnk` |
| Data root (default) | `%LOCALAPPDATA%\WeighBridge Modern` |
| Database | `<data root>\Data\weighbridge.db` |
| Logs | `<data root>\Logs\weighbridge-yyyy-MM-dd.log` |
| Configuration | `<data root>\appsettings.json` |
| User preferences | `<data root>\userpreferences.json` |
| Camera captures | `<data root>\Captures` |
| Exported reports | `<data root>\Reports` |

**`WEIGHBRIDGE_DATA_ROOT`** overrides the data root. This is the mechanism every test script
uses to stay away from live data, and it is the single most important thing to know before
running anything in `scripts/`.

### Publishing

```
powershell -ExecutionPolicy Bypass -File scripts\publish-app.ps1 [-Force]
```

Self-contained `win-x64` Release build (~154 MB, runtime included), installed to
`%LOCALAPPDATA%\Programs\WeighBridge Modern`, with the desktop shortcut rewritten to match.
The script refuses to run while the application is open unless `-Force` is passed, and it
never touches the data folder.

Deliberately **not** `PublishSingleFile`: single-file publishing leaves `Assembly.Location`
empty, and `ApplicationInfoService` reads it to report the build date. Under single-file every
terminal would report today's date as its build date.

---

## 5. Application startup and session lifetime

This is the part most likely to surprise someone, so it is described exactly.

`App.OnStartup` runs the bootstrapper, then calls **`RunSessionsAsync()`** — a loop, not a
single sign-in:

```
while (true):
    show the login dialog
    if cancelled or failed  -> return (process ends)
    build a NEW shell (MainWindow + MainWindowViewModel are TRANSIENT)
    show it, wait for its Closed event
    if it closed for any reason other than sign-out -> return (process ends)
    clear Application.MainWindow, loop back to the login dialog
```

Three consequences, each load-bearing:

1. **`ShutdownMode` is `OnExplicitShutdown` for the whole process lifetime.** Under
   `OnMainWindowClose`, the login dialog would become `MainWindow` during startup, and a
   sign-out — which closes the shell to get back to the login dialog — would end the process.
   `RunSessionsAsync` returning is what decides the application is finished; it then calls
   `Shutdown(0)`.

2. **The shell is transient, not a singleton.** `MainWindowViewModel` filters the navigation
   rail by permission *in its constructor*. A shell held for the process lifetime would show
   the first operator's modules to everyone who signed in after them.

3. **Singletons must not assume one window per process.** `WindowPlacementService.Attach`
   used to throw on a second call; that latent throw only became reachable once a session loop
   existed, and would have turned the first sign-out into a failed startup with exit code −1.
   It now detaches the previous window's handlers and re-attaches.

`MainWindow.SignOutRequested` is a **property, not an event**. Multicast delegates fire in
subscription order, so a second subscriber registered after the window's own handler would be
reached only after `Window.Closed` had already fired. `App` needs to read it at a known point.

---

## 6. Security

| Concern | How it works |
|---------|--------------|
| Password storage | PBKDF2, salted. Legacy unsalted SHA-256 hashes are accepted once through a controlled migration path and re-hashed. |
| First run | **No account is seeded.** A database with no users shows an *Administrator setup* form that appoints the first administrator. There is no fixed known password anywhere in the product. |
| Pre-login state | `Roles.Unauthenticated`, which grants nothing. No unauthenticated process state carries an operational role. |
| Lockout | Failed sign-ins are counted; `SecurityOptions.EffectiveMaxFailedSignIns` locks the account. **A lockout survives a sign-out** — otherwise signing out would be the way round it. |
| Inactive users | Cannot sign in. |
| Enforcement point | The command/service layer, via `IPermissionService.Authorize`. Hidden UI controls are a hint, never the control. `AdministrationViewModel.AuthoriseUserManagementAsync` gates every user write. |
| Audit trail | `AuditEntries` table plus log entries, stamped with the authenticated operator — not the Windows account. |
| Secrets | Not stored in `appsettings.json`; never logged. |

### Sign-out

`IAuthenticationService.SignOut()` writes the `SignedOut` audit entry **before** clearing the
identity, because the audit store stamps entries with the signed-in operator. Clearing first
would attribute the sign-out to the Windows machine account — a wrong record, which is worse
than no record. `IPermissionService.SignOut()` then rebuilds the pre-login identity
(Windows account name, `Roles.Unauthenticated`) rather than nulling the current one, so the
terminal still names who is standing at it.

---

## 7. Hardware

| Device | State |
|--------|-------|
| Serial weight indicator | Implemented. `DriverType: Serial` reads a real indicator. |
| COM port auto-detection | Implemented. Settings → **Detect Indicator** listens on every serial port and adopts the one sending readable weight frames. **Refresh** re-reads the port list Windows reports. Both controls are hidden unless the driver is Serial. |
| Simulator | Implemented. `DriverType: Simulator` invents weights. |
| Camera | Implemented, writes to `<data root>\Captures`. |
| Printer | Implemented via `WeighBridge.Printing`. |

**Provenance — read this before claiming anything works.** The default `DriverType` is
`Serial`. `Simulator` is not the default, deliberately: a fresh installation must not weigh
vehicles with generated numbers. Every runtime script that needs readings *asks* for the
simulator by name.

No physical indicator, printer, or camera has been connected and tested in any session to
date. Serial parsing, decimal handling, and detection logic are covered by unit tests and by
the simulator. **That is simulator-verified, not hardware-verified.** Do not describe it
otherwise.

---

## 8. The seven modules

`ApplicationModule` — Dashboard, Vehicle Entry, Duplicate Slip, Reports, Masters, Settings,
Administration. Each is a view swapped into the shell; the navigation rail is filtered by the
signed-in operator's permissions.

| Module | Purpose |
|--------|---------|
| Dashboard | Live weight, subsystem status, counts. Subscribes to the indicator on activation and releases on exit. |
| Vehicle Entry | The core workflow: first and second weighment, slip issue. |
| Duplicate Slip | Reprint an existing slip. |
| Reports | Query weighments, export CSV. |
| Masters | CRUD for Vehicles, Vehicle Types, Materials, Parties. |
| Settings | Preferences and hardware configuration, including COM detection. |
| Administration | User accounts, roles. Every write gated by `UsersManage`. |

**Settings never writes JSON directly from the ViewModel.** The path is
`SettingsView → SettingsViewModel → ISettingsService / IConfigurationWriter → persistence`.
`ConfigurationProvisioner.MergeMissingKeys` makes partial `appsettings.json` writes safe, and
an options object is never serialised wholesale over the file — that would silently overwrite
keys the running build does not know about.

---

## 9. Verification

### Unit tests

```
dotnet build WeighBridge.sln --nologo -v q     -> 0 warnings, 0 errors
dotnet test  WeighBridge.sln --nologo -v q     -> 618 passed, 0 failed, 0 skipped
```

`WeighBridge.Tests` does **not** reference `WeighBridge.App`. Nothing in the WPF layer —
views, view models, the session loop, XAML resource lookups — can be reached by a unit test.
That gap is why `scripts/` exists.

### Runtime scripts

17 PowerShell scripts driving the real application through UI Automation. All of them run
against an **isolated temporary data root** (`New-IsolatedDataRoot` sets
`WEIGHBRIDGE_DATA_ROOT`, which `Start-Process` inherits), so no script can touch live data.
`Assert-NotLiveDataRoot` is the backstop before any deletion.

Key scripts:

| Script | Proves |
|--------|--------|
| `final-application-smoke.ps1` | Launch, login, shell survives, clean exit 0, no error-level log entries. |
| `startup-cycle-audit.ps1` | 10 startup/shutdown cycles (clean + existing database), each exit 0. |
| `navigation-audit.ps1` | All 7 modules, 3 rounds each, every navigation log-confirmed; Settings is editable; COM detection controls appear when the driver is Serial and are hidden when it is not; clean shutdown. |
| `sign-out-check.ps1` | The sign-out session loop end to end. **Currently failing — see §10.** |
| `corrupt-database-startup.ps1` | Startup against a corrupt database. |
| `privilege-enforcement-check.ps1` / `privilege-escalation-repro.ps1` | Permissions are enforced in the service layer, not just hidden in the UI. |
| `shell-identity-check.ps1` | The title bar names the operator, not the Windows account. |
| `masters-smoke.ps1`, `vehicle-entry-smoke.ps1`, `hardware-smoke.ps1`, `serial-absent-log-check.ps1` | Module-level workflows. |

Results from the most recent run (2026-08-27):

```
final-application-smoke.ps1   PASS  exit 0
startup-cycle-audit.ps1       PASS  10/10 cycles, exit 0, shell in 3681-5225 ms
navigation-audit.ps1          PASS  7 modules x 3 rounds, exit 0
sign-out-check.ps1            FAIL  exit 1  (see §10)
```

### Test-the-tests

A script that cannot fail is not evidence. `navigation-audit.ps1` was verified by breaking the
invariant it guards: `Content="Detect Indicator"` was temporarily changed to
`"Detect Indicatorz"`, the application rebuilt, and the audit reported
`Settings has no 'Detect Indicator' button once the driver is Serial` with exit code 1. The
defect was then reverted and confirmed byte-identical by MD5. Exit codes in these scripts are
real; none is masked.

---

## 10. Known open defect — confirmation dialogs do not display

**Severity: high. Found 2026-08-27. Not yet fixed.**

`IDialogService.ShowConfirmationAsync` returns `false` without ever displaying a dialog. Every
"Are you sure?" prompt in the application therefore silently behaves as though the operator
had cancelled.

Reproduction: `scripts\sign-out-check.ps1` (exit 1).

Evidence gathered:

- The sign-out button is present, `IsEnabled = True`, exposes `InvokePattern`, has a clickable
  point at the expected position.
- `SignOutAsync` **is** entered — confirmed with a temporary log statement, since removed.
- **No exception is thrown.** `MainWindowViewModel.OnCommandFailed` calls
  `_logger.LogError` as its first statement, and `AsyncRelayCommand` routes every exception
  to it. Both the sign-out script and the navigation audit scan the run's log for
  `[ERR|EROR|CRIT|FATAL]` and found none, so a swallowed `XamlParseException` from
  `new MessageDialog(...)` is ruled out.
- Every `StaticResource` the dialog needs exists: `Icon.Information`, `Icon.Success`,
  `Icon.Warning`, `Icon.Error` and `Icon.Question` are all declared in `Icons.xaml:37-41`.
  `Icon.Question` is used only by the `Question` severity, so it was the obvious suspect, and
  it is present.
- `DialogWindowBase` assigns `DialogResult = false` in exactly one place — `Key.Escape` while
  `CanDismissWithEscape` — so something setting the result is either that key path or
  `MessageDialog.OnCancelClicked`.
- No dialog window ever appears in the UI Automation tree.
- The UI thread is **not** blocked: the theme toggle beside it still works seconds later, so
  `ShowDialog()` returned promptly rather than opening modally.
- The theme toggle in the same panel with the same binding style works, so the button's
  command binding and the panel's `DataContext` are both fine.

Therefore `ShowDialogWindow(new MessageDialog(viewModel))` is returning a non-`true` result
immediately. `MessageDialog`, `MessageDialogViewModel` and `DialogWindowBase` were all read
and contain no auto-close path; `OnKeyDown` dismisses on `Escape` only, and nothing sends one.

**This is pre-existing, not a regression from the sign-out work.** No script before
`sign-out-check.ps1` had ever exercised a confirmation dialog — `grep -l ConfirmButton
scripts/*.ps1` matches only that one file. The defect has simply never been reachable by
automated verification until now.

Not yet ruled out: `DialogService.ResolveOwner()` returning an owner that makes `ShowDialog()`
return immediately when the application is in the background (no window satisfies
`IsActive && IsLoaded`), and a `DataTrigger` on `DialogSeverity.Question` in
`MessageDialog.xaml` — `Question` is the severity confirmations use and no other prompt does.
Start there.

Impact: sign-out, Masters delete, Administration disable-user, and every other confirmed
destructive action will silently do nothing. The service layer beneath them is correct and
unit-tested; it is the prompt that fails.

---

## 11. Deliberate omissions

Stated rather than quietly dropped:

- **Audit viewer UI.** The audit trail is written to the `AuditEntries` table and to the log,
  and is unit-tested. There is no screen for reading it back. Adding one is new product
  functionality, which the repair passes explicitly excluded.
- **Notification toast host.** `INotificationService` works and is tested; no UI consumer
  displays its output yet. Deliberate, not a defect.
- **`ViewLocator.Register<TViewModel, TView>()`** has no callers. It is kept because
  `ViewLocator`'s own exception message directs the next developer to it; deleting the method
  would make that message a lie.

---

## 12. Working on this codebase

```bash
dotnet build WeighBridge.sln --nologo -v q
dotnet test  WeighBridge.sln --nologo -v q

# Runtime verification (isolated data root, safe)
powershell -ExecutionPolicy Bypass -File scripts/final-application-smoke.ps1 \
  -ExePath "…/src/WeighBridge.App/bin/Debug/net8.0-windows/WeighBridge.App.exe"

# Publish + refresh the desktop shortcut
powershell -ExecutionPolicy Bypass -File scripts/publish-app.ps1 -Force
```

House rules that are not negotiable, because each one was learned from a real failure:

1. **The running software is the source of truth.** A green test suite proved nothing about
   the confirmation dialog in §10 for the entire life of the project.
2. **Reproduce before fixing.** §10 was characterised with a probe before a line was changed —
   and the first hypothesis (a dead command binding) turned out to be wrong.
3. **Isolation beats restoration.** Test scripts get a temporary data root; they never back up
   and restore the live one.
4. **No vacuous assertions.** `if (Test-Path $log) { … }` is a false pass. Assert, don't guard.
5. **Every exit code is real.** No masking, no ignored child failures. When piping a script's
   output in bash, read `${PIPESTATUS[0]}` — `$?` gives you `tail`'s status, not the script's.
6. **Never claim hardware verification without hardware.** See §7.
