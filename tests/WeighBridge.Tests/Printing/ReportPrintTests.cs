using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Printing;
using WeighBridge.Core.Reporting;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Security;
using WeighBridge.Printing.Outputs;
using WeighBridge.Printing.Services;
using WeighBridge.Printing.Template;

namespace WeighBridge.Tests.Printing;

public sealed class ReportPrintTests
{
    [Fact]
    public void ReportPrint_RendererUsesSameReportDocument_AndPreservesRowsAndTotals()
    {
        var document = CreateReportDocument();

        string rendered = ReportPrintTextRenderer.Render(document, paperSize: "A4", sideWisePrinting: true);

        Assert.Contains("Deterministic Report", rendered);
        Assert.Contains("WB-000001", rendered);
        Assert.Contains("WB-000002", rendered);
        Assert.Contains("MH12AB1001", rendered);
        Assert.Contains("10 Wheeler", rendered);
        Assert.Contains("Acme Minerals", rendered);
        Assert.Contains("Iron Ore", rendered);
        Assert.Contains("Total Records: 2", rendered);
        Assert.Contains("Total Charges: 450", rendered);
        Assert.Contains("Total Net: 37,000 kg", rendered);
        Assert.Equal(2, document.TotalRecordCount);
        Assert.Equal(37_000m, document.TotalNetWeightKg);
        Assert.Equal(450m, document.TotalCharges);
    }

    [Fact]
    public void ReportPrint_RendererRespectsPaperSizeAndSideWiseConfiguration()
    {
        var document = CreateReportDocument();

        string a4SideWise = ReportPrintTextRenderer.Render(document, paperSize: "A4", sideWisePrinting: true);
        string halfA4 = ReportPrintTextRenderer.Render(document, paperSize: "Half A4 / A5", sideWisePrinting: false);

        int a4HeaderWidth = a4SideWise.Split(Environment.NewLine).First(line => line.StartsWith("---", StringComparison.Ordinal)).Length;
        int halfHeaderWidth = halfA4.Split(Environment.NewLine).First(line => line.StartsWith("---", StringComparison.Ordinal)).Length;

        Assert.Equal(160, a4HeaderWidth);
        Assert.Equal(96, halfHeaderWidth);
    }

    [Fact]
    public async Task ReportPrint_DisabledPrinter_ReturnsFailureWithoutMutatingReport()
    {
        var document = CreateReportDocument();
        var service = CreatePrintService(new PrinterOptions
        {
            Enabled = false,
            DefaultPrinterName = "Any Printer",
            PrinterType = "Graphics Printer",
            PaperSize = "A4",
            CopyCount = 3,
        });

        var result = await service.PrintAsync("ReportDocument", new Dictionary<string, object?>
        {
            ["ReportDocument"] = document,
        });

        Assert.False(result.Succeeded);
        Assert.Contains("disabled", result.Message);
        Assert.Equal(2, document.TotalRecordCount);
        Assert.Equal(37_000m, document.TotalNetWeightKg);
        Assert.Equal("WB-000001", document.Rows[0].SlipNumber);
    }

    [Fact]
    public async Task ReportPrint_InvalidPayloadFailsBeforeAnySlipFallback()
    {
        var service = CreatePrintService(new PrinterOptions
        {
            Enabled = true,
            DefaultPrinterName = "NonExistentReportPrinter_999XYZ",
        });

        var result = await service.PrintAsync("ReportDocument", new Dictionary<string, object?>());

        Assert.False(result.Succeeded);
        Assert.Contains("canonical ReportDocument", result.Message);
    }

    private static WindowsPrintService CreatePrintService(PrinterOptions options)
    {
        var templateEngine = new SlipTemplateEngine();
        return new WindowsPrintService(
            Options.Create(options),
            Options.Create(new CompanyOptions { CompanyName = "Report Print Tests Ltd" }),
            new AllowAllPermissionService(),
            templateEngine,
            new WindowsGdiPrintOutput(templateEngine, NullLogger<WindowsGdiPrintOutput>.Instance),
            new RawSpoolPrintOutput(templateEngine, NullLogger<RawSpoolPrintOutput>.Instance),
            NullLogger<WindowsPrintService>.Instance);
    }

    private static ReportDocument CreateReportDocument()
    {
        var rows = new List<ReportDocumentRow>
        {
            new(
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
                Status: "Completed"),
            new(
                SerialNumber: 2,
                SlipNumber: "WB-000002",
                VehicleNumber: "MH12AB1002",
                VehicleTypeName: "Truck",
                PartyName: "Zenith Logistics",
                MaterialName: "Bauxite",
                Charges1: 150m,
                Charges2: 0m,
                TotalCharges: 150m,
                GrossWeightKg: 28_000m,
                TareWeightKg: 11_000m,
                NetWeightKg: 17_000m,
                GrossCapturedAtLocal: new DateTime(2026, 9, 1, 11, 15, 0),
                CompletedAtLocal: new DateTime(2026, 9, 1, 11, 45, 0),
                Status: "Completed")
        };

        return ReportDocument.Create(
            title: "Deterministic Report",
            companyName: "Report Print Tests Ltd",
            startDateLocal: new DateTime(2026, 9, 1),
            endDateLocal: new DateTime(2026, 9, 1),
            rows: rows);
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
