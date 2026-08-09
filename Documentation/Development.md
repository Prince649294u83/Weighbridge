# Development guide

## Prerequisites

- Windows 10 1809 or later
- .NET 8 SDK
- An IDE with WPF support (Visual Studio 2022 17.8+, JetBrains Rider) — optional, the CLI is
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
consequence is that it cannot reference types from `WeighBridge.App` — anything worth
testing belongs in a library project, which is a useful forcing function for the layering.

Current coverage: settings persistence and round-tripping, configuration provisioning and
recovery, navigation history, and the MVVM command primitives.

`TempDataRoot` gives a test its own throwaway data root by constructing
`ApplicationPaths` with an explicit path. Use it for anything that touches disk; never let
a test write to the real `%LOCALAPPDATA%` folder.

## Adding a module

The seven module pages are placeholders. To make one real:

1. **ViewModel** — derive from `ViewModelBase` in `App/ViewModels/`. Take dependencies
   through the constructor. Override `OnNavigatedToAsync` to load data.
2. **View** — a `UserControl` in `App/Views/`, named so the convention resolves it:
   `FooViewModel` → `FooView`. Bind only; no logic in code-behind.
3. **Register** the ViewModel as transient in `Bootstrapper.RegisterViewModels`.
4. **Services** — put business logic in `WeighBridge.Services` behind an interface, and data
   access behind `IRepository<T>`. Not in the ViewModel.
5. **Navigate** with `INavigationService.NavigateToAsync<FooViewModel>()`.

Style the view from existing tokens and controls. If something is genuinely missing from the
design system, add it to the design system — not to the view.

## Database

There are no tables yet, by design. The context, connection handling, migration
infrastructure and health check are all in place; the first business module adds the first
entity and the first migration.

```bash
dotnet ef migrations add <Name> --project src/WeighBridge.Infrastructure --startup-project src/WeighBridge.App
```

`IDesignTimeDbContextFactory` supplies the design-time connection. Until a migration exists,
startup falls back to `EnsureCreatedAsync`, which is why a fresh install logs *no migrations
are defined yet*.

## Conventions

- Interfaces for all services; constructor injection everywhere.
- No service locator, no static mutable state.
- `async`/`await` for anything that touches disk, database or hardware. Never block the UI
  thread.
- Nullable reference types are enabled; do not silence a warning with `!` unless the
  invariant is genuinely local and worth a comment.
- Comment the *why*, not the *what*. Several comments in this codebase record a WPF
  behaviour that cost real debugging time — those are worth keeping.

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
  exception — a fire-and-forget call whose failure was never awaited.
