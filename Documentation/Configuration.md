# Configuration

## Where files live

Everything writable lives under a per-user data root, because the installation directory is
assumed to be read-only:

```
%LOCALAPPDATA%\WeighBridge Modern\
├── appsettings.json          application configuration
├── userpreferences.json      per-operator UI state
├── Logs\weighbridge-{date}.log
├── Data\weighbridge.db
├── Reports\
└── Captures\
```

Paths are resolved through `IApplicationPaths`, never composed inline. The root is
constructor-overridable, which is how the test suite redirects every file into a temporary
folder.

## Self-healing behaviour

The application must never fail to start because of configuration. `ConfigurationProvisioner`
runs before the configuration system reads anything, and reports one of four outcomes:

| Outcome | Situation |
| --- | --- |
| `Created` | No file. Written from documented defaults. |
| `Unchanged` | File present and complete. |
| `Repaired` | File missing keys a newer build introduced. They are added; operator values are left untouched. |
| `Recovered` | File unreadable or not a JSON object. It is quarantined for support, then regenerated. |

Both this file and `userpreferences.json` are written atomically — to a `.tmp`, then
`File.Move(overwrite: true)` — so an interrupted write can never leave a half-written
document behind.

Configuration sources, in increasing precedence: `appsettings.json`, then environment
variables.

## `appsettings.json` sections

### Application
`Name`, `OrganizationName`, `SiteName`, `Theme` (`System` | `Light` | `Dark`), `Language`,
`StartupModule`, `RestoreWindowPlacement`.

### Database
`Provider` (`Sqlite`), `ConnectionString` (empty means derive from `FileName` under the
data root), `FileName`, `CommandTimeoutSeconds`, `ApplyMigrationsOnStartup`,
`ProbeOnStartup`, `HealthCheckIntervalSeconds`, `EnableSensitiveDataLogging`.

### Logging
Standard `LogLevel` block, plus a `File` block: `Enabled`, `MinimumLevel`,
`FileNamePattern`, `RetainedDays` (default 30), `MaxFileSizeMegabytes`,
`IncludeDebugOutput`.

### Hardware
`WeightIndicator`: `Enabled` (default true with simulator), `DriverType` (`SerialPort` | `Simulator` | `Placeholder`), `PortName` (`COM1`), `BaudRate` (`9600`), `DataBits` (`8`), `Parity` (`None`), `StopBits` (`One`), `Protocol` (`GenericAscii`), `PollIntervalMilliseconds` (`250`), `StabilitySampleCount` (`5`), `StabilityToleranceKg` (`5.0`), `StabilityDurationMs` (`1000`), `AutoReconnect` (`true`), `ReconnectIntervalMs` (`3000`), `Unit` (`kg`).

### Camera
`Enabled` (default true), `CaptureOnWeighment` (`true`), `ImageQuality` (`80`), `RetentionDays` (`90`), `Devices` (array of `{ "Name": "Camera 1", "Enabled": true }`). Snapshot JPEGs are stored in `%LOCALAPPDATA%\WeighBridge Modern\Captures` with SHA-256 integrity checksums stored in the `WeighmentImages` database table.

### Printer
`Enabled` (default false), `DefaultPrinterName`, `SlipTemplate`, `CopyCount`, `PaperSize`,
`ShowPrintDialog`, `PreviewBeforePrint`.

### Server
`Enabled` (default false), `BaseUrl`, `ApiKey`, `TerminalId` (defaults to the machine
name), `SyncIntervalSeconds`, `TimeoutSeconds`, `HealthCheckIntervalSeconds`.

### Reporting
`DefaultExportFormat`, `OutputDirectory`, `OpenAfterExport`, `MaxRowsPerReport`.

Hardware, camera, printer and server are all disabled by default, so a fresh install runs
on any machine without attached equipment. Their status bar indicators read *Disabled*
rather than *Disconnected*, which distinguishes "not configured" from "configured but
unreachable".

Each section binds to an options class in `WeighBridge.Core.Configuration` through
`AddOptions<T>().Bind(section)`. Adding a key means adding it in both places.

## User preferences

`userpreferences.json` holds per-operator UI state, separate from administrative
configuration:

```json
{
  "Theme": "Dark",
  "IsNavigationCollapsed": false,
  "LastModule": "Reports",
  "Window": {
    "Left": 404, "Top": 180,
    "Width": 1024, "Height": 700,
    "IsMaximized": false
  }
}
```

`Left` and `Top` are nullable, and "not recorded yet" is `null` rather than `double.NaN`.
NaN reads naturally in WPF, where it means *auto*, but it has no JSON representation —
`System.Text.Json` throws rather than writing it, which would have failed every save until
a position happened to be captured. The serializer additionally enables
`AllowNamedFloatingPointLiterals` as defence in depth.

`Left`/`Top`/`Width`/`Height` always describe the **restored** window, even when
`IsMaximized` is true, so un-maximising after a restart returns the window to the size the
operator last chose. A stored position is only reused when enough of the window would fall
on a connected monitor; otherwise the shell centres itself.

## Logging

Daily log file, rolled by date and by size, with a retention purge governed by
`RetainedDays`. Writes go through a background thread so the UI never blocks on disk.

Logged at minimum: startup, shutdown, configuration loading, DI initialisation,
navigation, subsystem status transitions, and every exception.
