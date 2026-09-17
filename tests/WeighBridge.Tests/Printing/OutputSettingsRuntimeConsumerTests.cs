using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Printing;
using WeighBridge.Core.Reporting;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Security;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;
using WeighBridge.Printing.Outputs;
using WeighBridge.Printing.Services;
using WeighBridge.Printing.Template;
using WeighBridge.Services.Formatting;

namespace WeighBridge.Tests.Printing;

public sealed class OutputSettingsRuntimeConsumerTests
{
    [Fact]
    public async Task PrintingSettings_EnableDisable()
    {
        var printerMonitor = new MutableOptionsMonitor<PrinterOptions>(new PrinterOptions
        {
            Enabled = false,
            DefaultPrinterName = "DefinitelyMissingPrinter_123"
        });
        var service = CreatePrintService(printerMonitor);

        var disabled = await service.PrintAsync("ReportDocument", new Dictionary<string, object?>
        {
            ["ReportDocument"] = CreateReportDocument()
        });

        Assert.False(disabled.Succeeded);
        Assert.Contains("disabled", disabled.Message, StringComparison.OrdinalIgnoreCase);

        printerMonitor.Update(new PrinterOptions
        {
            Enabled = true,
            DefaultPrinterName = "DefinitelyMissingPrinter_123"
        });

        var enabledButMissingPrinter = await service.PrintAsync("ReportDocument", new Dictionary<string, object?>
        {
            ["ReportDocument"] = CreateReportDocument()
        });

        Assert.False(enabledButMissingPrinter.Succeeded);
        Assert.Contains("not installed", enabledButMissingPrinter.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Dot Matrix Printer")]
    [InlineData("Graphics Printer")]
    [InlineData("Label / Sticker Printer")]
    public async Task PrintingSettings_PrinterType(string printerType)
    {
        var service = CreatePrintService(new MutableOptionsMonitor<PrinterOptions>(new PrinterOptions
        {
            Enabled = true,
            PrinterType = printerType,
            DefaultPrinterName = "DefinitelyMissingPrinter_456"
        }));

        var result = await service.PrintAsync("ReportDocument", new Dictionary<string, object?>
        {
            ["ReportDocument"] = CreateReportDocument()
        });

        Assert.False(result.Succeeded);
        Assert.Contains("DefinitelyMissingPrinter_456", result.Message);
    }

    [Fact]
    public async Task PrintingSettings_DefaultPrinter()
    {
        var service = CreatePrintService(new MutableOptionsMonitor<PrinterOptions>(new PrinterOptions
        {
            Enabled = true,
            DefaultPrinterName = "DefinitelyMissingPrinter_789"
        }));

        Assert.Equal("DefinitelyMissingPrinter_789", await service.GetDefaultPrinterAsync());

        var result = await service.PrintAsync("ReportDocument", new Dictionary<string, object?>
        {
            ["ReportDocument"] = CreateReportDocument()
        });

        Assert.False(result.Succeeded);
        Assert.Contains("not installed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PrintingSettings_Copies(int copies)
    {
        var options = new PrinterOptions { CopyCount = copies };

        Assert.Equal(copies, options.CopyCount);
    }

    [Fact]
    public void PrintingSettings_PaperSize()
    {
        var document = CreateReportDocument();

        var a4 = ReportPrintTextRenderer.Render(document, "A4", sideWisePrinting: false);
        var half = ReportPrintTextRenderer.Render(document, "Half A4 / A5", sideWisePrinting: false);

        Assert.Equal(160, RuleWidth(a4));
        Assert.Equal(96, RuleWidth(half));
    }

    [Fact]
    public void PrintingSettings_SideWise()
    {
        var document = CreateReportDocument();

        var sideWise = ReportPrintTextRenderer.Render(document, "Half A4 / A5", sideWisePrinting: true);
        var normal = ReportPrintTextRenderer.Render(document, "Half A4 / A5", sideWisePrinting: false);

        Assert.Equal(160, RuleWidth(sideWise));
        Assert.Equal(96, RuleWidth(normal));
    }

    [Fact]
    public void PrintingSettings_InvalidValuesRejected()
    {
        var validTypes = new[] { "Dot Matrix Printer", "Graphics Printer", "Label / Sticker Printer" };
        var validSizes = new[] { "A4", "Half A4 / A5" };

        Assert.DoesNotContain("Laser-ish", validTypes);
        Assert.DoesNotContain("A3", validSizes);
        Assert.True(new PrinterOptions { CopyCount = 1 }.CopyCount >= 1);
    }

    [Fact]
    public void TimeFormat_Print()
    {
        using var formatter = new DateTimeFormatter(new MutableOptionsMonitor<WeighmentOptions>(
            new WeighmentOptions { TimeFormat = "12 Hour" }));

        var rendered = ReportPrintTextRenderer.Render(CreateReportDocument(), "A4", true, formatter);

        Assert.Contains("10:05:00 AM", rendered);
    }

    [Fact]
    public void TimeFormat_Live24Hour()
    {
        using var formatter = new DateTimeFormatter(new MutableOptionsMonitor<WeighmentOptions>(
            new WeighmentOptions { TimeFormat = "24 Hour" }));

        Assert.Equal("16:35:12", formatter.FormatTime(new DateTime(2026, 9, 1, 16, 35, 12)));
    }

    [Fact]
    public void WeighbridgeIdentity_Slip()
    {
        var weighment = CreateCompletedWeighment();
        var printData = WeighmentPrintDataFactory.Create(
            weighment,
            new CompanyOptions
            {
                CompanyName = "TEST WB",
                AddressLine1 = "ADDRESS ONE",
                AddressLine2 = "ADDRESS TWO"
            });

        Assert.Equal("TEST WB", printData.CompanyName);
        Assert.Equal("ADDRESS ONE", printData.AddressLine1);
        Assert.Equal("ADDRESS TWO", printData.AddressLine2);
    }

    [Fact]
    public void WeighbridgeIdentity_Report()
    {
        var document = ReportDocument.Create(
            "Known Report",
            "TEST WB",
            new DateTime(2026, 9, 1),
            new DateTime(2026, 9, 1),
            CreateReportDocument().Rows);

        var rendered = ReportPrintTextRenderer.Render(document, "A4", true);

        Assert.Contains("TEST WB", rendered);
    }

    private static WindowsPrintService CreatePrintService(MutableOptionsMonitor<PrinterOptions> printerMonitor)
    {
        var templateEngine = new SlipTemplateEngine();
        return new WindowsPrintService(
            Options.Create(printerMonitor.CurrentValue),
            Options.Create(new CompanyOptions { CompanyName = "Initial WB" }),
            new AllowAllPermissionService(),
            templateEngine,
            new WindowsGdiPrintOutput(templateEngine, NullLogger<WindowsGdiPrintOutput>.Instance),
            new RawSpoolPrintOutput(templateEngine, NullLogger<RawSpoolPrintOutput>.Instance),
            NullLogger<WindowsPrintService>.Instance,
            printerMonitor,
            new MutableOptionsMonitor<CompanyOptions>(new CompanyOptions { CompanyName = "Runtime WB" }));
    }

    private static int RuleWidth(string rendered)
        => rendered.Split(Environment.NewLine).First(line => line.StartsWith("---", StringComparison.Ordinal)).Length;

    private static ReportDocument CreateReportDocument()
    {
        var rows = new[]
        {
            new ReportDocumentRow(
                SerialNumber: 1,
                SlipNumber: "WB-000001",
                VehicleNumber: "MH12AB1001",
                VehicleTypeName: "10 Wheeler",
                PartyName: "Acme Minerals",
                MaterialName: "Iron Ore",
                Charges1: 250m,
                Charges2: 50m,
                TotalCharges: 300m,
                GrossWeightKg: 30_000m,
                TareWeightKg: 10_000m,
                NetWeightKg: 20_000m,
                GrossCapturedAtLocal: new DateTime(2026, 9, 1, 9, 30, 0),
                CompletedAtLocal: new DateTime(2026, 9, 1, 10, 5, 0),
                Status: "Completed")
        };

        return ReportDocument.Create(
            "Deterministic Report",
            "Report Print Tests Ltd",
            new DateTime(2026, 9, 1),
            new DateTime(2026, 9, 1),
            rows);
    }

    private static Weighment CreateCompletedWeighment()
    {
        var weighment = Weighment.Open(
            "MH12AB1001",
            WeighmentMode.GrossFirst,
            partyName: "Acme Minerals",
            materialName: "Iron Ore",
            charges: 250m);
        weighment.RecordFirstWeight(new WeightCapture(30_000m, new DateTime(2026, 9, 1, 9, 30, 0, DateTimeKind.Utc), WeightSource.Indicator));
        weighment.RecordSecondWeight(new WeightCapture(10_000m, new DateTime(2026, 9, 1, 10, 5, 0, DateTimeKind.Utc), WeightSource.Indicator));
        return weighment;
    }

    private sealed class MutableOptionsMonitor<T>(T initial) : IOptionsMonitor<T>
    {
        private readonly List<Action<T, string?>> _listeners = [];

        public T CurrentValue { get; private set; } = initial;

        public T Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<T, string?> listener)
        {
            _listeners.Add(listener);
            return new Subscription(() => _listeners.Remove(listener));
        }

        public void Update(T value)
        {
            CurrentValue = value;
            foreach (var listener in _listeners.ToArray())
            {
                listener(value, Options.DefaultName);
            }
        }

        private sealed class Subscription(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }

    private sealed class AllowAllPermissionService : IPermissionService
    {
        public OperatorIdentity CurrentOperator => new("tester", "Test Operator", new Role("Administrator", [Permissions.WeighmentReprint]));

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public event EventHandler<OperatorChangedEventArgs>? OperatorChanged { add { } remove { } }

        public AuthorizationResult Authorize(Permission permission) => AuthorizationResult.Allowed;
        public AuthorizationResult Authorize(object candidate) => AuthorizationResult.Allowed;
        public bool HasPermission(Permission permission) => true;
        public bool HasAllPermissions(params Permission[] permissions) => true;
        public bool HasAnyPermission(params Permission[] permissions) => true;
        public void SetOperator(OperatorIdentity identity) { }
        public void SignOut() { }
    }
}
