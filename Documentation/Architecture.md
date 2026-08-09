# Architecture

## Layering

```
Views  →  ViewModels  →  Services  →  Repositories  →  Database
                              ↓
                    Hardware / Printing / Reporting
```

Three rules hold everywhere, and are the reason the layering survives contact with new
modules:

1. **No business logic in Views.** Code-behind is limited to view concerns — a click
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
| `WeighBridge.Core` | net8.0 | Abstractions, MVVM primitives, options records. No solution dependencies. |
| `WeighBridge.Domain` | net8.0 | Entities and value objects. |
| `WeighBridge.Infrastructure` | net8.0 | EF Core context, repositories, unit of work, database health check. |
| `WeighBridge.Services` | net8.0 | Navigation, subsystem status monitoring. |
| `WeighBridge.Settings` | net8.0 | Configuration provisioning, user preferences. |
| `WeighBridge.Hardware` | net8.0 | Weight indicator and camera abstractions (placeholder implementations). |
| `WeighBridge.Printing` | net8.0 | Slip printing abstractions (placeholder). |
| `WeighBridge.Reporting` | net8.0 | Report generation abstractions (placeholder). |
| `WeighBridge.App` | net8.0-windows | WPF shell, design system, dialogs, view models. |
| `WeighBridge.Tests` | net8.0 | xUnit suite. Headless by design — it cannot reference WPF types. |

## Startup pipeline

`App.xaml` → `Bootstrapper` → shell. The order in `Bootstrapper.StartAsync` is fixed
because each step depends on the previous one:

1. **Ensure data folders** — `IApplicationPaths.EnsureCreated()`.
2. **Provision configuration** — `ConfigurationProvisioner` creates `appsettings.json` from
   defaults, or repairs one missing keys, or quarantines and regenerates a corrupt one. The
   application never fails to start because configuration is absent or damaged.
3. **Build configuration** — JSON file plus environment variables.
4. **Start logging** — file provider plus debug provider. Reads its own options directly
   from the configuration root, because the logging builder is not yet available to inject.
5. **Register services** — see below.
6. **Load preferences** — theme, collapsed navigation, last module, window placement.
7. **Apply theme** — before any window exists, so there is no flash of the wrong theme.
8. **Create the shell** — resolved from the container.
9. **Show, then initialise the database** — the health check runs after `Show()` so a slow
   or missing database delays nothing the operator can see.

Shutdown runs in reverse: stop monitoring, save window placement, save preferences, flush
the log.

## Dependency injection

Constructor injection throughout. There is no service locator and no static mutable state.
The container is private to the `Bootstrapper`; only the composition root resolves from it.

It is built with `ValidateOnBuild` and `ValidateScopes` enabled, so a missing registration
or a captive dependency fails loudly at startup rather than at first use.

Registered: configuration and options, `IApplicationPaths`, logging, `ISettingsService`,
`IThemeService`, `INavigationService`, `IDialogService`, `IWindowPlacementService`,
`IViewLocator`, `ISystemStatusService`, the database context factory, `IRepository<>`,
`IUnitOfWork`, health checks, and the hardware, printing and reporting placeholders.
ViewModels are transient; the shell and its ViewModel are singletons.

## Navigation

One window. There is no window switching anywhere — every module is hosted in the shell's
content area.

`NavigationService` holds back and forward stacks and resolves each destination ViewModel
from the container, so a module always gets its dependencies injected. It has no WPF
reference: it raises `Navigated`, and the shell swaps its content in response. That is what
makes the history logic testable headlessly.

`ViewLocator` maps a ViewModel to its View by convention —
`...ViewModels.FooViewModel` → `...Views.FooView` — and caches the result.

## Error handling

Three surfaces are hooked, because WPF reports failures on three different paths:

- `Application.DispatcherUnhandledException` — UI thread. Logged, shown in a custom error
  dialog, marked handled.
- `AppDomain.CurrentDomain.UnhandledException` — background threads. Logged; the process is
  already terminating.
- `TaskScheduler.UnobservedTaskException` — faulted fire-and-forget tasks. Logged and
  observed, so a stray `async void` cannot take the process down from the finalizer thread.

The application should not terminate unexpectedly.

## Window placement

`WindowPlacementService` persists size, position and maximised state.

Two details are load-bearing and were both learned the hard way:

- The tracked rectangle comes from `Window.RestoreBounds`, not from
  `Left`/`Top`/`ActualWidth`/`ActualHeight`. WPF raises `LocationChanged` while
  `WindowState` still reads `Normal`, so reading the live properties during a transition
  captures the *destination* of a maximise (a small negative overshoot) or of a minimise
  (parked near −32000 device pixels) and stores it as the operator's position.
- Shutdown reads a lock-guarded snapshot, never the `Window`. `App.RunShutdown` blocks the
  UI thread awaiting the shutdown chain, so touching a dispatcher-affine property from that
  chain either throws (off-thread) or deadlocks (if marshalled back).

A stored position is only reused when enough of the window would land on a connected
monitor; otherwise the shell centres itself.
