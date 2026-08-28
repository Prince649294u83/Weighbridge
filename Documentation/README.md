# WeighBridge Modern

Native Windows desktop application for weighbridge operations, rebuilt from the ground up
on .NET 8 LTS and WPF.

**Current state: infrastructure Components 1â€“11 complete, Vehicle Entry working end to end, Master Data subsystem (Vehicles, Parties, Materials, Vehicle Types) fully implemented and integrated, and Real Weight Indicator & Camera Hardware Integration fully operational.** The shell, design system, navigation, configuration, logging, event bus, notification centre, background task manager, health monitoring, command pipeline, undo, busy state, dialogs, validation and permissions are all complete. On top of them, an operator can manage master records, select active masters or type free-form, auto-populate vehicle types and standard tare weights, stream live weight readings with rolling stability detection, capture multi-camera weighment snapshots with SHA-256 integrity, open a weighment, record both weights, get a correct net, cancel with a reason, and find all data preserved after a restart.

## Documentation index

| Document | Contents |
| --- | --- |
| [AI-Handoff.md](AI-Handoff.md) | **Start here if you are an AI agent.** Decisions, pitfalls, what not to change |
| [ImplementationStatus.md](ImplementationStatus.md) | Per-component status, files, test counts, verified results, change record |
| [Architecture.md](Architecture.md) | Layering rules, project responsibilities, startup pipeline, the weighment domain |
| [DesignSystem.md](DesignSystem.md) | Theme tokens, typography, spacing, controls |
| [Configuration.md](Configuration.md) | `appsettings.json`, user preferences, data locations |
| [Development.md](Development.md) | Building, running, testing, adding a module, migrations |

## Requirements

- Windows 10 1809 or later (Windows 11 recommended)
- .NET 8 SDK to build; .NET 8 Desktop Runtime to run

## Quick start

```bash
dotnet build WeighBridge.sln
dotnet run --project src/WeighBridge.App
dotnet test tests/WeighBridge.Tests
```

Runtime verification drives the real application through UI Automation. All must exit 0:

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/runtime-smoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vehicle-entry-smoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/masters-smoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/hardware-smoke.ps1
```

The second, third, and fourth delete the local database and write test records. Development machines only.

## Solution layout

```
WeighBridge.sln
â”œâ”€â”€ src/
â”‚   â”œâ”€â”€ WeighBridge.App             WPF shell: views, view models, theme, dialogs (Vehicle Entry, Masters)
â”‚   â”œâ”€â”€ WeighBridge.Core            Abstractions, MVVM primitives, domain events (no dependencies)
â”‚   â”œâ”€â”€ WeighBridge.Domain          Entities and value objects â€” Weighment, WeighmentImage, Vehicle, Party, Material, VehicleType
â”‚   â”œâ”€â”€ WeighBridge.Infrastructure  EF Core context, migrations, repositories, health checks
â”‚   â”œâ”€â”€ WeighBridge.Services        Navigation, status, command pipeline, undo, busy, permissions, weighments, masters
â”‚   â”œâ”€â”€ WeighBridge.Settings        Configuration provisioning and user preferences
â”‚   â”œâ”€â”€ WeighBridge.Hardware        Weight indicator and camera hardware integration (Serial, Delimited framing, Generic ASCII parser, Stability detector, Simulator, Camera coordinator)
â”‚   â”œâ”€â”€ WeighBridge.Printing        Slip printing abstractions (placeholder)
â”‚   â””â”€â”€ WeighBridge.Reporting       Report generation abstractions (placeholder)
â”œâ”€â”€ tests/
â”‚   â””â”€â”€ WeighBridge.Tests           xUnit suite, headless (net8.0, no WPF)
â”œâ”€â”€ scripts/                        UI Automation runtime verification
â””â”€â”€ Documentation/
```

## What is deliberately not here yet

- **Hardware.** No weight indicator integration and **no simulation of one** either. The
  placeholder is disconnected, "Read indicator" is disabled, and typed weights are recorded as
  manual. Nothing pretends to be a scale.
- **Printing.** Print service renders weighment slips via WindowsPrintService with GDI handle safety; duplicate printing marks the slip as DUPLICATE and is permission-gated (WeighmentReprint).
- **Login and authentication.** Permissions are enforced against a role from configuration.
- **Duplicate slip, reports, administration, dashboard and masters.** These modules are implemented and navigation-tested.
