using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Reporting;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Security;
using WeighBridge.Infrastructure.Repositories;
using WeighBridge.Reporting.Services;
using WeighBridge.Tests.Infrastructure;
using WeighBridge.Tests.Weighments;

namespace WeighBridge.Tests.Reporting;

/// <summary>
/// Forensic validation tests for PDF and XLSX report export formats.
/// Ensures structurally valid ISO 32000-1 binary PDF and ECMA-376 OpenXML spreadsheets.
/// </summary>
public sealed class DocumentExportForensicTests : IDisposable
{
    private readonly WeighmentHarness _harness = new();
    private readonly TempDataRoot _temp = new();

    public DocumentExportForensicTests()
    {
        _harness.SignInAs(Roles.Administrator);
    }

    public void Dispose()
    {
        _harness.Dispose();
        _temp.Dispose();
    }

    private CsvReportService CreateService() => new(
        () => new UnitOfWork(_harness.CreateContext()),
        _harness.Permissions,
        Options.Create(new ReportingOptions
        {
            OutputDirectory = _temp.Paths.ReportsDirectory,
            MaxRowsPerReport = 10_000,
        }),
        NullLogger<CsvReportService>.Instance,
        Options.Create(new CompanyOptions
        {
            CompanyName = "Forensic Test Weighbridge Ltd",
            AddressLine1 = "Testing Yard Phase 1",
            AddressLine2 = "Gate 4B"
        }));

    [Fact]
    public async Task ExportPdf_ProducesValidIsoPdfBinary_WithCorrectStructureAndTotals()
    {
        // 1. Arrange test data
        await Complete("MH12AB1001", "Acme Industries", "Iron Ore", 25_000m, 10_000m, 150m);
        await Complete("MH12AB1002", "Zenith Logistics", "Bauxite", 32_000m, 12_000m, 200m);

        var service = CreateService();
        var filters = new ReportFilterParameters(
            StartDateLocal: DateTime.Today.AddDays(-1),
            EndDateLocal: DateTime.Today.AddDays(1));

        var doc = await service.BuildDocumentAsync(filters);
        Assert.Equal(2, doc.TotalRecordCount);
        Assert.Equal(350m, doc.TotalCharges);
        Assert.Equal(35_000m, doc.TotalNetWeightKg);

        var pdfPath = Path.Combine(_temp.Paths.ReportsDirectory, "ForensicTest.pdf");

        // 2. Export PDF
        var exportResult = await service.ExportAsync(doc, ReportFormat.Pdf, pdfPath);
        Assert.True(exportResult.Succeeded, exportResult.Message);
        Assert.True(File.Exists(pdfPath), "PDF file must exist on disk");

        // 3. Binary Forensic Inspection
        var bytes = await File.ReadAllBytesAsync(pdfPath);
        Assert.True(bytes.Length > 200, "PDF file must contain substantial binary content");

        var text = Encoding.ASCII.GetString(bytes);

        // Header check
        Assert.StartsWith("%PDF-1.4", text);

        // Object checks
        Assert.Contains("/Type /Catalog", text);
        Assert.Contains("/Type /Pages", text);
        Assert.Contains("/Type /Page", text);
        Assert.Contains("/Type /Font", text);
        Assert.Contains("/BaseFont /Helvetica", text);

        // Stream and trailer checks
        Assert.Contains("stream", text);
        Assert.Contains("endstream", text);
        Assert.Contains("xref", text);
        Assert.Contains("trailer", text);
        Assert.Contains("startxref", text);
        Assert.EndsWith("%%EOF\r\n", text.TrimEnd() + "\r\n");

        // Content verification
        Assert.Contains("Forensic Test Weighbridge Ltd", text);
        Assert.Contains("MH12AB1001", text);
        Assert.Contains("MH12AB1002", text);
        Assert.Contains("Acme Industries", text);
        Assert.Contains("Zenith Logistics", text);
        Assert.Contains("Total \\(2 Records\\)", text);
    }

    [Fact]
    public async Task ExportExcel_ProducesValidOpenXmlPackage_WithNumericTypesAndTotals()
    {
        // 1. Arrange test data
        await Complete("MH14CD5001", "Global Cement", "Flyash", 40_000m, 15_000m, 300m);
        await Complete("MH14CD5002", "National Steel", "Scrap", 28_000m, 11_000m, 250m);

        var service = CreateService();
        var filters = new ReportFilterParameters(
            StartDateLocal: DateTime.Today.AddDays(-1),
            EndDateLocal: DateTime.Today.AddDays(1));

        var doc = await service.BuildDocumentAsync(filters);
        var xlsxPath = Path.Combine(_temp.Paths.ReportsDirectory, "ForensicTest.xlsx");

        // 2. Export Excel
        var exportResult = await service.ExportAsync(doc, ReportFormat.Excel, xlsxPath);
        Assert.True(exportResult.Succeeded, exportResult.Message);
        Assert.True(File.Exists(xlsxPath), "XLSX file must exist on disk");

        // 3. OpenXML Package Forensic Inspection
        using var zip = ZipFile.OpenRead(xlsxPath);

        // Required OpenXML parts
        var contentTypes = zip.GetEntry("[Content_Types].xml");
        Assert.NotNull(contentTypes);

        var rootRels = zip.GetEntry("_rels/.rels");
        Assert.NotNull(rootRels);

        var workbookRels = zip.GetEntry("xl/_rels/workbook.xml.rels");
        Assert.NotNull(workbookRels);

        var workbook = zip.GetEntry("xl/workbook.xml");
        Assert.NotNull(workbook);

        var styles = zip.GetEntry("xl/styles.xml");
        Assert.NotNull(styles);

        var worksheet = zip.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(worksheet);

        // Read worksheet XML content
        using var stream = worksheet.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var worksheetXml = await reader.ReadToEndAsync();

        // Verify numeric cell formatting (not string representations)
        Assert.Contains("MH14CD5001", worksheetXml);
        Assert.Contains("MH14CD5002", worksheetXml);
        Assert.Contains("Global Cement", worksheetXml);
        Assert.Contains("National Steel", worksheetXml);

        // Verify numeric values exist in <v> tags
        Assert.Contains("<v>40000</v>", worksheetXml);
        Assert.Contains("<v>15000</v>", worksheetXml);
        Assert.Contains("<v>25000</v>", worksheetXml); // Net weight 1
        Assert.Contains("<v>17000</v>", worksheetXml); // Net weight 2
        Assert.Contains("<v>42000</v>", worksheetXml); // Total Net Weight
        Assert.Contains("<v>550</v>", worksheetXml);   // Total Charges
    }

    [Fact]
    public void PdfReportDocumentWriter_MultiPagePagination_ProducesMultiplePageObjects()
    {
        // Arrange 60 rows to exceed a single page
        var rows = new List<ReportDocumentRow>();
        for (var i = 1; i <= 60; i++)
        {
            rows.Add(new ReportDocumentRow(
                SerialNumber: i,
                SlipNumber: $"WB-{i:D6}",
                VehicleNumber: $"MH12AB{i:D4}",
                VehicleTypeName: "10 Wheeler",
                PartyName: $"Party Number {i}",
                MaterialName: "Raw Material",
                Charges1: 100m,
                Charges2: 0m,
                TotalCharges: 100m,
                GrossWeightKg: 20_000m,
                TareWeightKg: 8_000m,
                NetWeightKg: 12_000m,
                GrossCapturedAtLocal: DateTime.Today,
                CompletedAtLocal: DateTime.Today,
                Status: "Completed"));
        }

        var doc = ReportDocument.Create(
            title: "Multi-Page Daily Weighment Report",
            companyName: "High Throughput Weighbridge",
            startDateLocal: DateTime.Today,
            endDateLocal: DateTime.Today,
            rows: rows);

        // Act
        var pdfBytes = PdfReportDocumentWriter.GeneratePdf(doc);
        var text = Encoding.ASCII.GetString(pdfBytes);

        // Assert
        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("/Count 3", text); // 60 rows across ~25 rows per page = 3 pages
        Assert.Contains("Page 1 of 3", text);
        Assert.Contains("Page 2 of 3", text);
        Assert.Contains("Page 3 of 3", text);
        Assert.Contains("Total \\(60 Records\\)", text);
    }

    [Fact]
    public async Task ExternalForensicVerification_UsingPythonPyMuPdfAndOpenPyXl_Passes100Percent()
    {
        for (var i = 1; i <= 30; i++)
        {
            await Complete($"MH12AB{2000 + i}", $"Customer {i}", "Bauxite", 30_000m + (i * 100), 10_000m, 200m);
        }

        var service = CreateService();
        var filters = new ReportFilterParameters(
            StartDateLocal: DateTime.Today.AddDays(-1),
            EndDateLocal: DateTime.Today.AddDays(1));

        var doc = await service.BuildDocumentAsync(filters);

        var pdfPath = Path.Combine(_temp.Paths.ReportsDirectory, "ExternalForensicTest.pdf");
        var xlsxPath = Path.Combine(_temp.Paths.ReportsDirectory, "ExternalForensicTest.xlsx");

        var pdfRes = await service.ExportAsync(doc, ReportFormat.Pdf, pdfPath);
        Assert.True(pdfRes.Succeeded);

        var xlsxRes = await service.ExportAsync(doc, ReportFormat.Excel, xlsxPath);
        Assert.True(xlsxRes.Succeeded);

        // Run python external validator
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"\"C:\\Users\\dell\\.gemini\\antigravity-ide\\brain\\1a578efa-6160-4587-add8-893b698a9fcc\\scratch\\validate_exports.py\" \"{pdfPath}\" \"{xlsxPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = System.Diagnostics.Process.Start(psi);
        Assert.NotNull(proc);

        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        Assert.Equal(0, proc.ExitCode);
        Assert.Contains("[PyMuPDF] ALL PAGES RENDERED AND VALIDATED SUCCESSFULLY!", stdout);
        Assert.Contains("[openpyxl] WORKBOOK STRUCTURE AND DATA TYPES VALIDATED SUCCESSFULLY!", stdout);
    }

    private async Task Complete(string vehicle, string party, string material, decimal gross, decimal tare, decimal charges)
    {
        var created = await _harness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = vehicle,
            Mode = WeighmentMode.GrossFirst,
            PartyName = party,
            MaterialName = material,
            Charges = charges,
        });
        await _harness.Service.RecordFirstWeightAsync(created.Id, gross, WeightSource.Indicator);
        await _harness.Service.RecordSecondWeightAsync(created.Id, tare, WeightSource.Indicator);
    }
}
