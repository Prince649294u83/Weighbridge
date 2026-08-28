# WeighBridge Modern — Forensic Audit Report

**Status: audit and repairs complete. The application is not production-ready, and this
report says why.**

Audit started 2026-08-16 and finished 2026-08-18, against the working tree on top of commit
`3077420` ("Baseline: Phase 0.5 Components 1-5 verified"). Every prior claim of "complete",
"passed" and "verified" was treated as untrusted until independently reproduced.

32 findings. 22 fixed, each fix verified against the running application; 10 open, each
recorded with what it costs to leave open. What still blocks production use is listed in
[Before this is used for real work](#before-this-is-used-for-real-work); what this audit did
not examine is listed in [Outstanding](#outstanding). Neither list is a formality —
[F-030](#f-030) (no audit table) and [F-027](#f-027)/[F-028](#f-028) (this installation is
still on the simulator, still filing generated JPEGs) are each on their own sufficient reason
not to weigh a real load on it.

## Method

Findings are only recorded here after being reproduced against the running Windows
application. For each: reproduction steps, the evidence collected, the root cause, the
fix, and how the fix was verified. Where a fix could only be proven by removing the
UI-level guard that hides the defect, that removal is stated explicitly, together with
the fact that it was restored.

Environment for all runtime evidence in this report:

| | |
|---|---|
| Machine | PRINCE, Windows 11 Pro 10.0.26200 |
| Build | `dotnet build` Debug, `net8.0` / `net8.0-windows`; and `dotnet publish -c Release -r win-x64 --self-contained` for phase 23 |
| Executable | `src\WeighBridge.App\bin\Debug\net8.0-windows\WeighBridge.App.exe` |
| Published executable | `%LOCALAPPDATA%\Programs\WeighBridge Modern\WeighBridge.App.exe` (phase 23 only) |
| Data root | `%LOCALAPPDATA%\WeighBridge Modern` |
| Logs | `%LOCALAPPDATA%\WeighBridge Modern\Logs\weighbridge-2026-08-16.log` through `-2026-08-18.log` |
| Database | `%LOCALAPPDATA%\WeighBridge Modern\Data\weighbridge.db` |
| Pre-audit backup | `%LOCALAPPDATA%\WeighBridge Modern\_audit-backup\` |

Tooling constraints that shaped the evidence gathering, recorded because they limit what
can be re-verified later: only PowerShell 5.1 is present (`pwsh` is not installed), so a
script cannot load the .NET 8 `Microsoft.Data.Sqlite.dll`, and there is no `sqlite3` CLI.
Early database assertions are therefore made with `grep -a` against the `.db` file, which is
valid for row existence because SQLite stores text as UTF-8 in the file itself; the phase-26
schema and pragma work uses python's bundled `sqlite3` module over a **read-only**
(`file:…?mode=ro`) connection, which is why one pragma reading in that section is explicitly
recorded as *not* a finding. Where an exit code is reported below it was measured **without**
a pipe: in bash `$?` after a pipeline reports the last element's status, so
`powershell … | tail` reports `tail`'s exit code and silently masks a failing script.

## Severity

| | |
|---|---|
| **P0** | Data loss, silent failure, security bypass, or the application does not work. Blocks release. |
| **P1** | A shipped feature is wrong or a security control is absent. Blocks release. |
| **P2** | Real defect with a workaround, or a correctness risk not yet triggered. |
| **P3** | Cosmetic, stale documentation, inconsistency with no functional effect. |

## Finding index

| ID | Sev | Component | Summary | State |
|---|---|---|---|---|
| [F-001](#f-001) | P0 | Administration / Persistence | Every user-management write was silently discarded | **Fixed, verified** |
| [F-002](#f-002) | P1 | Administration / Security | Any authenticated operator could create an Administrator account | **Fixed, verified** |
| [F-003](#f-003) | P1 | Test automation | Shared login helper could not see the login dialog's fields | **Fixed, verified** |
| [F-004](#f-004) | P0 | Security | Fixed, known seeded credential `admin` / `admin123` | **Fixed, verified** |
| [F-005](#f-005) | P1 | Security | Unsalted single-round SHA-256 password hashing, three copies | **Fixed, verified** |
| [F-006](#f-006) | P3 | Settings / Security | `Settings.Edit` declared and granted, with no capability behind it | Reproduced, reclassified from P1 |
| [F-007](#f-007) | P2 | Security | 8 of 13 declared permissions are enforced nowhere | **Two fixed** ([F-025](#f-025), [F-026](#f-026)); remainder reclassified |
| [F-008](#f-008) | P2 | Administration / UI | Password typed into a plain `TextBox`, visible on screen | **Fixed, verified** |
| [F-009](#f-009) | P2 | UI / Accessibility | No `AutomationProperties.Name` in 5 of 7 views | Open |
| [F-010](#f-010) | P2 | Test infrastructure | `WeighBridge.App` has no unit-test coverage at all | Open, and it bounded this audit |
| [F-011](#f-011) | P2 | Architecture | User management bypasses the command/service pattern | Open |
| [F-012](#f-012) | P2 | Test automation | Three smoke scripts delete the operator's live database | Open |
| [F-013](#f-013) | P2 | Hardware / Camera | Camera reports connected when it is not | **Superseded by [F-028](#f-028), fixed** |
| [F-014](#f-014) | P2 | Security config | `SecurityOptions.DefaultRole` is `Administrator` | **Fixed, verified** |
| [F-015](#f-015) | P2 | Shutdown | Indicator released twice, after the shutdown marker | **Fixed, verified** |
| [F-016](#f-016) | P3 | Hardware / Camera | Capture filenames mix UTC and local time | Open, carried forward |
| [F-017](#f-017) | P3 | Documentation | Stale "until there is a login screen" remarks in three files | **Fixed** |
| [F-018](#f-018) | P2 | Test automation | The audit's own startup script returned a false pass, then a false failure | **Fixed, verified — and three more instances since** |
| [F-019](#f-019) | P2 | Dashboard / Navigation | Every Dashboard visit leaked a permanent weight-indicator subscriber and a DbContext | **Fixed, verified** |
| [F-020](#f-020) | P1 | Security / Audit trail | Every log and audit entry named the Windows account, not the operator who signed in | **Fixed, verified** |
| [F-021](#f-021) | P2 | Shell / UI | The title bar showed the Windows account, not the operator | **Fixed, verified** |
| [F-022](#f-022) | P1 | Security | No way to sign out; `Logout()` was dead code that would have forged an identity | Dead code removed; **the capability is a recorded gap, not built** |
| [F-023](#f-023) | P1 | Security / Data | A zero-byte database file was treated as a new installation: migrated over, then first-run administrator setup offered | **Fixed, verified** |
| [F-024](#f-024) | P2 | Startup | A failed database initialisation did not stop startup; three doc comments described an unreachable degraded mode | **Fixed, verified** |
| [F-025](#f-025) | P1 | Reporting / Security | `Reports.Export` enforced nowhere: any signed-in account could write the site's whole weighment history to a file | **Fixed, verified at runtime** |
| [F-026](#f-026) | P1 | Printing / Security | `Weighment.Reprint` enforced nowhere: any signed-in account could reissue any slip | **Fixed, verified at runtime** |
| [F-027](#f-027) | P1 | Hardware / Data integrity | The shipped default weight source was the **simulator**, and any unrecognised driver type fell back to it silently | **Fixed, verified** |
| [F-028](#f-028) | P1 | Hardware / Evidence | Generated JPEGs were filed against weighments as vehicle photographs, with cameras configured off | **Fixed, verified** |
| [F-029](#f-029) | P2 | Hardware / Logging | An absent serial port wrote ~170,000 log lines a day, rolling away the 30 days of history the site is configured to keep | **Fixed, verified** |
| [F-030](#f-030) | P1 | Audit trail | There is **no audit table**. The audit trail exists only in a rolling text log; `Audit.View` has nothing to show | Open — recorded, not built |
| [F-031](#f-031) | P2 | Database schema | Only 2 foreign keys in the whole schema; all four of a weighment's master references are unconstrained | Open |
| [F-032](#f-032) | P2 | Reporting / UI | "Show in Explorer" was handed the status message instead of the file path | **Fixed** |

---

## F-001

**P0 — every user-management write was silently discarded.**
Component: `WeighBridge.App/ViewModels/AdministrationViewModel.cs`,
`WeighBridge.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`

### Description

Creating, updating, disabling or enabling a user did nothing. No row was written, no
exception was raised, and the status bar reported success. The module then reloaded from
the database and displayed the unchanged list, so the only visible symptom was the success
message being replaced a moment later by "Loaded 1 users".

This is the defect that explains how a claimed 497 passing tests could coexist with a
completely non-functional module: see [F-010](#f-010).

### Reproduction

`scripts/privilege-escalation-repro.ps1` phase 1 — log in as the seeded administrator,
open Administration, click **+ Add User**, fill username / display name / password, pick a
role, click **Create**, then read the users grid.

### Evidence

Three independent sources, none of which relied on the application's own success message:

1. **Screen.** After the save the editor panel had closed and the status read
   `Loaded 1 users`; the users grid contained one row (`admin`). The script dumped every
   `Text` element to prove the new username appeared nowhere outside the form fields.
2. **Log.** No line was written for the save at all — not a success, not a failure. (That
   absence is a second defect in itself; a security-relevant write left no trace. Fixed
   together with this one.)
3. **Database.** `grep -a 'audit_readonly' weighbridge.db` → no match, and the file's
   last-write time did not advance across two consecutive attempts (frozen at 11:17).

### Root cause

`WeighBridgeDbContext` is registered `AddTransient`
(`InfrastructureServiceCollectionExtensions.cs:55`). `AdministrationViewModel` injected
`IRepository<User>` **and** `IUnitOfWork` side by side, so the container handed it two
different contexts and therefore two different EF Core change trackers. Every write was
staged on the repository's tracker and committed through the unit of work's tracker.
`SaveChangesAsync` found nothing to save, returned `0`, and threw nothing. The return
value was not checked.

Blast radius was scoped by grepping every direct `IRepository<T>` consumer: five in total,
of which only `AdministrationViewModel` performs writes. The four master and weighment
services already obtain their repository from `unitOfWork.Repository<T>()` and were never
affected.

### Fix

`AdministrationViewModel` now takes `Func<IUnitOfWork>` — the pattern every masters and
weighment service already uses — and does `await using var unitOfWork = _unitOfWork();`
per operation, taking its repository from `unitOfWork.Repository<User>()` so the mutation
and the commit share one tracker. Disable and enable use `repository.Update(user)` on the
entity taken from the `Users` collection, which EF attaches as `Modified`. The DI
registration for `IRepository<>` now carries a comment stating that it is for read-only
consumers and why. Logging was added for create, disable and enable, matching
`VehicleService`'s existing format.

### Verification

- `dotnet build` — 0 warnings, 0 errors.
- Re-ran the reproduction: `audit_readonly` was created, appeared in the grid, and the
  database file's last-write time advanced to 11:50.
- Phase 2 of the same script then authenticated as `audit_readonly`, which cannot succeed
  unless the row exists.
- Two regression tests added in `tests/WeighBridge.Tests/Infrastructure/UnitOfWorkTrackerTests.cs`
  pin both halves of the rule: a repository taken from the unit of work saves 1 row, and a
  repository over its own context returns `SaveChangesAsync() == 0` with the row absent —
  the silent-zero shape itself is now asserted, so it cannot return unnoticed.
- `dotnet test` — 499 passed, 0 failed.

---

## F-002

**P1 — any authenticated operator could create an Administrator account.**
Component: `WeighBridge.App/ViewModels/AdministrationViewModel.cs`,
`WeighBridge.App/ViewModels/MainWindowViewModel.cs`

### Description

`Permissions.UsersManage` is declared and granted to `Administrator` alone, but nothing in
production code consulted it. A ReadOnly operator — the role intended for a gate terminal
that may only look at masters and reports — could open Administration and create a new
Administrator account, granting itself full privileges on the next login.

### Reproduction

`scripts/privilege-escalation-repro.ps1`. Its exit codes are deliberately inverted —
`0` = escalation blocked, `1` = escalation succeeded, `2` = inconclusive — because a
reproduction script that exits 0 while reproducing the bug is exactly the false-positive
shape this audit exists to remove.

Phase 1 creates a ReadOnly account as the administrator. Phase 2 logs in as that account,
navigates to Administration, and attempts to create an account with role Administrator.

### Evidence

Before the fix, the run reported:

```
[escalation]   a ReadOnly operator reached the Administration module
[escalation]   ESCALATED: a ReadOnly operator created an Administrator account
[escalation] REPRODUCED: user management enforces no authorisation.
```

and `grep -a` on the database confirmed the row `audit_escalated_admin` /
`Created By A ReadOnly User` with role `Administrator`.

Navigation was confirmed by a log-line count delta rather than by the navigation radio
button's checked state, because the command is bound to `Click` — a ticked radio button
proves nothing about what the content area actually shows.

### Root cause

Four independent gaps, all of which had to be closed:

1. `AdministrationViewModel` injected `IPermissionService` and never called it.
2. User management is the only write path with no `IApplicationCommand`, so it never
   reaches `CommandExecutor.cs:151` — the single place `_permissions.Authorize(command)`
   is called, and therefore the only place `IRequiresPermission` is enforced. See
   [F-011](#f-011).
3. The command-enablement predicates checked field contents only, so the buttons were
   enabled for every role.
4. `MainWindowViewModel` built the navigation rail as a fixed array with no permission
   filter, so Administration was offered to everyone.

### Fix

- `AuthoriseUserManagementAsync()` gates all four write operations. It calls
  `_permissions.Authorize(Permissions.UsersManage)`, logs a warning naming the operator
  and role on denial, sets a danger status and shows a "Not Permitted" dialog. It is
  called **inside** each operation, before any repository access — a disabled button is a
  hint, not a control.
- `HasPermission(Permissions.UsersManage)` added to the `CanExecute` of new-user, save,
  disable and enable, which is what `IPermissionService` documents `HasPermission` for.
- `MainWindowViewModel` omits the Administration navigation item when the operator lacks
  `UsersManage`. Evaluated once, which is correct because the shell is constructed only
  after a successful login (`App.OnStartup`). `ResolveStartupItem` already falls back when
  a remembered module is absent from the list, so a ReadOnly operator whose last module
  was Administration still opens cleanly.

### Verification

Two runs were needed, because a run that stops at the navigation rail proves only that the
UI hides the module — not that the write is refused.

**Run 1 — fixed code, exit 0:**

```
[escalation]   BLOCKED: Administration is not offered in the navigation rail to a ReadOnly operator
[escalation] Escalation blocked - UsersManage is enforced.
```

**Run 2 — write-path proof.** The two UI-level guards were *temporarily* removed (the
navigation filter, and the `HasPermission` checks in the `CanExecute` predicates), marked
`AUDIT-PHASE22-TEMPORARY`, leaving `AuthoriseUserManagementAsync` as the only remaining
defence. A ReadOnly operator then reached the module, found **+ Add User** enabled, filled
the form with role Administrator and clicked **Create**:

```
[escalation]   a ReadOnly operator reached the Administration module
[escalation]     text: Not Permitted
[escalation]     text: You do not have permission to manage users.
[escalation]     field Username: 'audit_escalated_admin'
[escalation]     field DisplayName: 'Created By A ReadOnly User'
[escalation]   BLOCKED: the save was refused
```

The users grid still listed only `admin` and `audit_readonly`. Corroborated outside the
UI:

- Log: `12:44:58.123 [WRN] AdministrationViewModel :: User management denied for audit_readonly as ReadOnly: You do not have permission to manage users.`
- Database: `grep -ac 'audit_escalated_admin' weighbridge.db` → `0`.

Both temporary removals were then reverted; `grep -rn AUDIT-PHASE22-TEMPORARY src/ tests/ scripts/`
returns nothing, and the restored build is clean. The final restored-state run exits 0.

`dotnet test` — 499 passed.

**Probe accounts:** the run leaves `audit_readonly` (and, when reproducing,
`audit_escalated_admin`) in the live database. Both were removed by restoring
`_audit-backup\weighbridge.db`; `grep -ac 'audit_' weighbridge.db` → `0`.

---

## F-003

**P1 — the shared login helper could not see the login dialog's fields, failing runs at random.**
Component: `scripts/login-helper.ps1`

### Description

`Invoke-WeighBridgeLogin` is dot-sourced by every runtime smoke script. It polled for a
top-level window titled `Login`, then looked up the `UsernameBox` field exactly once and
threw if it was absent. A run failed with
`Login dialog has no element with AutomationId 'UsernameBox'.`

### Reproduction

Observed live twice. First on a single launch of the escalation reproduction, which
succeeded on the next launch with no code change. Then, after the first attempted fix, on
cycle 7 of a 10-cycle startup run — which is what turned an unreproducible flake into a
measured rate of roughly 1 in 10.

### Evidence

The application was healthy in the failing cycle and the script was wrong, both times. For
cycle 7 the log shows startup completing normally and then nothing at all for the 15
seconds until the process was killed:

```
13:14:26.890 [INF] Bootstrapper :: ==== WeighBridge Modern v0.1.0 starting on PRINCE ====
13:14:27.639 [INF] DatabaseInitializer :: Database schema is up to date (4 migration(s) applied previously)
13:14:27.790 [INF] WeighBridge.Application :: Permission service started for dell as Administrator
   (nothing until the process is killed at 13:14:43)
```

`Permission service started` is the line immediately before the login dialog is shown, and
a modal dialog with nobody typing into it logs nothing. In the nine passing cycles of the
same run the next line, `Operator changed from dell to admin`, arrives about 0.9 s later.
No warning, no error, no exception: the dialog was up and the automation client could not
see inside it.

### Root cause

Two layers, and the first fix only addressed the outer one.

WPF sets a window's title before it has realised the window's content, so the poll can
return the dialog while its fields are still absent from the UI Automation tree. The window
wait had a deadline; the field lookup did not. **That was the first fix: poll the field to
its own deadline.** It was not sufficient, and cycle 7 proved it — 15 seconds is far beyond
content realisation.

The deeper cause is that WPF's automation peers cache their children. The peer created to
answer the script's own early query can keep answering with an empty tree, and nothing
invalidates it, so polling that one captured `AutomationElement` re-asks a handle that has
already committed to seeing nothing.

### Fix

`Find-Field` re-acquires the window from the desktop root on every pass and re-asserts
foreground, which makes the provider rebuild the tree rather than confirm a stale answer.
The deadline is 20 s and is a deadline, not a sleep: a sleep either wastes time on a fast
machine or reintroduces the race on a slow one. On expiry it now dumps the control types
and automation ids UIA can actually see, because an intermittent "field not found" with no
other evidence leaves the next reproduction as uninformative as the last.

### Verification

30 consecutive logins across three 10-cycle runs of `scripts/startup-cycle-audit.ps1`
(including the deliberately-defective run for [F-015](#f-015)) with no recurrence. A flake
by nature, so this is a rate, not a proof of absence: the measured rate before the fix was
1 in 10, and 0 in 30 after it.

---

## F-004

**P0 — the application shipped a fixed, known administrator password.** **Fixed, verified.**

### Description

`DatabaseInitializer` seeded `admin` with a hash of `admin123` on any database that had no
users, and that credential was published in this repository — in the initialiser itself and
in `scripts/login-helper.ps1`. Every installation that had never changed it shared one known
administrator account. Per the audit brief this alone means the application is not
production-ready.

### Reproduction

`scripts/startup-cycle-audit.ps1` cycle 1 starts with no database at all. Before the fix,
the log recorded `Seeded default administrator user (username: admin)` and the login dialog
then accepted `admin` / `admin123`. The credential was the default in `login-helper.ps1`, so
all five runtime smoke scripts depended on it.

### Root cause

The seed existed because the login dialog had no other way to produce a first account: the
dialog could only authenticate. Given a locked front door, the previous work shipped a key
under the mat rather than a way to fit a lock.

### Fix

The seed is deleted — `src/WeighBridge.Infrastructure/Persistence/DatabaseInitializer.cs`
creates no account, and the removal is commented so it is not helpfully restored. The login
dialog gained a second mode instead:

- `IAuthenticationService.RequiresInitialSetupAsync` reports whether the user table is
  empty. It asks for the absence of *any* row, not of an *enabled* row: "no usable account"
  would have been a backdoor, since disabling the last administrator would then let the next
  person to start the application appoint themselves one.
- `IAuthenticationService.CreateInitialAdministratorAsync` creates the administrator and
  signs it in. It re-checks that no account exists and enforces
  `IAuthenticationService.MinimumPasswordLength` itself: the dialog's mode is a presentation
  decision, not the control.
- `LoginDialogViewModel` resolves the mode in `InitializeAsync`, which
  `DialogService.ShowLoginAsync` awaits *before* the window is constructed, so the title,
  prompt, button label and confirmation field never change under the operator's hands — or
  under an automated script that identifies the dialog by its title.
- The dialog's title is `Administrator setup` in that mode and `Login` otherwise. That title
  is the only thing distinguishing the two modes from outside the process, and
  `login-helper.ps1` now branches on it. A helper that could not tell them apart would type
  a sign-in into a setup form and report a successful login.

The application therefore ships with no password at all. `AdministrationViewModel` also now
enforces the same minimum length, so the bar does not apply solely to the one account created
before the product is in use.

### Verification

- `tests/WeighBridge.Tests/Security/AuthenticationTests.cs`, 13 tests, all passing. Three
  are the direct regression: `FreshDatabase_HasNoAccountAndRequiresSetup` runs the real
  `DatabaseInitializer` and asserts the user table is empty afterwards;
  `InitialSetup_IsRefusedOnceAnAccountExists`; `InitialSetup_IsRefusedForATooShortPassword`.
  `Authenticate_AcceptsTheChosenPasswordAndRejectsAnyOther` also asserts
  `admin` / `admin123` is rejected, in case the seed is ever reintroduced.
- **The assertions were proved to bite** (phase 22). Reintroducing the seed under an
  `AUDIT-PHASE22-TEMPORARY` marker failed exactly one test —
  `FreshDatabase_HasNoAccountAndRequiresSetup`, 12 of 13 still passing. Disabling the
  duplicate-account guard failed exactly `InitialSetup_IsRefusedOnceAnAccountExists`. Both
  markers were removed, `grep -rn AUDIT-PHASE22` returns nothing, and the suite is green
  again.
- End to end against the running application: `scripts/startup-cycle-audit.ps1` exits 0 with
  10 of 10 cycles. Cycle 1 met the setup form and appointed the account; cycles 2–10 signed
  it in. The script asserts that count rather than assuming it, because setup running twice
  would mean the account never persisted — which from the outside looks like a healthy
  launch. The log agrees exactly once:

  ```
  17:22:00.479 Initial administrator account admin created during first-run setup
  17:22:00.480 User admin authenticated successfully as Administrator
  17:22:04.165 User admin authenticated successfully as Administrator   <- cycle 2
  ...
  17:22:31.096 User admin authenticated successfully as Administrator   <- cycle 10
  ```

- Cycle 1's time to shell rose from 3172 ms to 5617 ms and the warm cycles from a 2843 ms
  mean to 3476 ms. That is the cost of the extra dialog interaction and of PBKDF2
  (see [F-005](#f-005)) — deliberate, and paid once per sign-in.

### Residual

The smoke scripts still need *a* credential, and `login-helper.ps1` defaults to one
(`admin` / `WbAudit-Local-2026`). That value is created **by the script** into a test
database; nothing in the product knows it and nothing ships it. A database created before
this fix still holds accounts with their old passwords, so running a smoke script against
one means passing `-Username` and `-Password` for it.

There is no password *reset* path: an installation that loses its only administrator
password cannot recover it, only rebuild the database. That is a product decision for the
owner, not a defect this audit invented — recorded, not fixed.

---

## F-005

**P1 — passwords were hashed with unsalted, single-round SHA-256, in three separate copies.**
**Fixed, verified.**

### Description

`AdministrationViewModel.HashPassword`, `AuthenticationService.HashPassword` and the
`DatabaseInitializer` seed each implemented the same
`Convert.ToBase64String(SHA256.HashData(...))`. Unsalted single-round SHA-256 is not a
password hash: identical passwords produce identical digests, so one precomputed table
attacks the whole user table at once and two accounts sharing a password are visibly
identical in the database.

Three copies is a defect in its own right. A password set in Administration and a password
checked at sign-in were equal only by the coincidence of two duplicated implementations
agreeing.

### Fix

One implementation, in the service that owns authentication:

- `AuthenticationService.HashPassword` uses `Rfc2898DeriveBytes.Pbkdf2` — framework, no new
  dependency — with a 16-byte random salt, a 32-byte digest and 600,000 iterations, the
  OWASP figure for PBKDF2-HMAC-SHA256. Output format
  `pbkdf2-sha256$<iterations>$<salt>$<digest>`, about 85 characters, which fits the existing
  `User.PasswordHashMaxLength = 256` column: **no migration**.
- The iteration count is written into every hash and read back out when verifying, so raising
  it strengthens new passwords without invalidating stored ones and without a forced reset.
- `VerifyPassword` is on the interface because a salted hash cannot be checked by re-hashing
  and comparing strings. It uses `CryptographicOperations.FixedTimeEquals`, and it returns
  false for a malformed stored hash rather than throwing a `FormatException` at the login
  dialog.
- The other two copies are gone. `AdministrationViewModel` injects `IAuthenticationService`
  and calls it; the seed no longer exists ([F-004](#f-004)).
- A bare 32-byte base64 digest — the old format — is still accepted, so upgrading the
  application does not lock an existing installation out of its own accounts. Nothing in this
  build ever writes that format; an account still carrying one is upgraded by setting its
  password again in Administration.

`AuthenticationService` also stopped injecting `IRepository<User>` and now takes
`Func<IUnitOfWork>`. The service is a singleton and `WeighBridgeDbContext` is transient, so
the old shape pinned one `DbContext` — and its change tracker — open for the life of the
process, and could not have written the new account at all. This is the same defect class as
[F-001](#f-001).

### Verification

- `HashPassword_SaltsEveryPasswordSeparately` asserts the property an unsalted digest cannot
  have: two hashes of one password differ, and both verify.
- `VerifyPassword_StillOpensAnAccountStoredInTheLegacyFormat` pins the continuity path.
- `VerifyPassword_RejectsAMalformedStoredHashInsteadOfThrowing` covers five corrupt shapes.
- `InitialSetup_CreatesAnAdministratorAndSignsItIn` asserts the stored value contains no
  trace of the plaintext and begins `pbkdf2-sha256$`.
- Cost measured, not assumed: 512 tests including ~11 PBKDF2 operations run in 6 s, and one
  sign-in adds about 400 ms to application startup.

### Phase 6 — password handling evidence

Searched after a full 10-cycle run in which the password was typed into the live application:

| Check | Result |
| --- | --- |
| The chosen password, or `admin123`, in `weighbridge-2026-08-16.log` | 0 matches |
| The chosen password in `weighbridge.db` | 0 matches |
| `password`, `secret` or `credential` in `appsettings.json` | none |
| Any `Log*(...)` call in `src/` mentioning a password | none |

Audit entries are written by `AuditLogger` through the same `ILoggerFactory` into that same
log file, so the first row covers audit-log exposure too.

---

## F-006

**P3 (was P1) — `Settings.Edit` is declared, granted, and has nothing to enforce it on.**
**Reproduced; does not hold as written. Reclassified.**

### What was claimed

That `SettingsViewModel` does not inject `IPermissionService`, so "any operator, including
ReadOnly, can change hardware, printing and application settings — on a weighbridge that
includes the COM port and calibration." That finding was raised from a grep, was recorded as
unreproduced, and the impact claim is **false**.

### Reproduction

`scripts/navigation-audit.ps1` navigates to Settings and enumerates the module's interactive
controls through UIA. The shell's own chrome contains no text box, combo box or check box, so
whatever is found belongs to the module:

```
[nav] settings module: 0 text input(s), 1 combo box(es), 1 check box(es)
```

Zero text inputs. The module cannot change the COM port, the baud rate, the protocol, the
stability tolerance, the printer, the copy count, the paper size or any camera option: every
one of those is rendered as a read-only `TextBlock`
([SettingsView.xaml:70-203](../src/WeighBridge.App/Views/SettingsView.xaml#L70-L203)), under a
sub-header that says "from appsettings.json". The two writable controls are the current
operator's own appearance theme and navigation-collapse preference, which
`SavePreferencesCommand` writes to that operator's `userpreferences.json`.

Nothing in the application writes `appsettings.json` in response to operator input.
`ConfigurationProvisioner` writes it only at startup, to create it when missing or to repair
it when corrupt, from `DefaultConfiguration`. No ViewModel touches it — which also satisfies
the audit's separate requirement that settings not be manipulated as JSON from a ViewModel.

### What is actually true

`Permissions.SettingsEdit` is declared, is granted to Administrator alone (via
`Permissions.All` in `Roles.Administrator`), and is consulted nowhere. It is therefore an
instance of [F-007](#f-007) — a permission that reads as a control and is not one — and not a
bypass, because there is no capability behind it to bypass.

### Not fixed, deliberately

Gating `SavePreferencesCommand` on `Settings.Edit` would be a *fake* control, and a harmful
one: the permission is Administrator-only, so it would stop a ReadOnly operator choosing
their own colour scheme while protecting nothing. A personal display preference is not
application configuration.

The two coherent options are to leave the permission declared against the day the product
gains an editable settings surface — and wire it at that point — or to remove it now. That is
the owner's call, and either way the P1 impact claimed above does not exist. Recorded, not
repaired.

---

## F-007

**P2 — 8 of the 13 declared permissions are enforced nowhere in production code.**

Enforced: `Weighment.Create`, `Weighment.Cancel` (via `WeighmentCommands`),
`Masters.Edit`, `Masters.Delete` (via `MasterCommands`), and `Users.Manage` as of
[F-002](#f-002).

Declared and never checked: `Weighment.Edit`, `Weighment.Reprint`, `Masters.View`,
`Reports.View`, `Reports.Export`, `Settings.Edit` (see [F-006](#f-006)), `Audit.View`,
`Diagnostics.View`.

A permission that is declared, assigned to roles and shown in the UI but never consulted
is worse than an absent one: it reads as a control that exists. Each needs either
enforcement at its write/read path or removal.

### Resolution — split into what was reachable and what is not

Two of the eight guarded an operation an operator can actually perform today, from a screen
on the navigation rail, for every role. Both were exploitable and both are now fixed and
proven at runtime: `Reports.Export` ([F-025](#f-025)) and `Weighment.Reprint`
([F-026](#f-026)).

The remaining six are **not** privilege holes, because there is no operation behind them:

| Permission | Why nothing enforces it |
|---|---|
| `Weighment.Edit` | A completed weighment cannot be edited anywhere in the application. |
| `Settings.Edit` | No setting is editable from Settings — reproduced in [F-006](#f-006): the module exposes 0 text inputs. |
| `Audit.View` | There is no audit viewer, and no audit table to point one at ([F-030](#f-030)). |
| `Diagnostics.View` | There is no diagnostics screen. |
| `Masters.View`, `Reports.View` | Granted to all four roles, so a check would refuse nobody. They are `View` permissions on modules that are on the rail for everyone by design. |

So F-007 as written overstated the exposure — six of the eight were declarations ahead of
features, one of which is a documentation defect ([F-006](#f-006)) — and understated it for
the two that mattered, which it listed beside the harmless ones without noticing that they
gate live operations. **A permission inventory is not a risk assessment.** The question is
not "is this checked" but "can a user reach the operation it names".

---

## F-008

**P2 — the Administration form took a password in a plain `TextBox`. Fixed.**

`Views/AdministrationView.xaml` bound `FormPassword` to a `TextBox`, so a new operator's
password was displayed in clear text while being typed — at a weighbridge counter, where
the person whose account is being created is standing at the window, alongside drivers.
The login dialog used a `PasswordBox` correctly, so the product contradicted itself.

### Fix

`AdministrationView.xaml:134-142` — a `PasswordBox` named `FormPasswordBox`.
`PasswordBox.Password` is deliberately not a bindable `DependencyProperty` (binding it
would put the plaintext in a property-change notification and in any binding trace), so the
value reaches the view model through a `PasswordChanged` handler in
`AdministrationView.xaml.cs`, which is the same shape `LoginDialog.xaml.cs` already used.

### Verification

`scripts/privilege-enforcement-check.ps1` creates its ReadOnly probe account through this
form, over UIA, and the account is then used to sign in — so the field still accepts a
password, and one that authenticates. A `PasswordBox` exposes no `Text` to the automation
tree, which is why the script sets it through `ValuePattern` and why the run proves the
control was swapped rather than merely styled.

---


## F-009

**P2 — five of seven views set no `AutomationProperties.Name`.**

Only `VehicleEntryView.xaml` (13 occurrences) and `DuplicateSlipView.xaml` (2) name their
controls. Administration, Masters, Reports, Settings and Dashboard set none, and none of
the views use `AutomationProperties.LabeledBy`, so a sibling label `TextBlock` does not
name its input to a screen reader.

This is an accessibility defect in its own right, and it is why the reproduction script
for [F-002](#f-002) has to anchor on the "Account is Active" check box and walk the UIA
tree upward to reach the three text boxes positionally.

---

## F-010

**P2 — `WeighBridge.App` has no unit-test coverage of any kind.**

`tests/WeighBridge.Tests/WeighBridge.Tests.csproj` targets `net8.0` with no WPF and does
not reference `WeighBridge.App`. The stated reason is headless CI. The consequence is that
no ViewModel is unit-testable, which is the structural explanation for a claimed 497
passing tests alongside a user-management module that persisted nothing
([F-001](#f-001)) and enforced no authorisation ([F-002](#f-002)).

Both of those defects lived entirely in a ViewModel, i.e. in the one assembly the suite
cannot see. Recorded rather than fixed: changing the suite's target framework contradicts
a documented, deliberate decision, and that call belongs to the project owner. The options
are a second test project targeting `net8.0-windows`, or moving user-management logic into
a service ([F-011](#f-011)), which would make it testable where the suite already reaches.

### How this bounded the audit itself

`WeighBridge.Printing` also targets `net8.0-windows`, so it is equally unreachable, and
`tests/WeighBridge.Tests/App/` is an empty directory. The practical effect on this audit:

- [F-025](#f-025) (`CsvReportService`) **could** be unit-tested — `WeighBridge.Reporting` is
  `net8.0` — and has three new tests.
- [F-026](#f-026) (`WindowsPrintService`) **cannot be**. Its only evidence is
  `scripts/privilege-enforcement-check.ps1` driving the real application over UIA. If that
  script is ever deleted, the reprint permission gate has no regression test at all.
- The `AuthoriseUserManagementAsync` gate added for [F-002](#f-002) has no unit test either,
  for the same reason, which is why it needed a UIA script too.

So the 531 passing tests do not cover: any view model, any WPF behaviour, the print service,
or the reprint permission check. That is not a criticism of the number; it is what the number
excludes, and it is why every claim in this report that concerns those areas cites a runtime
script rather than a test.

---


## F-011

**P2 — user management bypasses the command/service pattern every other write uses.**

Every other write in the application goes ViewModel → service → `IApplicationCommand` →
`CommandExecutor`, which is where authorization is enforced and the operation is audited.
User management went ViewModel → repository directly. That is why it needed a hand-written
authorization gate ([F-002](#f-002)) and hand-written log lines ([F-001](#f-001)) that
every other write gets structurally, and it is the reason the defects were invisible to
the test suite ([F-010](#f-010)).

The consistent fix is a `UserService` plus `IRequiresPermission` commands mirroring
`MasterCommands`. Deliberately **not** done during the audit: it is a refactor of a module
that now works and is verified, and introducing it mid-audit would put unreviewed code
under the remaining phases. Recorded as the recommended follow-up.

---

## F-012

**P2 — three smoke scripts delete the operator's live database without asking.**

- `scripts/hardware-smoke.ps1:137-142` — `weighbridge.db` and `appsettings.json`
- `scripts/vehicle-entry-smoke.ps1:265` — `Remove-Item "$dbFile*"`
- `scripts/masters-smoke.ps1:184` — `Remove-Item "$dbFile*"`

Two of the three use a `*` wildcard, so they also take any sibling file whose name starts
with `weighbridge.db` — the SQLite `-wal` and `-shm` files, and any stash a careful script
put beside it. `runtime-smoke.ps1` and `final-application-smoke.ps1` do not delete anything.

Observed during this audit: the pre-audit backup taken at 09:42 contained 3 weighment
slips; the live database at 11:50 contained 1. The operator's real data had already been
destroyed by a previous smoke run. It was recovered from `_audit-backup\weighbridge.db`
(slips `WB-000001`–`WB-000003` restored, verified by `grep -ao 'WB-[0-9]\{6\}'`).

A test that needs a clean database should point the application at a temporary data root,
or move the real one aside and put it back. `scripts/startup-cycle-audit.ps1`, written
during this audit, does the latter in a `finally` block and is the pattern the other three
should follow.

---

## F-013

**P2 — the camera reported connected when it was not.** Reproduced and superseded: the root
cause turned out to be larger than a wrong status string — the service does not capture at
all, it *generates* images, and they were being filed against weighments as photographs.
Recorded in full as [F-028](#f-028) and fixed there. The observation that
`hardware-smoke.ps1` "asserts only that a `.jpg` exists and is over 100 bytes, so it passes
on a synthetic image" was correct, and is the reason that defect survived a passing smoke
run — see [F-018](#f-018).

---

## F-014

**P2 — `SecurityOptions.DefaultRole` was `Administrator`. Fixed.**

The fallback identity for an operator that cannot be resolved was the most privileged role
in the system.

### The justification that was false

The value was defended on the grounds that it "ships in `appsettings.json`", i.e. that a
real installation always overrides it. It does not. The live installation on this machine
has **no `Security` section at all** (`appsettings.json` top-level keys: `Application`,
`Database`, `Logging`, `Hardware`, `Camera`, `Printer`, `Server`, `Reporting`), so the code
default is exactly what applies. A default that is only safe because something else is
expected to overwrite it is not a default; it is an assumption, and this one was untrue on
the only installation available to test.

### Fix

`SecurityOptions.cs:30` — `DefaultRole` is `nameof(Roles.ReadOnly)`, the least privileged
role. `ResolveDefaultRole()` still falls back to `Roles.Operator` when the configured name
does not resolve, which is deliberate: an unparseable role name is a configuration error,
and `Operator` is the working-but-unprivileged answer, not `Administrator`.

---


## F-015

**P2 — the weight indicator was released twice, both times after the shutdown marker.**
Component: `src/WeighBridge.Hardware/WeightIndicators/WeightIndicatorSimulator.cs`,
`src/WeighBridge.App/Bootstrapper.cs`

### Description

```
12:35:15.079 [INF] WeighBridge.App.Bootstrapper :: ==== Shutdown complete ====
12:35:15.084 [INF] WeightIndicatorSimulator :: Weight indicator simulator disconnected
12:35:15.084 [INF] WeightIndicatorSimulator :: Weight indicator simulator disconnected
```

Two problems in three lines: the simulator is released more than once, and every release
happens *after* the line that claims shutdown is complete. On a real installation that line
is a serial port, so the log said the application had finished while it still held the
hardware — and the writer that would have recorded the release was itself about to be
disposed.

### Root cause

Two independent causes, both required.

1. `DisconnectAsync` logged unconditionally, outside its own `if (cts is not null)` guard,
   so it announced a disconnect even when there was nothing connected.
2. `HardwareServiceCollectionExtensions` registers the simulator under three descriptors —
   `AddSingleton<WeightIndicatorSimulator>()` plus two forwarding factories. MEDI captures
   the instance in the root scope's disposables once per *resolved* descriptor and does not
   deduplicate, so disposing the container disposes the same object twice. Container
   disposal is the last stage of shutdown, after the marker is written.

`CameraService` is registered in the same double-capture shape. It is harmless today (it
disposes only a `SemaphoreSlim`) and is left recorded rather than changed.

### Fix

- The log line moved inside the `if (cts is not null)` guard, so a release is announced
  once, by the code path that actually released something. Disposal stays idempotent.
- `Bootstrapper.ShutdownAsync` now calls `IWeightIndicatorService.DisconnectAsync()`
  explicitly, after the background loops stop and before the placement and settings saves —
  i.e. while the log writer is still alive — rather than leaving the release to container
  disposal.

### Verification

The first verification of this fix was a **false pass**, and is recorded because it is the
exact failure mode the audit brief warns about. A 10-cycle run went green, but the
simulator had never connected in any of those cycles (`Degraded: Simulator disconnected`
throughout), so the assertion could not distinguish "the line moved" from "the line stopped
being written".

`scripts/startup-cycle-audit.ps1` now points the operator's `LastModule` preference at
Vehicle Entry for the duration of the run, restoring it afterwards, because
`VehicleEntryViewModel.cs:495` is the only caller of `IWeightIndicatorService.ConnectAsync`.
Each cycle then asserts the indicator started, and that it was released **exactly once**
and before the marker.

With a genuinely connected simulator, 10 of 10 cycles pass, and the application's own log
shows one connect and one release per cycle, every release before the marker:

```
13:10:41.066  starting on PRINCE
13:10:42.944  simulator stream
13:10:43.451  simulator disconnected
13:10:43.466  Shutdown complete
```

The assertion was then proved to bite. The pre-fix unconditional log line was
reintroduced under an `AUDIT-PHASE22-TEMPORARY` marker and the run failed on every cycle:

```
cycle  1 [clean database    ] exit 0  FAIL (2)
     - 2 log line(s) written after 'Shutdown complete'
     - the indicator reported disconnecting 3 time(s), expected 1
```

Three releases — one from `ShutdownAsync` plus one per resolved container descriptor — which
also confirms the double-capture root cause directly. The marker was removed
(`grep -rn AUDIT-PHASE22-TEMPORARY src/ tests/ scripts/` returns nothing), the build is
clean, and the restored run is 10 of 10 again.

---

## F-016

**P3 — capture filenames mix UTC and local time.** `CameraService.cs:119` and `:186`
disagree. Carried forward, not yet re-verified.

---

## F-017

**P3 — three files still say there is no login screen.**

`SecurityOptions.cs`, `PermissionService.cs` and `IPermissionService.cs` carry remarks of
the form "Until there is a login screen the identity comes from the Windows account". A
login screen exists and is mandatory (`App.OnStartup` shows it before the shell). The
comments describe a state of the world that is two modules out of date.

**Fixed**, and reclassified in effect if not in severity: these comments were not cosmetic.
They were the accurate record of an unfinished migration, and they are the reason it survived
review — [F-020](#f-020) is exactly the defect they describe, left in the code for two
modules while the comments explaining it went unread. All five were rewritten
(`SecurityOptions.cs`, `PermissionService.cs`, `IPermissionService.cs` ×2 and
`IApplicationInfoService.cs`, which said "the Login module will replace this").

`SecurityOptions.DefaultRole` was left at `Administrator` when these comments were rewritten,
with the comment recording that its justification was gone rather than pretending the value was
fine. It has since been narrowed to `ReadOnly` — see [F-014](#f-014).

---

## F-018

**P2 — the audit's own startup script returned a false pass, then a false failure.**
Component: `scripts/startup-cycle-audit.ps1`

Recorded as a finding rather than quietly corrected, because the brief requires the
automation itself to be shown capable of detecting real failures, and this script twice
reported the wrong answer about the application.

**The false pass.** Its shutdown-ordering assertion went green across 10 cycles while the
weight indicator had never connected, so the invariant it claimed to check was vacuous.
Fixed by making every cycle open Vehicle Entry and assert that the indicator actually
started before judging how it was released. See [F-015](#f-015).

**The false failure.** The corrected assertion then reported "the indicator reported
disconnecting 2 time(s), expected 1" on all 10 cycles, against an application that was
behaving correctly. PowerShell's `-match` is case-insensitive, so the pattern
`simulator disconnected` also matched `SystemStatusService`'s unrelated startup line
`Weight Indicator status changed from Disconnected to Degraded: Simulator disconnected`.
Fixed by anchoring the pattern on the simulator's own logger name. The application's log
sequence — one connect, one release, release before the marker, ten times — was confirmed
independently of the script before the script was touched.

A third change made while chasing the false failure was reverted: the per-cycle log
baseline was rewritten to read the file length from an open handle rather than from
`Get-Item`, on the theory that Windows was reporting a stale directory-entry size. It
changed nothing, the theory was wrong, and the one-line original was restored rather than
left in place looking like a fix.

### Three further instances, found later in the audit

The pattern kept recurring, so it is worth stating as a general result: **a check written
against a component that is not running passes for the wrong reason, and reads identically
to a check that passed for the right one.**

1. **`hardware-smoke.ps1` inherited the simulator default.** It never set
   `Hardware:WeightIndicator:DriverType`, so it verified whatever the installation happened
   to be configured for — the simulator ([F-027](#f-027)). Its camera assertion was `.jpg`
   exists and is over 100 bytes, which a 328-byte generated test pattern satisfies
   ([F-028](#f-028)). A "hardware smoke test" that passes with no hardware attached, and
   passes on generated evidence, was the single largest false-confidence source found.
2. **`hardware-smoke.ps1` clicked a navigation item without confirming navigation.** It
   asserted on the rail's state, which `SelectionItemPattern.Select()` sets without running
   the navigation command — the same defect shape as the rail-highlight assertion corrected
   in `navigation-audit.ps1`. Now every navigation in the audit's scripts is confirmed by a
   delta in the count of `Navigated to <ViewModel>` lines in the log.
3. **`privilege-enforcement-check.ps1` silently discarded its own failures.** Its two
   `Test-` functions collected failures with `$failures += …` inside a function, which in
   PowerShell creates a *function-local* copy of the array. Every failure either function
   found would have been dropped and the script would have printed PASS. Found before the
   first real run, and fixed by routing all 15 collection points through
   `function Add-Failure($text) { $script:failures += $text }`. This is the same class of
   defect as the false pass above, in the script written to prove two security controls.

The lesson applied throughout the rest of the audit: an assertion must be shown to fail
against a known-broken state before its passing result is believed. That is what phase 22's
defect injection did for the earlier findings, and what the deliberate two-direction design
of `privilege-enforcement-check.ps1` does for [F-025](#f-025) and [F-026](#f-026) — it
checks that a privileged operator still succeeds, so a refusal that refused everyone would
fail the run.

---


## F-019

**P2 — every visit to the Dashboard left a permanent subscriber on the weight indicator.**
**Fixed, verified.**

### Description

`DashboardViewModel` subscribed to `IWeightIndicatorService.ReadingReceived` and
`StateChanged` **in its constructor**, and removed those handlers only in `Dispose()`.

Four facts in the surrounding code turn that into a leak, and all four were confirmed by
reading it:

| | |
|---|---|
| `IWeightIndicatorService` | singleton — `HardwareServiceCollectionExtensions.cs:37` |
| `DashboardViewModel` | transient — `Bootstrapper.cs:286`, so a new instance per navigation |
| `NavigationService.ActivateAsync` | calls `OnNavigatedFromAsync()` on the outgoing ViewModel and nothing else — no disposal, at `NavigationService.cs:199-213` |
| `DashboardViewModel.Dispose()` | called by nothing in the repository. The only `Dispose()` invocation on any ViewModel is `ProgressDialogViewModel`'s on its own `CancellationTokenSource` |

So the one place the handlers were removed never ran. Every visit to the Dashboard added a
subscriber that was never released, and because the singleton's event delegate holds a strong
reference, the instance could not be collected even after the navigation history trimmed its
entry past `MaxHistoryDepth`. The accumulation was unbounded across the life of the process.

Consequences, in order of how much they matter:

- Each retained ViewModel goes on handling every reading. The simulator's configured cadence
  is 250 ms (`PollIntervalMilliseconds`), so each leaked visit adds four `IUiDispatcher.Post`
  calls per second to the UI thread, permanently, all of them updating properties nothing is
  bound to. Over a shift of an operator moving in and out of the Dashboard this grows without
  bound and only a restart clears it.
- Each retained ViewModel also holds the `IRepository<Weighment>` it was constructed with, and
  therefore a `WeighBridgeDbContext` — which is transient. One leaked DbContext and change
  tracker per Dashboard visit. Same class as [F-001](#f-001) and [F-005](#f-005).
- `IDisposable` on the type advertised a cleanup contract that nothing honoured, which is what
  made the leak easy to miss on review.

### Fix

The subscription now follows activation rather than construction, which is the pattern
`VehicleEntryViewModel` already used correctly at `VehicleEntryViewModel.cs:480-512`: `-=`
then `+=` in `OnNavigatedToAsync` (idempotent on a repeat activation), and `-=` in a new
`OnNavigatedFromAsync`. `IDisposable` and the dead `Dispose()` are removed, so the type no
longer claims a contract no caller invokes.

`NavigationService` still disposes nothing, and up to `MaxHistoryDepth` ViewModels stay
reachable from the two history stacks — deliberately, since `GoBackAsync` reactivates the same
instance. With no subscriptions held while inactive that retention is bounded and inert. It is
recorded here rather than redesigned, because changing history semantics mid-audit is outside
what the brief permits.

### Verification

- `scripts/navigation-audit.ps1` asserts that the Dashboard, once activated, shows a live
  indicator status — one of `ZERO`, `NEGATIVE`, `STABLE`, `UNSTABLE`, none of which is the
  field's `"Disconnected"` initial value. It visits Vehicle Entry first, because that is the
  module that connects the indicator; the Dashboard never connects it itself. Passing run:
  `[nav] dashboard live status: ZERO`.
- **The assertion was proved to bite** (phase 22). Removing just the `ReadingReceived += `
  line made the run exit 1 with exactly one failure — "the Dashboard showed no live indicator
  status within 10s of being activated" — while all 21 navigation assertions and the Settings
  check still passed. Marker removed, `grep -rn AUDIT-PHASE22` clean, build and 512 tests
  green again.
- Without that assertion the release half of the fix would have been unfalsifiable: a
  Dashboard that unsubscribes and never re-subscribes shows a frozen reading forever, and
  every other assertion in the script passes.

### The measurement that proved nothing, reported as such

The script samples the process working set before and after 12 further Dashboard visits. Three
runs gave 347→348 MB, 337→356 MB, and — with the defect deliberately injected — 255→345 MB.
The figure is dominated by GC timing and JIT, the collector cannot be driven from outside the
process, and the leak's own footprint is around a megabyte. **The script therefore reports
this number and asserts nothing on it.** A threshold here would pass or fail for reasons
unrelated to the defect, which is precisely the false-signal class this audit exists to
remove. The evidence for the leak is the four-link chain in the source, not the memory graph.

There is no unit test for this. `WeighBridge.Tests` targets `net8.0` with no reference to
`WeighBridge.App`, deliberately, so the suite runs headless — which means no ViewModel in the
application can be unit-tested at all. That is [F-010](#f-010), and it is the reason a P2
defect in a shipped module had to be caught by reading and pinned by a UIA script.

---

## F-020

**P1 — every log and audit entry named the Windows account instead of the operator who
signed in.**
Component: `WeighBridge.Core/Logging/CategoryLoggerBase.cs`,
`WeighBridge.Services/Security/PermissionService.cs`

### Description

The audit trail attributed every action on a terminal to whichever Windows account the
application happened to be running under — `dell` on this machine — no matter which operator
had signed in. On a weighbridge terminal shared by a shift of operators, that is the whole
value of the trail gone: it records that something was recorded, not who recorded it.

`AuditLogger.Record` puts no operator in the message text, so the `user=` field in the scope
is the *only* subject an audit entry has.

### Reproduction

No new run needed — the defect is in the logs the earlier phases already produced,
`%LOCALAPPDATA%\WeighBridge Modern\Logs\weighbridge-2026-08-15.log` and `-16.log`. Sign in as
`admin` and record a weight; every entry from then on still reads `user=dell`.

### Evidence

Two lines, quoted verbatim — one from a sign-in, one from the audit category:

```
2026-08-16 09:42:33.488 [INF] [01] WeighBridge.Application
    {user=dell machine=PRINCE version=0.1.0}
    :: User admin authenticated successfully as Administrator

2026-08-15 22:30:45.610 [INF] [01] WeighBridge.Audit
    {module=Record second weight user=dell corr=b35fa542e82c machine=PRINCE version=0.1.0}
    :: Record second weight RecordSecondWeightCommand - SlipNumber=WB-000001,
       Kilograms=12500, WeightSource=Manual, NetKilograms=22500
```

The first line names both identities at once: the application knew the operator was `admin`
well enough to say so in the message, and stamped `dell` on the same entry.

### Scope — what was *not* wrong

The database attribution is correct and was verified before anything was changed.
`Weighment.CreatedBy` / `ModifiedBy` and every master-data service take the operator from
`IPermissionService.CurrentOperator`, so the *records* name the right person:

```
2026-08-16 11:16:36.234 [INF] [01] WeighBridge.Services.Weighments.WeighmentService
    :: Weighment WB-000001 opened for MH12AB1234 in GrossFirst mode by admin
```

Only the log and audit enrichment was wrong. The fix is scoped to that and touches no
persistence path.

### Root cause

`CategoryLoggerBase` read `IApplicationInfoService.CurrentUserName` — which returns
`Environment.UserName` — once, into a field, **in its constructor**. Every logger is a
singleton, so that field was captured before any login could happen and could never change
afterwards. Login was added by a later module and nothing ever replaced the source.

The code said so itself. Three doc comments still read "Until there is a login screen the
identity comes from the Windows account" and "Module 0.1 shows the Windows account; the
Login module will replace the source" — an unfinished migration, described accurately, in
comments nobody re-read once the login screen existed. That is [F-017](#f-017), and this is
what it was hiding.

### Fix

A logger cannot simply ask `IPermissionService` who is signed in: `PermissionService` takes
`IApplicationLogger` **and logs from its own constructor**, so a lazy lookback — `Func<>` or
`provider.GetService` — would resolve a singleton that is still under construction. The
container would build a second instance, which logs, which resolves again, until the stack
runs out.

So the operator is *published*, not fetched. `SignedInOperator` (new,
`WeighBridge.Core/Security/`) is a one-field cell that depends on nothing.
`PermissionService.SetOperator` writes it before its own log line, and every logger reads it
per entry, falling back to the Windows account when nothing has been published — which is
the honest answer for an entry no operator caused.

The cell is a **required** constructor parameter on `PermissionService`, so a missing
registration fails at `ValidateOnBuild`, and an **optional** one on the five loggers, so all
28 existing construction sites compile untouched and exercise the fallback.

A `static` ambient holder — the shape `ModuleScope` and `CorrelationScope` already use — was
rejected: xUnit parallelises across test classes, so one test signing an operator in could
flip another test's assertion. The brief forbids masking flaky behaviour; it equally forbids
introducing it.

### Verification

Three regression tests, then the same defect injected twice to prove each level is covered
independently:

- `CategoryLoggerTests.Enrichment_FollowsTheSignedInOperator_ForEntriesFromOneLogger` — two
  entries from **one** logger instance, before and after the sign-in, must name different
  users. A test that built a second logger after the sign-in would have passed against the
  defect.
- `CategoryLoggerTests.Audit_NamesTheSignedInOperator_NotTheTerminalAccount` — the audit
  category specifically, because `AuditLogger` is a separate class that has to forward the
  cell.
- `AuthenticationTests.Authenticate_NamesTheOperator_InTheLogAndTheAuditTrail` — through the
  real `PermissionService` and `AuthenticationService`, asserting on the very line quoted as
  evidence above. The defect was the wiring, so the wiring is what this exercises.
- `scripts/startup-cycle-audit.ps1` now fails any cycle where an entry after the sign-in
  names anyone other than the operator. This exists because the loggers take the cell as an
  *optional* parameter: a registration that omitted it would compile, resolve, pass every
  unit test, and quietly go back to stamping the Windows account. Only the running container
  can show the wiring survived.

Injection 1 — `user: _terminalUser` restored in the enrichment: **3 failures, exactly the
three new tests**, 512 others still green; and 10 of 10 startup cycles failed, reproducing
the evidence line verbatim (`{user=dell …} :: User admin authenticated successfully as
Administrator`).

Injection 2 — the publish removed from `SetOperator`: **1 failure**, the wiring test only.
The two logger unit tests still passed, which is the point of having both levels.

The injection's own output then exposed a hole in the new script assertion: only one
post-sign-in entry carries a `user=` field at all, because loggers resolved as `ILogger<T>`
are not enriched — so dropping the field from the enrichment entirely would have left
nothing to judge and passed. A positive check ("at least one entry after the sign-in carries
a user field") was added before restoring.

Both injections removed; `grep -rn AUDIT-PHASE22 src/ tests/ scripts/` clean. After restore:
**515 passed, 0 failed**; `startup-cycle-audit.ps1` exit 0, 10 of 10;
`navigation-audit.ps1` exit 0.

---

## F-021

**P2 — the shell title bar showed the Windows account, not the operator.**
Component: `WeighBridge.App/ViewModels/MainWindowViewModel.cs`

`CurrentUserName => _applicationInfo.CurrentUserName` — the same wrong source as
[F-020](#f-020), reaching the screen instead of the log. The operator sees the machine's
Windows account where the application says who is signed in.

Same origin, and again the file admitted it: "Module 0.1 shows the Windows account; the Login
module will replace the source."

**Fix.** `CurrentUserName` is now a get-only property assigned once in the constructor from
`permissions.CurrentOperator.DisplayName`. Read once, with no change notification, because
`App.OnStartup` signs the operator in (`App.xaml.cs:68`) *before* it creates the shell
(`:76`) and the application has no sign-out — so the operator cannot change while this window
exists. That is the same reasoning the file already uses to decide whether to show the
Administration rail item. A sign-out would make this a subscription to
`IPermissionService.OperatorChanged`; see [F-022](#f-022).

Verified on screen, not by reading the code: `scripts/shell-identity-check.ps1` (new) signs in
through the login dialog, enumerates every static text in the shell window over UIA and
requires that the operator's name is among them and the Windows account is not — checked in
both directions, because a check for only the first would pass while both were on screen.
Exit 0. With the old one-line read injected back in, it printed
`title-bar identity candidates: dell` and exited 1. Injection removed.

The title-bar `TextBlock` has no `AutomationId`, so the check identifies it by content; that
is [F-009](#f-009).

---

## F-022

**P1 — the application has no way to sign out, and the method that claimed to was dead code
that would have forged an identity.**
Component: `WeighBridge.Core/Security/IAuthenticationService.cs`,
`WeighBridge.Services/Security/AuthenticationService.cs`

### Description

`IAuthenticationService.Logout()` had **zero callers** anywhere in `src/` or `tests/`. No
menu item, no button, no keyboard shortcut, no code path. An operator who signs in at the
start of a shift stays signed in until the process ends.

Worse than absent. Its body did not end the session — it installed a new one:

```csharp
_permissionService.SetOperator(new OperatorIdentity(
    _applicationInfo.CurrentUserName, _applicationInfo.CurrentUserName, Roles.ReadOnly));
```

A *usable* ReadOnly session named after the Windows account. Had anything ever called it, the
terminal would have carried on working under an identity nobody authenticated — which is
[F-020](#f-020) again, this time manufactured deliberately.

### Fixed, and deliberately not fixed

**Deleted:** the method, its interface declaration, and the `IApplicationInfoService`
dependency that existed only to feed it. Same precedent as the dead `Dispose()` removed under
[F-019](#f-019): a security interface must not advertise a capability the product does not
have.

**Not built:** an actual sign-out — end the session, clear the operator, return to the login
dialog. That is a new feature, and this brief forbids adding features. It is recorded here
for the owner rather than smuggled in under an audit.

### Consequence for release

Stated plainly because the fix is deliberately partial: on a terminal shared by a shift of
operators, there is no way to stop acting as the previous operator short of restarting the
application. [F-020](#f-020) made the audit trail name the right person; without a sign-out,
"the right person" stays whoever signed in first. **The application should not be called
production-ready for shared-terminal use until a sign-out exists.**

---

## F-023

**P1 — a zero-byte database file was treated as a brand-new installation: the application
migrated a fresh schema over it and offered first-run administrator setup.**
Component: `WeighBridge.Infrastructure/Persistence/DatabaseInitializer.cs`

### Description

A zero-byte file is a *valid empty SQLite database*. `MigrateAsync` therefore succeeded
against one, created the whole schema in place, and left a database with no `User` rows —
which is exactly the state `IAuthenticationService.RequiresInitialSetupAsync` reports as a
fresh install. The login dialog then showed its **Administrator setup** form.

On a terminal that has been weighing vehicles for months, that means:

- whoever is standing at the terminal is invited to appoint themselves administrator, with
  no credential, because the application believes this installation is new;
- nothing on screen says a database was lost;
- the zero-byte file — the only evidence that something damaged it — is overwritten with a
  fresh 163,840-byte schema, so nobody can tell afterwards what state it was in.

The application never leaves a zero-byte file at this path itself: SQLite writes the header
on the first write. A zero-byte file there is damage, not a starting state.

### Reproduction

`scripts/corrupt-database-startup.ps1`, `empty` variant. Before the fix:

```
[corrupt] empty             0 bytes -> showed window(s): Administrator setup  FAIL (1)
[corrupt]            - the damaged file was rewritten (0 -> 163840 bytes)
```

The other two variants of the same run were handled acceptably, which is what made this one
worth chasing rather than a general "corrupt database" complaint:

```
[corrupt] garbage          41 bytes -> showed error dialog 'WeighBridge could not start'  ok
[corrupt] truncated     81921 bytes -> showed error dialog 'WeighBridge could not start'  ok
```

### Fix

One guard in `InitializeCoreAsync`, before the context is created, refusing a zero-byte file
at the database path and naming it. Applied only when the connection string is the one
derived from that path — a site that configures its own connection string points the provider
somewhere else, and a stale file at the default path says nothing about the database in use.

### Verification

Regression test `AuthenticationTests.EmptyDatabaseFile_IsRefused_NotTreatedAsAFreshInstallation`
asserts the initialisation fails, the message names the file, and **the file is still 0
bytes** — refusing is worth nothing if the schema is written anyway.

Injection (phase 22): `.Length == 0` changed to `.Length < 0` so the guard can never fire.
Result — exactly one failure, by name:

```
WeighBridge.Tests.Security.AuthenticationTests.EmptyDatabaseFile_IsRefused_NotTreatedAsAFreshInstallation [FAIL]
Failed!  - Failed: 1, Passed: 515, Skipped: 0, Total: 516
```

Injection removed; `grep -rn AUDIT-PHASE22 src/ tests/ scripts/` is clean.

On screen, after the fix — the dialog body read back over UI Automation, verbatim:

```
WeighBridge cannot open its database
The database file is empty: C:\Users\dell\AppData\Local\WeighBridge Modern\Data\weighbridge.db

A zero-byte file is not a database this application wrote. Restore it from a backup, or
delete it if this terminal is genuinely new - deleting an empty file loses nothing, and the
application will then create a database.
```

and in the log:

```
2026-08-16 19:49:45.020 [ERR] [01] WeighBridge.Infrastructure.Persistence.DatabaseInitializer
    :: Database file C:\Users\dell\AppData\Local\WeighBridge Modern\Data\weighbridge.db
       is empty (zero bytes); refusing to migrate over it
```

---

## F-024

**P2 — a failed database initialisation did not stop startup. Three separate doc comments
described a degraded mode that had become unreachable.**
Component: `WeighBridge.App/Bootstrapper.cs`, `WeighBridge.App/App.xaml.cs`,
`WeighBridge.Infrastructure/Persistence/DatabaseInitializer.cs`,
`WeighBridge.Core/Abstractions/IDatabaseInitializer.cs`

### Description

`Bootstrapper.InitializeDatabaseAsync` handled `!result.Succeeded` by setting a status
indicator and **returning normally**:

```csharp
if (!result.Succeeded)
{
    status.Database.Update(ConnectionState.Disconnected, result.Message);
    return;
}
```

`App.OnStartup` then continued straight to `ShowLoginAsync()`, under this comment:

```csharp
// Database must be ready before login because authentication queries it
await _bootstrapper.InitializeDatabaseAsync();
```

The comment states the requirement correctly. Nothing enforced it.

Continuing was deliberate once, and documented in three places — `DatabaseInitializer`'s
remarks ("Failure is never fatal: the shell still opens and simply reports the database as
disconnected"), the `Succeeded` parameter doc ("False when initialisation failed; the app
still starts"), and `InitializeDatabaseAsync`'s own remarks ("Deliberately fire-and-forget
from the caller's point of view"). All three became false when login was placed before the
shell: authentication queries the database, so the shell can never open to display the
Disconnected badge, and the method is awaited rather than fire-and-forget.

What the operator actually got was the **startup catch-all** — the dialog that reports any
unhandled startup exception — because the login dialog's first query threw a moment later:

```
2026-08-16 [ERR] WeighBridge.Infrastructure.Persistence.DatabaseInitializer
    :: Database initialisation failed for ...\weighbridge.db
2026-08-16 [ERR] Microsoft.EntityFrameworkCore.Query
    :: An exception occurred while iterating over the results of a query for context type
       'WeighBridge.Infrastructure.Persistence.WeighBridgeDbContext'.
```

Dialog: *"WeighBridge could not start — The application could not complete its startup and
will close. Please contact support with the log file."* The database is never mentioned. The
initialiser's own message, which does name it, went to the log and nowhere else.

### Fix

`InitializeDatabaseAsync` returns the `DatabaseInitializationResult`; `OnStartup` reports a
failure against the database, by name, and shuts down instead of opening a login dialog over
a database it could not read. The three stale doc comments now describe what the code does.

A second defect surfaced while verifying this one: the generic failure message was
`$"Database initialisation failed: {ex.Message}"`, so the dialog said *"SQLite Error 26:
'file is not a database'"* without saying **which** file — the path was only inside "Show
technical details". A site can keep its database on a share or at a configured path, so the
path is now in the message itself.

### Verification

`scripts/corrupt-database-startup.ps1`, all three variants, exit 0 — and the script now reads
the **dialog body** back, not just its title, because a correct title over an empty body is a
check that passes while the operator has nothing to act on. That stricter assertion is what
caught the missing path: two variants failed on it before the message was fixed.

```
[corrupt] garbage          41 bytes -> showed error dialog 'WeighBridge cannot open its database'  ok
[corrupt] truncated     81921 bytes -> showed error dialog 'WeighBridge cannot open its database'  ok
[corrupt] empty             0 bytes -> showed error dialog 'WeighBridge cannot open its database'  ok
[corrupt] variants: 3   passed: 3   failed: 0
```

Body for the `garbage` variant, verbatim:

```
WeighBridge cannot open its database
The database could not be opened: C:\Users\dell\AppData\Local\WeighBridge Modern\Data\weighbridge.db

SQLite Error 26: 'file is not a database'.        [Show technical details]  [OK]
```

`scripts/startup-cycle-audit.ps1` re-run afterwards: **10 of 10 cycles passed**, exit 0 —
every startup goes through the changed path, so the healthy case needed re-proving, not just
the broken one.

### A defect this introduced in the test tooling, and its fix

The new dialog title was invisible to `Get-WeighBridgeStartupError` in
`scripts/login-helper.ps1`, which matches three exact titles. Any script relying on it would
have reported this dialog as *"the window never appeared"* — the wrong place to send the next
person. The title was added, with a note in the function's doc that a title added to the
application has to be added there too. This is [F-018](#f-018)'s lesson recurring: the
automation is as capable of a false negative as the product is of a defect.

---

## F-025

**P1 — `Reports.Export` was enforced nowhere: any signed-in account could write the site's
whole weighment history to a file.** Fixed, verified at runtime.
Component: `src/WeighBridge.Reporting/Services/CsvReportService.cs`

### Description

`Reports.Export` is declared, granted to `Administrator` and `Supervisor`, and withheld from
`Operator` and `ReadOnly`. Nothing consulted it. The Reports module is on the navigation rail
for every role, and its **Generate CSV** button wrote a file containing every weighment in
the selected date range — vehicle numbers, parties, materials, weights, operators — to
`%LOCALAPPDATA%\WeighBridge Modern\Reports`, from where it can be copied to a USB stick.

A `ReadOnly` account is the one handed out most freely at a weighbridge: a gate guard, a shift
supervisor's read-only terminal, a visiting auditor. It was the account with the least right
to bulk-export and no obstacle to doing so.

### Reproduction

`scripts/privilege-enforcement-check.ps1` phase 2 — sign in as a `ReadOnly` account, open
Reports, click **Generate CSV**, read the status line, and count the `.csv` files in the
reports directory before and after.

Before the fix: the status line reported the report had been generated, and the file count
increased by one.

### Root cause

The permission existed only as a row in `Roles`. No call site anywhere consulted it.

### Fix

The check is in `CsvReportService.GenerateAsync`, before the format check, not in
`ReportsViewModel`. **The method that leaves the file on disk is the place to enforce it** —
a check in front of one caller guards that caller only, and this service is registered as
`IReportService` for anything that resolves it later. On refusal it logs at warning level with
the operator, role and reason, and returns `ReportResult.Failure` with the reason, so the
operator is told why rather than seeing a silent no-op.

### Verification

Three tests in `tests/WeighBridge.Tests/Reporting/CsvReportServiceTests.cs`:

- `GenerateAsync_WithoutTheExportPermission_RefusesAndWritesNothing` — a `[Theory]` over
  `Operator` and `ReadOnly`. Asserts the failure, the null `OutputPath`, the message, **and
  that no file exists**. The last assertion is the one that matters: a refusal returned after
  the writer opened would leave a file behind and pass the first three.
- `GenerateAsync_AsSupervisor_StillProducesTheReport` — the counterpart. Without it, a gate
  that refused everybody would pass the refusal tests.

Runtime, `scripts/privilege-enforcement-check.ps1`, exit 0:

| | as `admin` | as `audit_readonly` (ReadOnly) |
|---|---|---|
| Status line | `Report saved to C:\…\Reports\DailyWeighments_20260818_111529.csv` | `You do not have permission to export reports.` |
| Files written | 1 | **0** |

The log recorded `Permission denied: audit_readonly as ReadOnly lacks Reports.Export`. A
control that only tells the operator and leaves nothing behind for whoever reads the log
afterwards is half a control.

---

## F-026

**P1 — `Weighment.Reprint` was enforced nowhere: any signed-in account could reissue any
slip.** Fixed, verified at runtime.
Component: `src/WeighBridge.Printing/Services/WindowsPrintService.cs`

### Description

`Weighment.Reprint` is declared and withheld from `ReadOnly`. Nothing consulted it. The
Duplicate Slip module is on the navigation rail for every role: search, select any completed
weighment, click **Reprint slip**, and a duplicate came out of the printer.

At a weighbridge a slip is a commercial document — it is what the driver presents and what
the load is billed against. An unrestricted reprint is how one load gets paid for twice.

### Reproduction

`scripts/privilege-enforcement-check.ps1` — sign in, open Duplicate Slip, **Search**, select
the first row, click **Reprint slip**, read the status line.

### Fix

The check is in `WindowsPrintService.PrintAsync`, before the `Enabled` check, and guarded by
`IsDuplicate(data)`:

```csharp
if (IsDuplicate(data)) { /* Authorize(Permissions.WeighmentReprint) or fail */ }
```

Two deliberate choices:

1. **In the print service, not in `DuplicateSlipViewModel`.** This method is what puts paper
   in someone's hand. `DuplicateSlipViewModel` needed no change at all — it already surfaces
   `PrintResult.Message` in its status badge — which is the sign the gate went in the right
   place.
2. **`IsDuplicate` is a shared helper, read by both the gate and the slip's title line.**
   `DrawSlip` already decided whether to stamp `WEIGHMENT SLIP (DUPLICATE)` from
   `data["IsDuplicate"]`. Both now read the same field through the same helper, so a slip
   cannot print stamped DUPLICATE while the gate believes it was a first print. A first
   print carries no flag (`VehicleEntryViewModel.cs:854`) and is unaffected — the permission
   for that is `Weighment.Create`, which is enforced already.

### Verification

**This fix has no unit test and cannot have one.** `WeighBridge.Printing` targets
`net8.0-windows`; the test project targets `net8.0` and cannot reference it
([F-010](#f-010)). `scripts/privilege-enforcement-check.ps1` is its only regression test.

`Printer.Enabled` is `false` on this machine, which turned out to be the ideal
discriminator — a privileged operator's reprint fails for a reason that is provably *not*
about permission, and no paper is produced:

| | as `admin` | as `audit_readonly` (ReadOnly) |
|---|---|---|
| Status line | `Printing is disabled.` | `You do not have permission to reprint slip.` |

The script asserts the admin message does **not** match `permission` and the ReadOnly message
does. The log recorded `Permission denied: audit_readonly as ReadOnly lacks
Weighment.Reprint`.

Not verified, and stated rather than implied: that a reprint on a machine **with** a working
printer produces one correctly stamped page for a privileged operator. No printer is attached
to this machine.

---

## F-027

**P1 — the shipped default weight source was the simulator, and any unrecognised driver type
fell back to it silently.** Fixed, verified.
Component: `src/WeighBridge.Settings/Configuration/DefaultConfiguration.cs`,
`src/WeighBridge.Core/Configuration/HardwareOptions.cs`,
`src/WeighBridge.Hardware/DependencyInjection/HardwareServiceCollectionExtensions.cs`

### Description

Three separate defaults conspired to make a fresh installation weigh vehicles with invented
numbers:

1. `DefaultConfiguration` wrote `Hardware:WeightIndicator:DriverType = "Simulator"` into a new
   installation's `appsettings.json`.
2. `HardwareOptions.WeightIndicatorOptions.DriverType` also defaulted to `"Simulator"`, so a
   configuration file missing the key got the simulator too.
3. The DI factory's fallback for **any** unrecognised `DriverType` was the simulator. A typo
   in one configuration key — `"Seriall"`, `"serial "`, `"RS232"` — was therefore enough to
   put generated weights onto a printed slip, with nothing said anywhere.

The simulator produces plausible, smoothly settling weights that are indistinguishable at a
glance from a real load. This is a weighbridge: the number on the slip is what money changes
hands over.

### Fix

The simulator is now reachable **only by asking for it by name**:

- `DriverType` defaults to `"Serial"` in both the options class and the installation template,
  and `Enabled` is now `true` — a terminal with no indicator wired up reports `Disconnected`
  and the operator enters the weight, which is recorded as manual.
- The DI mapping is explicit: `Serial` → `WeightIndicatorService`, `Simulator` →
  `WeightIndicatorSimulator`, `Disabled` → placeholder. **Anything else resolves to the
  placeholder**, which reports "not connected" and weighs nothing, and logs an error naming
  the offending value and the three valid ones.

The comment at `HardwareServiceCollectionExtensions.cs:37-43` records why, so the fallback is
not "simplified" back to the simulator by a later reader.

### Verification

`tests/WeighBridge.Tests/DependencyInjection/HardwareRegistrationTests.cs` covers the mapping
including the unrecognised-value case. `scripts/serial-absent-log-check.ps1` runs the
application on the new shipped default (`Serial`, `COM1`, nothing attached) and confirms it
reports a disconnected indicator rather than producing weights — see [F-029](#f-029).

### Residual — this machine is still on the simulator

**The fix changes the template used to create an `appsettings.json` that does not exist yet.
It cannot change one that already exists, and must not.** The installation on this machine was
created before the fix, and its file still reads:

```json
"WeightIndicator": { "Enabled": true, "DriverType": "Simulator", … }
```

So the application as installed here **still runs on the simulator**, and every weight it
displays or records is generated. Deliberately not edited: an operator's configuration file is
their data, and silently rewriting it is exactly the class of act this audit is meant to catch.
Any existing installation must have that one key changed to `Serial` (or `Disabled`) before it
is used for anything real. This is the first item in
[Before this is used for real work](#before-this-is-used-for-real-work).

---

## F-028

**P1 — generated JPEGs were filed against weighments as vehicle photographs, and cameras
being configured off did not stop it.** Fixed, verified. Supersedes [F-013](#f-013).
Component: `src/WeighBridge.Hardware/DependencyInjection/HardwareServiceCollectionExtensions.cs`,
`src/WeighBridge.Hardware/Cameras/CameraService.cs`

### Description

There is no capture implementation behind `CameraService`. It **generates** a small synthetic
JPEG, and that image is written to `Captures\` and recorded in `WeighmentImages` with a
checksum, a stage (`FirstWeight` / `SecondWeight`) and a camera name — indistinguishable, in
the database, from a photograph of a vehicle on the platform.

The DI factory resolved `CameraService` unconditionally. It read `IOptions<CameraOptions>` and
then ignored it, so a site with `Camera:Enabled = false` still got generated photographs filed
against its weighments. That is the concrete form of the status defect recorded as
[F-013](#f-013) ("reports connected when it is not"): the status was not merely wrong, it was
describing a device that does not exist.

### Evidence

From the live database, before the fix, for weighment `WB-000001`:

| Stage | File | Bytes | Source |
|---|---|---|---|
| `FirstWeight` | `WB-000001_Camera_1_FirstWeight_20260818_044314_140e95d7.jpg` | 328 | `Simulator` |
| `SecondWeight` | `WB-000001_Camera_1_SecondWeight_20260818_044316_52986438.jpg` | 329 | `Simulator` |

Both files are present in `Captures\`, both sizes match the `FileSizeBytes` column exactly,
and both carry a SHA-256 checksum. A 328-byte JPEG cannot be a photograph of a truck; a real
capture at any usable resolution is tens to hundreds of kilobytes. The `Source` column
recording `Simulator` is the one honest part of the record, and nothing surfaces it to the
operator.

### Fix

`ICameraService` resolves to `CameraService` only when `CameraOptions.Enabled` is true, and to
`PlaceholderCameraService` otherwise; and the installation template now writes
`Camera:Enabled = false`, so a new installation files no images until a real capture device is
configured. The comment at `HardwareServiceCollectionExtensions.cs:80-84` states that what
this service files is synthetic and why it is gated.

### Verification

`tests/WeighBridge.Tests/DependencyInjection/HardwareRegistrationTests.cs` asserts both
resolutions. Retracted from an earlier draft of this report: a claim that
`WeighmentImages.FilePath` was empty on both rows and the images were therefore unreachable
from the database. That was **my own script's defect** — the column is `RelativeFilePath`, my
query read a non-existent `FilePath`, and the empty string it returned was the bug. Both paths
resolve correctly under `Captures\` and both files are byte-for-byte the size the database
records. The image chain is intact; what is wrong with the images is that they are generated.

### Residual — same as [F-027](#f-027)

This machine's existing `appsettings.json` has `Camera:Enabled = true`, so this installation
**still files generated JPEGs against every weighment**. The two 328/329-byte files above are
still in `Captures\` and still referenced by the restored database. Not edited, for the same
reason.

---

## F-029

**P2 — an absent serial port wrote roughly 170,000 log lines a day, rolling away the 30 days
of history the site is configured to keep.** Fixed, verified.
Component: `src/WeighBridge.Hardware/WeightIndicators/WeightIndicatorService.cs`,
`src/WeighBridge.Hardware/WeightIndicators/SerialPortTransport.cs`

### Description

A terminal whose indicator is not connected — a port typed wrong, a cable pulled, or a site
not yet wired up — retries for as long as the application runs. Every attempt wrote the same
six lines (`Connecting to serial transport…`, `Failed to open serial port…`, `Serial
connection lost or failed…`, `Waiting 3000ms before reconnecting…`, and two more), once every
three seconds. That is about 120 lines a minute, ~170,000 a day: it rolls the 20 MB log in
hours and leaves the site with the last few hours of history instead of the thirty days it is
configured to keep.

This became reachable *because of* [F-027](#f-027)'s fix. Making `Serial` the shipped default
is correct, and it means the very first thing a new installation does on a machine with no
indicator is enter this loop. A correct fix that turns a dormant defect into the default
experience is not a reason to revert it; it is a reason to fix the second defect.

### Fix

The condition is reported **in full when it starts, then quietly, then in full again every
hundredth attempt** so that a log opened during a long outage still says what is wrong:

```csharp
var loud = consecutiveFailures == 0 || consecutiveFailures % 100 == 0;
_logger.Log(loud ? LogLevel.Information : LogLevel.Debug, …);
```

`SerialPortTransport` applies the same rule to its own open failure with
`_consecutiveOpenFailures`. The suppressed lines drop to `Debug` rather than being deleted, so
they are still available by lowering the configured level — the information is not lost, it is
just not written 170,000 times.

### Verification

`scripts/serial-absent-log-check.ps1` — sets the shipped default (`Enabled`, `Serial`,
`COM1`), opens Vehicle Entry, holds it for 40 seconds (about 13 reconnect attempts), then
counts what actually landed in the log by byte offset. It asserts at most **one** report of
the absent port and at most **one** error-level line, and it counts *all* error-level lines,
not just serial ones, so a run that fills the log with something else is not a passing run.
Exit 0.

Before: 6 lines × ~13 attempts. After: 1. The script restores `appsettings.json` and
`userpreferences.json` in a `finally` block.

---

## F-030

**P1 — there is no audit table. The audit trail exists only in a rolling text log.**
Open — recorded, not built.

### Description

`IAuditLogger.Record(...)` is called throughout the application — every master-data write,
every weighment transition, every report generation, every user-management action. The role
descriptions shown in Administration advertise that "every change is audited". An
`Audit.View` permission is declared and granted.

The database contains no audit table. The complete schema is:

```
__EFMigrationsHistory, Weighments, Materials, Parties, VehicleTypes,
Vehicles, WeighmentImages, Users
```

Every audit record is written to `%LOCALAPPDATA%\WeighBridge Modern\Logs\weighbridge-<date>.log`
and nowhere else. The consequences:

- **It is not queryable.** "Who cancelled slip WB-000123, and when" requires grepping text
  files, and only if they are still there.
- **It rolls.** The log is a 20 MB rolling file with 30 days of retention. An audit trail with
  a retention policy set by log-file housekeeping is not an audit trail. Before
  [F-029](#f-029) it rolled in *hours* on a terminal with no indicator attached — so on such a
  terminal the audit history was effectively minutes deep.
- **It is per-terminal and locally writable.** The file sits in the operator's own
  `%LOCALAPPDATA%`, editable by the account being audited.
- **`Audit.View` can never be enforced meaningfully** ([F-007](#f-007)) because there is
  nothing to show.

### Why it is recorded rather than fixed

An audit table is a new feature: an entity, a migration, a repository, a write path on every
`Record` call, and a viewer to make `Audit.View` mean something. The brief for this audit is
explicit — *do not add new product features* — and this is the largest single piece of work
identified by it. It is recorded here, in `ImplementationStatus.md` and in `AI-Handoff.md` as
the top functional gap.

What can be said in the meantime: audit entries **do** now name the operator who signed in
rather than the Windows account ([F-020](#f-020)), so the file-based trail at least attributes
correctly. That was a prerequisite for a persisted trail being worth anything.

### Consequence for release

For any site with a compliance or dispute-resolution requirement on the weighment history —
which is most of them, since that is what a weighbridge slip is *for* — this is a blocking
gap, not a nice-to-have.

---

## F-031

**P2 — only two foreign keys exist in the whole schema; all four of a weighment's master
references are unconstrained.** Open.

### Description

`PRAGMA foreign_key_list` across every table returns exactly two constraints:

| Constraint | On delete |
|---|---|
| `Vehicles.VehicleTypeId → VehicleTypes.Id` | `SET NULL` |
| `WeighmentImages.WeighmentId → Weighments.Id` | `CASCADE` |

`Weighments.PartyId`, `Weighments.MaterialId`, `Weighments.VehicleId` and
`Weighments.VehicleTypeId` are all indexed and none is constrained. Nothing at the database
level prevents a weighment referencing a party that does not exist, and nothing defines what
happens to a weighment's references when a master row is deleted.

Mitigating, and the reason this is P2 rather than P1: master deletion is a **soft** delete
(`IsDeleted`, with soft-delete-aware filtered unique indexes on the name columns), so rows are
not actually removed and dangling references are not currently being created. The risk is
structural — the invariant depends entirely on application code, on every path, forever, and
`PRAGMA foreign_keys` is a per-connection setting that nothing verifies at startup.

`PRAGMA foreign_key_check` on the live database: **clean**. No dangling reference exists today.

### Not fixed

Adding four foreign keys is a migration against a schema whose entity configuration is
outside the scope of a defect audit, and on SQLite it requires a table rebuild. Recorded with
the recommendation that the constraints be added with the audit table
([F-030](#f-030)), since that migration has to be written anyway.

---

## F-032

**P2 — "Show in Explorer" was handed the status message instead of the file path.** Fixed.
Component: `src/WeighBridge.App/ViewModels/ReportsViewModel.cs`

`GenerateAsync` stored `result.Message` — the string `"Report generated."` — in
`_lastGeneratedPath`. **Show in Explorer** then ran
`explorer.exe /select,"Report generated."`, which opens a default Explorer window and selects
nothing, and the operator was never told where the file had gone. The status line said only
"Report generated successfully: Report generated."

Fixed to read `result.OutputPath`, and the status line now names the file:
`Report saved to C:\…\Reports\DailyWeighments_20260818_111529.csv`. Confirmed at runtime in
`scripts/privilege-enforcement-check.ps1`, whose first run still showed the old text and so
also caught that the binary under test was stale — the run was repeated after a rebuild.

In scope as a P2 because the export feature is complete and shipped, and this is that
feature being wrong rather than absent.

---

## Navigation — phase 7

**Result: navigation is reliable.** `scripts/navigation-audit.ps1`, exit 0. All seven modules
— Dashboard, Vehicle Entry, Duplicate Slip, Reports, Masters, Settings, Administration —
navigated three times each in one session, 21 navigations, plus 24 more in the retention pass.

Every navigation is confirmed by a *delta* in the count of `Navigated to XViewModel` lines in
that run's log slice, never by the rail highlight. Two reasons, both learned from defects
already in this report: `SelectionItemPattern.Select()` sets `IsChecked` without running the
navigation command, so a highlight assertion passes on a module that never loaded; and the
shell restores a module at startup, so "the log mentions this module" is already true before
any click. Counting up from a per-run baseline survives both. The Dashboard shows 4 rather
than 3 because it is also the startup module on the fresh database each run creates.

The brief said not to trust the existing navigation smoke script, and it should not be trusted
for this phase — not because it is wrong (a previous pass repaired its own false pass) but
because it clicks each module exactly once, so no defect that first appears on a second or
third visit is within its reach. That is exactly the shape of [F-019](#f-019), which this
phase found.

Two defects were found by this phase: [F-019](#f-019) (fixed) and the reproduction that
reclassified [F-006](#f-006) from P1 to P3.

Data safety, as with the startup audit: the live database is moved aside and put back rather
than deleted ([F-012](#f-012)), and the operator's preferences file is restored byte for byte.
`Save Preferences` is never clicked, so the operator's theme is not touched.

---

**Result: startup is reliable.** `scripts/startup-cycle-audit.ps1`, exit 0, 10 of 10 cycles
passed. Cycle 1 runs against no database at all (phase 3, clean cold start including the
migration from nothing); cycles 2–10 run against the database cycle 1 created (phase 4);
the ten cycles together are phase 5.

Each cycle is judged on six things, none of them the application's own success message: the
shell window appeared and identified itself by `AutomationId`, a module was navigated to
(confirmed from the log, because the content area is filled after the window is shown), the
weight indicator connected, it was released exactly once and before the shutdown marker, the
cycle's log delta contains no `[ERR]`/`[CRIT]`/`[FATAL]` line, and the process exited on its
own within 20 s of `WM_CLOSE` with code 0.

| | Time to shell |
|---|---|
| Cycle 1, clean database | 3172 ms |
| Cycles 2–10, existing database | 2591–3420 ms |
| Mean | 2843 ms |

Re-run after [F-004](#f-004) and [F-005](#f-005) were fixed: exit 0, 10 of 10 again, with
cycle 1 at 5617 ms (it now completes first-run administrator setup), cycles 2–10 at
3130–3303 ms and a 3476 ms mean. The ~630 ms rise in the warm cycles is PBKDF2 at sign-in
and is deliberate. The re-run also added a seventh per-cycle assertion: first-run setup ran
exactly once, in cycle 1.

No arbitrary sleeps: every wait polls for the condition it is waiting for and fails on a
deadline. The live database is moved aside and restored in a `finally` block rather than
deleted (see [F-012](#f-012)), and the operator's preferences file is restored byte for
byte. Both were confirmed intact after the run — slips `WB-000001`–`WB-000003` present,
`LastModule` back to `Dashboard`.

Two defects were found and fixed by this phase: [F-015](#f-015) (shutdown ordering) and
[F-003](#f-003) (the login helper, whose failure rate this phase is what finally measured).
One defect was found in the phase's own instrument: [F-018](#f-018).

Not covered by these ten cycles: startup with a *corrupt* database rather than an absent one
(covered afterwards by [F-023](#f-023)/[F-024](#f-024) and
`scripts/corrupt-database-startup.ps1`) and startup of the published Release build — now
covered by [Published application — phase 23](#published-application--phase-23).

## Database — phase 26

Run against the **live** installation database, read-only, with no writes of any kind. Only
PowerShell 5.1 is present and there is no `sqlite3` CLI, so the queries were made with
Python's `sqlite3` module over a `file:…?mode=ro` URI.

| | |
|---|---|
| File | `%LOCALAPPDATA%\WeighBridge Modern\Data\weighbridge.db`, 163,840 bytes |
| `journal_mode` | `wal` |
| `integrity_check` | **ok** |
| `foreign_key_check` | **clean** |
| Migrations | 4, all EF Core 8.0.11: `AddVehicleEntry`, `AddMasters`, `AddWeighmentImages`, `AddUsers` |
| Tables | `__EFMigrationsHistory`, `Weighments`, `Materials`, `Parties`, `VehicleTypes`, `Vehicles`, `WeighmentImages`, `Users` |

**Indexes.** A unique index on `Weighments.SlipNumber` — the constraint that matters most,
since a duplicate slip number is a billing dispute. Soft-delete-aware *filtered* unique
indexes on the master name columns and on `Users.Username`, which is the correct shape: a
soft-deleted row must not block the reuse of its name, and a plain unique index would.

**Foreign keys.** Two in the entire schema — recorded as [F-031](#f-031).

**No audit table** — recorded as [F-030](#f-030). This is the most significant finding of the
phase and was invisible from the application, which reports audit writes as succeeding because
they do succeed: into a text file.

### Password storage — the mandated checks

| Check | Result |
|---|---|
| Hash format | `pbkdf2-sha256$600000$<salt>$<hash>`, 90 characters, self-describing |
| Salt | Distinct per user (compared across both accounts present) |
| Plaintext anywhere in the database | **None.** Every text column of every table was swept for the four passwords used during this audit (`WbAudit-Local-2026`, `Audit#123`, `admin123`, `Audit#456`) — no hit |
| Password in `appsettings.json` | **None.** Top-level keys are `Application`, `Database`, `Logging`, `Hardware`, `Camera`, `Printer`, `Server`, `Reporting` — there is no credential of any kind in the file |
| Password in the log | **None.** Nothing logs a password value, in any branch, including failure paths |
| `admin` / `admin123` seed | **Absent.** Removed under [F-004](#f-004); the application requires first-run administrator setup instead. Re-verified in this phase against the live database: the `admin` row's hash is PBKDF2 with a per-user salt, and `admin123` appears nowhere in the file |

### One thing that was checked and is *not* a finding

`PRAGMA foreign_keys` returned `0` on the connection used for this audit. That says nothing
about the application: it is a **per-connection** setting, and this was a read-only Python
connection, not `Microsoft.Data.Sqlite`'s. Reporting it as "the application runs with foreign
keys disabled" would have been a finding manufactured by the measurement instrument. What the
application's connections actually set was not established, and given [F-031](#f-031) — two
constraints total — it makes little practical difference today.

## Published application — phase 23

The audit up to this point ran against the Debug build. This phase builds and audits the
application as it would actually be installed, and produces the artefact the operator opens.

### What was produced

`scripts/publish-app.ps1` — one command, repeatable:

```
powershell -ExecutionPolicy Bypass -File scripts\publish-app.ps1
```

| | |
|---|---|
| Installed to | `%LOCALAPPDATA%\Programs\WeighBridge Modern\` |
| Executable | `WeighBridge.App.exe` |
| Shortcut | `<Desktop>\WeighBridge.lnk`, refreshed on every publish |
| Build | `Release`, `win-x64`, **self-contained** — 153.6 MB, carries its own .NET 8 runtime |
| Data | `%LOCALAPPDATA%\WeighBridge Modern\` — untouched by publishing, and shared with the Debug build |

Self-contained deliberately: a weighbridge terminal should not need a .NET Desktop Runtime
installed, and "please install a runtime first" is not an instruction that survives contact
with a site.

**Not `PublishSingleFile`**, also deliberately, and for a reason found by reading rather than
assumed: in single-file mode `Assembly.Location` is empty, and
`ApplicationInfoService.ReadBuildDate` falls back to `DateTime.Now` when it is
(`ApplicationInfoService.cs:62-66`). Every terminal would then report *today* as its build
date, with no way to tell which build it was running — a silent wrong answer in the one place
you look when diagnosing a site. A folder plus a shortcut is a double-click either way.

The script refuses to overwrite a **running** application unless given `-Force`, and says so:
a publish that killed the app mid-weighment to replace its files would be a data-loss defect
introduced by the deployment tooling.

### Verification of the published build

Both existing runtime scripts were given an `-ExePath` parameter — a two-line change each —
rather than writing new ones, so the published build is judged by exactly the assertions the
Debug build was judged by:

| Script | Result |
|---|---|
| `final-application-smoke.ps1 -ExePath …` | **exit 0** — starts, authenticates through the real login dialog, shell opens and survives, exits with code 0, `Shutdown complete` in the log, and **no `[ERR]`/`[FTL]` line appended by the run** |
| `navigation-audit.ps1 -ExePath …` | **exit 0** — 7 modules × 3 rounds, every navigation confirmed by a `Navigated to <ViewModel>` log-count delta; Dashboard shows a live indicator status; clean shutdown |

Working set after 3 rounds: 357 MB / 563 handles. After 12 further visits to each of two
modules: 355 MB / 569 handles — flat, which is the [F-019](#f-019) leak fix holding in a
Release build. Reported, not asserted, for the reasons given in that finding.

### Not verified

Installation on a **different** machine. Everything above was run on the build machine, which
has the .NET SDK installed, so it cannot distinguish "self-contained works" from "the runtime
was already there". The publish is self-contained by construction (`--self-contained true`,
`-r win-x64`, and the runtime assemblies are present in the output folder), but that a clean
Windows machine runs it has not been demonstrated and should not be claimed.


## Before this is used for real work

In priority order. The first two are configuration on **this machine** and take a minute; the
rest are work.

1. **Turn the simulator off.** `%LOCALAPPDATA%\WeighBridge Modern\appsettings.json` still has
   `Hardware:WeightIndicator:DriverType = "Simulator"`. Until it is `"Serial"` (or
   `"Disabled"`), every weight this installation shows or records is **generated**
   ([F-027](#f-027)).
2. **Turn the camera off**, or wire up a real one. The same file has `Camera:Enabled = true`,
   and the only camera implementation *generates* its images — 328-byte JPEGs are being filed
   against weighments as vehicle photographs ([F-028](#f-028)).
3. **Build the audit trail** ([F-030](#f-030)). There is no audit table; the trail is a
   rolling text file in the operator's own profile. For any site that has to answer "who
   changed this slip and when", this is the blocking gap.
4. **Add sign-out** ([F-022](#f-022)). A weighbridge terminal is shared across shifts and
   there is currently no way to end a session other than closing the application. Every
   action after a shift change is attributed to whoever signed in first.
5. **Give the App and Printing layers a test project** ([F-010](#f-010)). Two security
   controls — the user-management gate and the reprint gate — are today proven only by
   PowerShell UIA scripts. Delete those scripts and nothing catches a regression.
6. **Add the four missing foreign keys** ([F-031](#f-031)), ideally in the same migration as
   the audit table.

## Outstanding

Phases run: 1–7 (startup, login, shell, database, navigation), 16–17 (settings persistence,
service-layer permissions), 22 (test-the-tests), 23 (published application), 25 (hardcoded
credentials / paths / ports / fake data), 26 (database), 29–32 (severity review, repairs,
final regression, documentation).

Not run, and stated as not run rather than left to look covered:

- **P8–P11** — Vehicle Entry A–K as a full functional pass, master data CRUD, and the weight
  indicator and camera as *features* rather than as the configuration defects found in
  [F-027](#f-027)/[F-028](#f-028). The workflow was exercised end to end enough to produce
  weighments and images (that is where the F-028 evidence came from), but the acceptance
  criteria for each sub-feature were not walked one by one.
- **P12–P15** — printing beyond the permission gate ([F-026](#f-026)); duplicate slip beyond
  the same; report *content* correctness (the CSV was proven to be written or refused, not
  that its columns and totals are right); dashboard figures.
- **P18–P19** — audit-log *content* per operation, and concurrency. Audit content is largely
  moot until [F-030](#f-030) is addressed; concurrency was not exercised at all — no two
  instances were run against one database.
- **P20–P21** — UI/UX pass and performance/responsiveness. No arbitrary sleeps were added to
  mask anything, per the brief; equally, no timing was measured.
- **P24** — performance profiling. Only the working-set samples in
  [F-019](#f-019)/phase 23 exist, and they are corroboration, not measurement.

One pre-existing wart noted and not fixed: `scripts/runtime-smoke.ps1:243` has a hard
`Start-Sleep -Seconds 35` waiting for a background tick. It passes, and the brief forbids
*adding* arbitrary sleeps rather than requiring existing ones be removed, so it is recorded
here rather than changed. Also `IApplicationPaths.InstallDirectory` is declared and
implemented but read by nothing (P3, cosmetic).

## Changes made so far

| File | Change | Finding |
|---|---|---|
| `src/WeighBridge.App/ViewModels/AdministrationViewModel.cs` | `Func<IUnitOfWork>` in place of split `IRepository`/`IUnitOfWork`; `AuthoriseUserManagementAsync` gate on all four writes; permission checks in `CanExecute`; log lines for create/disable/enable | F-001, F-002 |
| `src/WeighBridge.App/ViewModels/MainWindowViewModel.cs` | `IPermissionService` injected; Administration omitted from the navigation rail without `UsersManage` | F-002 |
| `src/WeighBridge.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` | Comment recording that `IRepository<T>` is for read-only consumers and why | F-001 |
| `tests/WeighBridge.Tests/Infrastructure/UnitOfWorkTrackerTests.cs` | New — two regression tests pinning the change-tracker rule | F-001 |
| `scripts/privilege-escalation-repro.ps1` | New — runtime reproduction and regression test, inverted exit codes | F-002 |
| `scripts/login-helper.ps1` | `Find-Field` re-acquires the window each pass, re-asserts foreground, polls to a 20 s deadline, and dumps the visible UIA tree on expiry | F-003 |
| `src/WeighBridge.Hardware/WeightIndicators/WeightIndicatorSimulator.cs` | The "disconnected" log line moved inside the `if (cts is not null)` guard | F-015 |
| `src/WeighBridge.App/Bootstrapper.cs` | `ShutdownAsync` releases the weight indicator explicitly, after the background loops and before the saves, instead of leaving it to container disposal | F-015 |
| `scripts/startup-cycle-audit.ps1` | New — phases 3, 4 and 5: clean cold start, existing-database start, ≥10 cycles, six assertions per cycle, database and preferences moved aside and restored | F-012, F-015, F-018 |
| `src/WeighBridge.Infrastructure/Persistence/DatabaseInitializer.cs` | The seeded `admin` / `admin123` account deleted; the removal commented so it is not restored | F-004 |
| `src/WeighBridge.Core/Security/IAuthenticationService.cs` | `MinimumPasswordLength`, `RequiresInitialSetupAsync`, `CreateInitialAdministratorAsync`, `VerifyPassword` | F-004, F-005 |
| `src/WeighBridge.Services/Security/AuthenticationService.cs` | First-run setup with a server-side duplicate-account and length check; PBKDF2-SHA256, 600 k iterations, 16-byte salt, self-describing hash; legacy digests still verify; `Func<IUnitOfWork>` in place of a captive `IRepository<User>` | F-004, F-005 |
| `src/WeighBridge.App/Dialogs/LoginDialogViewModel.cs` | Setup mode resolved in `InitializeAsync`; title, header, prompt and button label bound to it; `LoginSubmission` carries the confirmation; `HasError` raised from the `ErrorMessage` setter | F-004 |
| `src/WeighBridge.App/Dialogs/LoginDialog.xaml`, `.xaml.cs` | Bound title/header/prompt/button text; `ConfirmPasswordBox`, visible only in setup mode | F-004 |
| `src/WeighBridge.App/Dialogs/DialogService.cs` | `ShowLoginAsync` awaits `InitializeAsync` before constructing the window, so the mode is settled before it is shown | F-004 |
| `src/WeighBridge.App/ViewModels/AdministrationViewModel.cs` | Its own SHA-256 `HashPassword` deleted; hashes through `IAuthenticationService`; enforces the same minimum length | F-005 |
| `tests/WeighBridge.Tests/Security/AuthenticationTests.cs` | New — 13 tests over the real initialiser, a real migrated SQLite database and the real service | F-004, F-005 |
| `scripts/login-helper.ps1` | Recognises the setup form by title and fills it, types the confirmation, reads the dialog's own error banner back when the shell never opens | F-004 |
| `scripts/startup-cycle-audit.ps1` | Asserts first-run setup ran exactly once, in cycle 1 | F-004 |
| `src/WeighBridge.App/ViewModels/DashboardViewModel.cs` | Indicator subscription moved from the constructor to `OnNavigatedToAsync`/`OnNavigatedFromAsync`, with `-=` before each `+=`; the `IDisposable` declaration and the `Dispose()` nothing called removed | F-019 |
| `scripts/navigation-audit.ps1` | New — phase 7: seven modules × 3 rounds, every navigation confirmed by a log count delta rather than the rail highlight; enumerates the Settings module's editable controls (the F-006 reproduction); asserts the Dashboard shows a live indicator status after re-activation; working set reported, never asserted | F-006, F-019 |
| `src/WeighBridge.Core/Security/SignedInOperator.cs` | New — one shared field holding the signed-in operator's account name, written by the permission service and read by the loggers, because a logger that resolved `IPermissionService` would be a dependency cycle | F-020 |
| `src/WeighBridge.Core/Logging/CategoryLoggerBase.cs` | The user field is read from `SignedInOperator` per entry instead of `Environment.UserName` captured once in a singleton's constructor; the Windows account remains the fallback | F-020 |
| `src/WeighBridge.Core/Logging/CategoryLoggers.cs`, `AuditLogger.cs` | All five loggers take and forward the cell as an *optional* parameter, so the 28 existing construction sites compile unchanged and exercise the fallback | F-020 |
| `src/WeighBridge.Core/DependencyInjection/CoreServiceCollectionExtensions.cs` | `SignedInOperator` registered as a singleton, before the loggers | F-020 |
| `src/WeighBridge.Services/Security/PermissionService.cs` | Takes `SignedInOperator` as a *required* parameter, so a missing registration fails at `ValidateOnBuild`; `SetOperator` publishes the operator before its own log line | F-020 |
| `src/WeighBridge.App/ViewModels/MainWindowViewModel.cs` | `CurrentUserName` reads `permissions.CurrentOperator.DisplayName` once in the constructor instead of the Windows account | F-021 |
| `src/WeighBridge.Core/Security/IAuthenticationService.cs`, `src/WeighBridge.Services/Security/AuthenticationService.cs` | `Logout()` deleted — no callers, and its body installed a usable ReadOnly session named after the Windows account rather than ending the session; the `IApplicationInfoService` dependency it orphaned removed with it | F-022 |
| `src/WeighBridge.Core/Security/SecurityOptions.cs`, `IPermissionService.cs`, `src/WeighBridge.Core/Application/IApplicationInfoService.cs`, `PermissionService.cs` | Five stale remarks rewritten: they described the unfinished migration that F-020 *was*, and are the reason it survived review | F-017 |
| `tests/WeighBridge.Tests/Logging/CategoryLoggerTests.cs` | Two regression tests: the enrichment follows the operator across a sign-in from one logger instance, and the audit category names the operator rather than the terminal account | F-020 |
| `tests/WeighBridge.Tests/Security/AuthenticationTests.cs` | Harness rewired to share one `SignedInOperator` between the permission service and its loggers exactly as the container does, with a recording sink; one end-to-end test that a real sign-in reaches the log and the audit trail | F-020 |
| `tests/WeighBridge.Tests/Commands/CommandExecutorTests.cs`, `Commands/DocumentedCommandPatternTests.cs`, `Masters/MasterHarness.cs`, `Weighments/WeighmentHarness.cs` | Construction sites updated for the required `PermissionService` parameter | F-020 |
| `scripts/startup-cycle-audit.ps1` | Fails any cycle where an entry after the sign-in names anyone but the operator, and any cycle where no entry after the sign-in carries a user field at all; signs in with an `-Operator` parameter so the assertion cannot diverge from the credential used | F-020 |
| `scripts/shell-identity-check.ps1` | New — reads the shell's static text over UIA and requires the operator's name present and the Windows account absent | F-021 |
| `src/WeighBridge.Infrastructure/Persistence/DatabaseInitializer.cs` | Guard refusing a zero-byte database file; the failure message now names the database path; class remarks corrected — failure stops startup | F-023, F-024 |
| `src/WeighBridge.App/Bootstrapper.cs` | `InitializeDatabaseAsync` returns the `DatabaseInitializationResult` instead of swallowing a failure; stale "fire-and-forget" remarks corrected | F-024 |
| `src/WeighBridge.App/App.xaml.cs` | `OnStartup` acts on the result: reports the failure against the database, by name, and shuts down rather than opening a login dialog over an unusable database | F-024 |
| `src/WeighBridge.Core/Abstractions/IDatabaseInitializer.cs` | `Succeeded` parameter doc corrected — "the app still starts" was no longer true | F-024 |
| `tests/WeighBridge.Tests/Security/AuthenticationTests.cs` | `EmptyDatabaseFile_IsRefused_NotTreatedAsAFreshInstallation` — asserts the refusal, the message, and that the file is still 0 bytes | F-023 |
| `scripts/corrupt-database-startup.ps1` | New — startup against a garbage, truncated and zero-byte database file; judges what is on screen, that the shell is not reached, the log, that the damaged file is untouched, and that the dialog body names the database | F-023, F-024 |
| `scripts/login-helper.ps1` | `Get-WeighBridgeStartupError` taught the new dialog title, with a note that a title added to the application must be added there too | F-024 |
| `src/WeighBridge.App/Views/AdministrationView.xaml`, `.xaml.cs` | The password `TextBox` replaced with a `PasswordBox`, read through a `PasswordChanged` handler because `PasswordBox.Password` is deliberately not bindable | F-008 |
| `src/WeighBridge.Core/Security/SecurityOptions.cs` | `DefaultRole` changed from `Administrator` to `nameof(Roles.ReadOnly)` | F-014 |
| `src/WeighBridge.Reporting/Services/CsvReportService.cs` | `Reports.Export` enforced in `GenerateAsync`, before the writer opens; refusal logged with operator, role and reason and returned as `ReportResult.Failure` so the operator is told why | F-025 |
| `tests/WeighBridge.Tests/Reporting/CsvReportServiceTests.cs` | Three cases added to the existing suite — a `[Theory]` over `Operator`/`ReadOnly` asserting the refusal **and that no file exists**, plus the `Supervisor` counterpart so a gate that refused everybody would not pass | F-025 |
| `src/WeighBridge.App/ViewModels/ReportsViewModel.cs` | `_lastGeneratedPath` reads `result.OutputPath` instead of `result.Message`; the status line names the file it wrote | F-032 |
| `src/WeighBridge.Printing/Services/WindowsPrintService.cs` | `Weighment.Reprint` enforced in `PrintAsync`, guarded by a shared `IsDuplicate(data)` helper that `DrawSlip`'s DUPLICATE stamp now also reads, so gate and stamp cannot disagree | F-026 |
| `src/WeighBridge.Core/Configuration/HardwareOptions.cs` | `DriverType` default `Simulator` → `Serial`; `Enabled` → `true` | F-027 |
| `src/WeighBridge.Settings/Configuration/DefaultConfiguration.cs` | Installation template: `Hardware:WeightIndicator:DriverType` → `Serial`, `Camera:Enabled` → `false` | F-027, F-028 |
| `src/WeighBridge.Hardware/DependencyInjection/HardwareServiceCollectionExtensions.cs` | Driver mapping made explicit — anything unrecognised resolves to the placeholder and logs an error naming the offending value and the three valid ones, instead of silently becoming the simulator; `ICameraService` resolves to `CameraService` only when `CameraOptions.Enabled` | F-027, F-028 |
| `tests/WeighBridge.Tests/DependencyInjection/HardwareRegistrationTests.cs` | Three tests added to the existing resolution suite: driver chosen by name and never falling back to the simulator, nothing-configured is not the simulator, camera honours `Enabled` | F-027, F-028 |
| `src/WeighBridge.Hardware/WeightIndicators/WeightIndicatorService.cs`, `SerialPortTransport.cs` | Reconnect reporting: full on the first failure and every hundredth, `Debug` in between, so the lines are suppressed by volume rather than deleted | F-029 |
| `scripts/privilege-enforcement-check.ps1` | New — runtime reproduction and regression test for both service-layer gates, driven through a ReadOnly probe account it creates and deletes; 15 `$failures +=` sites re-routed through an `Add-Failure` function after the function-local assignment bug was found in it | F-025, F-026, F-018 |
| `scripts/serial-absent-log-check.ps1` | New — runs the shipped default with nothing on COM1 and counts by byte offset: at most one report of the absent port and at most one error-level line, counting *all* error levels | F-029 |
| `scripts/publish-app.ps1` | New — self-contained Release publish to `%LOCALAPPDATA%\Programs\WeighBridge Modern` with a Desktop shortcut; refuses to overwrite a running instance without `-Force` | phase 23 |
| `scripts/final-application-smoke.ps1`, `scripts/navigation-audit.ps1` | `-ExePath` added, so the published build is judged by exactly the assertions the Debug build was judged by rather than by a new script written for it | phase 23 |

Build: 0 warnings, 0 errors. `dotnet test`: **531 passed, 0 failed, 0 skipped**, and
`dotnet test --list-tests` enumerates 531 cases, so the pass count is the whole suite and not
a filtered subset.

The audit's own regression tests, counted by *case* rather than by method — a `[Theory]` with
six `InlineData` rows is six:

| Where | Cases | Finding |
|---|---|---|
| `Security/AuthenticationTests.cs` (new file) | 17 | F-004, F-005, F-020, F-023 |
| `DependencyInjection/HardwareRegistrationTests.cs` (added to an existing file) | 10 | F-027, F-028 |
| `Reporting/CsvReportServiceTests.cs` (added to an existing file) | 3 | F-025 |
| `Infrastructure/UnitOfWorkTrackerTests.cs` (new file) | 2 | F-001 |
| `Logging/CategoryLoggerTests.cs` (added to an existing file) | 2 | F-020 |

34 cases. The "499 before" and "516" figures quoted in earlier drafts of this section came
from run logs at points in the audit, not from counting, and they do not reconcile with
531 − 34; the audit also *deleted* tests, with `AuthenticationService.Logout()`
([F-022](#f-022)). Only the measured figure is asserted here: **531 passing, 0 failing.**
`scripts/privilege-escalation-repro.ps1`: exit 0 (blocked).
`scripts/startup-cycle-audit.ps1`: exit 0 (10 of 10 cycles), re-run after F-004/F-005: exit 0
(10 of 10), again after [F-020](#f-020) with the new attribution assertion: exit 0
(10 of 10), and again after [F-024](#f-024) changed the startup path every launch goes
through: exit 0 (10 of 10).
`scripts/navigation-audit.ps1`: exit 0, exit 0 again after [F-019](#f-019), and exit 0
against the published Release build (phase 23).
`scripts/shell-identity-check.ps1`: exit 0.
`scripts/corrupt-database-startup.ps1`: 1 of 3 variants passing on the first run
([F-023](#f-023)), exit 0 on all 3 after the repairs.
`scripts/privilege-enforcement-check.ps1`: both gates open on the first run
([F-025](#f-025), [F-026](#f-026)), exit 0 after the repairs.
`scripts/serial-absent-log-check.ps1`: exit 0 (1 report of the absent port, down from
~6 × 13 attempts).
`scripts/final-application-smoke.ps1`: exit 0 on the Debug build and exit 0 on the published
Release build via `-ExePath` (phase 23).
`scripts/publish-app.ps1`: 153.6 MB self-contained install, shortcut written, exit 0.
Phase-22 defect injection, seven in total, each removed and each verified removed by
`grep -rn AUDIT-PHASE22 src/ tests/ scripts/`: two against the F-004/F-005 tests, one against
the Dashboard subscription, two against [F-020](#f-020) (the enrichment read, which failed
exactly the three new tests and 10 of 10 startup cycles; and the publish in `SetOperator`,
which failed the wiring test only), one against [F-021](#f-021) (which put `dell` back on
screen and failed `shell-identity-check.ps1`), and one against [F-023](#f-023) (the zero-byte
guard made unreachable, which failed exactly its one regression test). Every injection failed
the assertions it was aimed at and nothing else.
