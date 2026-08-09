# WeighBridge Modern

Native Windows desktop application for weighbridge operations, rebuilt from the ground up
on .NET 8 LTS and WPF.

**Current state: Module 0.1 — Foundation & Application Shell.** The shell, design system,
navigation, configuration, logging, dialogs and database plumbing are complete. No
business workflow is implemented yet: every module page is a placeholder.

## Documentation index

| Document | Contents |
| --- | --- |
| [Architecture.md](Architecture.md) | Layering rules, project responsibilities, startup pipeline |
| [DesignSystem.md](DesignSystem.md) | Theme tokens, typography, spacing, controls |
| [Configuration.md](Configuration.md) | `appsettings.json`, user preferences, data locations |
| [Development.md](Development.md) | Building, running, testing, adding a module |

## Requirements

- Windows 10 1809 or later (Windows 11 recommended)
- .NET 8 SDK to build; .NET 8 Desktop Runtime to run

## Quick start

```bash
dotnet build WeighBridge.sln
dotnet run --project src/WeighBridge.App
dotnet test tests/WeighBridge.Tests
```

## Solution layout

```
WeighBridge.sln
├── src/
│   ├── WeighBridge.App             WPF shell: views, view models, theme, dialogs
│   ├── WeighBridge.Core            Abstractions and MVVM primitives (no dependencies)
│   ├── WeighBridge.Domain          Entities and value objects
│   ├── WeighBridge.Infrastructure  EF Core context, repositories, health checks
│   ├── WeighBridge.Services        Application services (navigation, status)
│   ├── WeighBridge.Settings        Configuration provisioning and user preferences
│   ├── WeighBridge.Hardware        Weight indicator and camera abstractions (placeholder)
│   ├── WeighBridge.Printing        Slip printing abstractions (placeholder)
│   └── WeighBridge.Reporting       Report generation abstractions (placeholder)
├── tests/
│   └── WeighBridge.Tests           xUnit suite, headless (net8.0, no WPF)
└── Documentation/
```

## Scope boundary for this module

Deliberately **not** implemented: login and authentication, vehicle entry, duplicate slip,
reports, masters, administration, and any database table. The database layer has a context,
connection, migration infrastructure and a health check, but no mapped entities — schema
arrives with the first business module.
