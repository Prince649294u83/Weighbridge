using System.Text.Json.Nodes;
using WeighBridge.Core.Application;

namespace WeighBridge.Settings.Configuration;

/// <summary>
/// The default <c>appsettings.json</c> document.
/// </summary>
/// <remarks>
/// Kept as a hand-built <see cref="JsonObject"/> rather than serialised from the
/// options classes so the generated file has a stable, human-friendly key order — an
/// administrator edits this file by hand on the cabin PC.
/// Every key here must correspond to a property on the matching options class in
/// <c>WeighBridge.Core.Configuration</c>.
/// </remarks>
internal static class DefaultConfiguration
{
    /// <summary>Builds the default document for the supplied path layout.</summary>
    public static JsonObject Create(IApplicationPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return new JsonObject
        {
            ["Application"] = new JsonObject
            {
                ["Name"] = "WeighBridge Modern",
                ["OrganizationName"] = string.Empty,
                ["SiteName"] = string.Empty,
                ["Theme"] = "Light",
                ["Language"] = "en-US",
                ["StartupModule"] = "Dashboard",
                ["RestoreWindowPlacement"] = true,
            },

            ["Database"] = new JsonObject
            {
                ["Provider"] = "Sqlite",
                ["ConnectionString"] = string.Empty,
                ["FileName"] = "weighbridge.db",
                ["CommandTimeoutSeconds"] = 30,
                ["ApplyMigrationsOnStartup"] = true,
                ["ProbeOnStartup"] = true,
                ["HealthCheckIntervalSeconds"] = 30,
                ["EnableSensitiveDataLogging"] = false,
            },

            ["Logging"] = new JsonObject
            {
                ["LogLevel"] = new JsonObject
                {
                    ["Default"] = "Information",
                    ["Microsoft"] = "Warning",
                    ["Microsoft.EntityFrameworkCore"] = "Warning",
                },
                ["File"] = new JsonObject
                {
                    ["Enabled"] = true,
                    ["MinimumLevel"] = "Information",
                    ["FileNamePattern"] = "weighbridge-{Date}.log",
                    ["RetainedDays"] = 30,
                    ["MaxFileSizeMegabytes"] = 20,
                    ["IncludeDebugOutput"] = true,
                },
            },

            ["Hardware"] = new JsonObject
            {
                ["WeightIndicator"] = new JsonObject
                {
                    ["Enabled"] = false,
                    ["PortName"] = "COM1",
                    ["BaudRate"] = 9600,
                    ["DataBits"] = 8,
                    ["Parity"] = "None",
                    ["StopBits"] = "One",
                    ["Protocol"] = "Generic",
                    ["PollIntervalMilliseconds"] = 250,
                    ["StabilitySampleCount"] = 5,
                    ["Unit"] = "kg",
                },
            },

            ["Camera"] = new JsonObject
            {
                ["Enabled"] = false,
                ["CaptureOnWeighment"] = true,
                ["ImageQuality"] = 80,
                ["RetentionDays"] = 90,
                ["Devices"] = new JsonArray(),
            },

            ["Printer"] = new JsonObject
            {
                ["Enabled"] = false,
                ["DefaultPrinterName"] = string.Empty,
                ["SlipTemplate"] = "Default",
                ["CopyCount"] = 2,
                ["PaperSize"] = "A4",
                ["ShowPrintDialog"] = false,
                ["PreviewBeforePrint"] = true,
            },

            ["Server"] = new JsonObject
            {
                ["Enabled"] = false,
                ["BaseUrl"] = string.Empty,
                ["ApiKey"] = string.Empty,
                ["TerminalId"] = Environment.MachineName,
                ["SyncIntervalSeconds"] = 300,
                ["TimeoutSeconds"] = 30,
                ["HealthCheckIntervalSeconds"] = 60,
            },

            ["Reporting"] = new JsonObject
            {
                ["DefaultExportFormat"] = "Pdf",
                ["OutputDirectory"] = paths.ReportsDirectory,
                ["OpenAfterExport"] = true,
                ["MaxRowsPerReport"] = 100000,
            },
        };
    }
}
