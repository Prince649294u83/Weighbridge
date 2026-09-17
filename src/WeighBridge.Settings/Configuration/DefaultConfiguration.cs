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
                    ["Enabled"] = true,
                    // "Simulator" here, which is what shipped, meant a fresh installation
                    // weighed vehicles with generated numbers. A terminal with no indicator
                    // wired up now reports Disconnected and the operator enters the weight,
                    // which is recorded as manual. The simulator is still available by
                    // setting this to "Simulator" deliberately.
                    ["DriverType"] = "Serial",
                    ["PortName"] = "COM1",
                    ["BaudRate"] = 9600,
                    ["DataBits"] = 8,
                    ["Parity"] = "None",
                    ["StopBits"] = "One",
                    ["Protocol"] = "GenericAscii",
                    ["PollIntervalMilliseconds"] = 250,
                    ["StabilitySampleCount"] = 5,
                    ["StabilityToleranceKg"] = 5.0,
                    ["StabilityDurationMs"] = 1000,
                    ["DtrEnable"] = true,
                    ["RtsEnable"] = true,
                    ["Handshake"] = "None",
                    ["ReadTimeoutMs"] = 5000,
                    ["AutoReconnect"] = true,
                    ["ReconnectIntervalMs"] = 1500,
                    ["Unit"] = "kg",
                    ["Decoding"] = new JsonObject
                    {
                        ["WeightDigits"] = 7,
                        ["DecimalPlaces"] = 1,
                        ["DigitsToRemoveFromEnd"] = 0,
                        ["ReversePayload"] = false,
                        ["DummyZero"] = 0,
                        ["ScaleFactor"] = 1.0,
                        ["TargetUnit"] = "kg",
                        ["EndingString"] = "NUL (0x00)",
                        ["HexValue"] = false,
                        ["EssaeMode"] = false,
                        ["RtsCts"] = false,
                        ["BufferData"] = 50,
                        ["StableWaitTime"] = 0,
                    },
                },
                ["PortSettings"] = new JsonObject
                {
                    ["ReceivePort1"] = new JsonObject { ["Enabled"] = false, ["PortName"] = "COM4", ["BaudRate"] = 9600 },
                    ["ReceivePort2"] = new JsonObject { ["Enabled"] = false, ["PortName"] = "COM5", ["BaudRate"] = 9600 },
                    ["SendDataPort"] = new JsonObject { ["Enabled"] = false, ["PortName"] = "COM6", ["BaudRate"] = 9600 },
                },
            },

            ["Camera"] = new JsonObject
            {
                // Off by default: the only camera implementation generates its images, so
                // enabling it files a synthetic JPEG against the weighment as though it were
                // a photograph of the vehicle. Turn this on when a real capture device is
                // wired up. An installation created before this change has true in its own
                // appsettings.json and keeps it - this template is only used to write a file
                // that does not exist yet.
                ["Enabled"] = false,
                ["CaptureOnWeighment"] = true,
                ["ImageQuality"] = 80,
                ["RetentionDays"] = 90,
                ["Devices"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["Name"] = "Camera 1",
                        ["Enabled"] = true,
                    },
                },
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

            ["Company"] = new JsonObject
            {
                ["CompanyName"] = string.Empty,
                ["AddressLine1"] = string.Empty,
                ["AddressLine2"] = string.Empty,
                ["Phone"] = string.Empty,
                ["Email"] = string.Empty,
                ["TaxId"] = string.Empty,
            },

            ["Email"] = new JsonObject
            {
                ["Enabled"] = false,
                ["Frequency"] = "Email only Final Entry",
                ["PdfEnabled"] = true,
                ["SenderName"] = string.Empty,
                ["SenderEmail"] = string.Empty,
                ["Password"] = string.Empty,
                ["SmtpServer"] = string.Empty,
                ["SmtpPort"] = 587,
                ["UseSsl"] = true,
                ["Recipients"] = new JsonArray(),
            },

            ["Sms"] = new JsonObject
            {
                ["Enabled"] = false,
                ["Provider"] = "GsmModem",
                ["DefaultRecipient"] = string.Empty,
                ["MessageFrequency"] = "SMS Both Entry",
                ["WhatsAppToken"] = string.Empty,
                ["GsmPortName"] = "COM4",
                ["GsmBaudRate"] = 9600,
            },

            ["Weighment"] = new JsonObject
            {
                ["UnitBagsWeightColumn"] = false,
                ["ManualTareEntry"] = true,
                ["AutoTareWeight"] = true,
                ["SecondEntryCharges"] = false,
                ["GstOnCharges"] = false,
                ["OnlySingleEntry"] = false,
                ["PriceComputing"] = false,
                ["DisconnectTimeSeconds"] = 300,
                ["AllowZeroNetWeight"] = false,
                ["AutoApplicationShortcut"] = true,
                ["AutoUpdateTareWeight"] = false,
                ["WeightHold"] = false,
                ["TimeFormat"] = "12 Hour",
                ["PrintQrCode"] = false,
                ["ChargesMandatory"] = false,
                ["MinimumCharges"] = 0,
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
