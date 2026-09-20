using System.Globalization;
using System.Text;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Dialogs;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Reporting;
using WeighBridge.Reporting.Services;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// ViewModel for the Report Preview modal window (Screenshot 3).
/// Formats the canonical ReportDocument into a monospace columnar view with totals,
/// and wires Print, Email, Excel (.xlsx), and PDF (.pdf) export actions.
/// </summary>
public sealed class ReportPreviewViewModel : ViewModelBase
{
    private readonly IReportService _reportService;
    private readonly IPrintService _printService;
    private readonly IDialogService _dialogs;
    private readonly ILogger<ReportPreviewViewModel> _logger;

    public ReportDocument Document { get; }
    public string FormattedPreviewText { get; }

    public ICommand PrintCommand { get; }
    public ICommand EmailCommand { get; }
    public ICommand ExportExcelCommand { get; }
    public ICommand ExportPdfCommand { get; }
    public ICommand CloseCommand { get; }

    public event Action? RequestClose;

    public ReportPreviewViewModel(
        ReportDocument document,
        IReportService reportService,
        IPrintService printService,
        IDialogService dialogs,
        ILogger<ReportPreviewViewModel> logger)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        _reportService = reportService ?? throw new ArgumentNullException(nameof(reportService));
        _printService = printService ?? throw new ArgumentNullException(nameof(printService));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Title = "Report Preview";
        Description = $"{document.Title} - {document.StartDateLocal:dd/MM/yyyy} to {document.EndDateLocal:dd/MM/yyyy}";

        FormattedPreviewText = FormatDocumentToText(document);

        PrintCommand = new AsyncRelayCommand(PrintAsync, () => !IsBusy);
        EmailCommand = new AsyncRelayCommand(EmailAsync, () => !IsBusy);
        ExportExcelCommand = new AsyncRelayCommand(ExportExcelAsync, () => !IsBusy);
        ExportPdfCommand = new AsyncRelayCommand(ExportPdfAsync, () => !IsBusy);
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
    }

    public static string FormatDocumentToText(ReportDocument doc)
    {
        var sb = new StringBuilder();
        const int reportWidth = 131;
        var divider = PdfReportDocumentWriter.DividerLine;

        if (!string.IsNullOrWhiteSpace(doc.CompanyName))
        {
            sb.AppendLine(PdfReportDocumentWriter.CenterText(doc.CompanyName, reportWidth));
        }
        if (!string.IsNullOrWhiteSpace(doc.AddressLine1))
        {
            sb.AppendLine(PdfReportDocumentWriter.CenterText(doc.AddressLine1, reportWidth));
        }
        if (!string.IsNullOrWhiteSpace(doc.AddressLine2))
        {
            sb.AppendLine(PdfReportDocumentWriter.CenterText(doc.AddressLine2, reportWidth));
        }

        sb.AppendLine();
        sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "Report From Date - {0:M/d/yyyy} To Date - {1:M/d/yyyy}",
            doc.StartDateLocal,
            doc.EndDateLocal));
        sb.AppendLine();

        sb.AppendLine(divider);
        sb.AppendLine(PdfReportDocumentWriter.HeaderLine);
        sb.AppendLine(divider);

        foreach (var r in doc.Rows)
        {
            sb.AppendLine(PdfReportDocumentWriter.FormatDataRow(r));
        }

        sb.AppendLine(divider);
        sb.AppendLine("Total No of Records".PadRight(20) + ": " + doc.TotalRecordCount);
        var netWeightStr = doc.TotalNetWeightKg.ToString("0", CultureInfo.InvariantCulture);
        sb.AppendLine("Total Net Weight".PadRight(20) + ": " + netWeightStr + " Kg.");
        var chargesStr = doc.TotalCharges.ToString("0", CultureInfo.InvariantCulture);
        sb.AppendLine("Total Charges".PadRight(20) + ": " + chargesStr + " /-");

        return sb.ToString();
    }

    private async Task PrintAsync()
    {
        try
        {
            var result = await _printService.PrintAsync(
                "ReportDocument",
                CreateReportPrintPayload(Document),
                copies: 1);

            if (result.Succeeded)
            {
                await _dialogs.ShowSuccessAsync("Print Report", "Report sent to printer.");
                return;
            }

            await _dialogs.ShowWarningAsync("Print Report", result.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to print report preview");
            await _dialogs.ShowErrorAsync("Print Failed", ex.Message);
        }
    }

    private async Task EmailAsync()
    {
        try
        {
            await _dialogs.ShowWarningAsync(
                "Email Report",
                $"Report email is not configured on this terminal. The preview contains {Document.TotalRecordCount} row(s); export PDF or Excel to share this report.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to email report preview");
            await _dialogs.ShowErrorAsync("Email Failed", ex.Message);
        }
    }

    private static IReadOnlyDictionary<string, object?> CreateReportPrintPayload(ReportDocument document)
        => new Dictionary<string, object?>
        {
            ["ReportDocument"] = document,
            ["Title"] = document.Title,
            ["CompanyName"] = document.CompanyName,
            ["StartDateLocal"] = document.StartDateLocal,
            ["EndDateLocal"] = document.EndDateLocal,
            ["TotalRecordCount"] = document.TotalRecordCount,
            ["TotalNetWeightKg"] = document.TotalNetWeightKg,
            ["TotalCharges"] = document.TotalCharges,
            ["Rows"] = document.Rows,
        };

    private async Task ExportExcelAsync()
    {
        try
        {
            var defaultFileName = $"Weighment_Report_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            var filter = "Excel Workbook (*.xlsx)|*.xlsx|All Files (*.*)|*.*";
            var chosenPath = await _dialogs.ShowSaveFileDialogAsync("Save Excel Report", defaultFileName, filter);
            if (string.IsNullOrWhiteSpace(chosenPath)) return;

            var result = await _reportService.ExportAsync(Document, ReportFormat.Excel, chosenPath);
            if (result.Succeeded)
            {
                await _dialogs.ShowSuccessAsync("Excel Export Complete", $"File saved to:\n{result.OutputPath}");
            }
            else
            {
                await _dialogs.ShowErrorAsync("Export Failed", result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export report to Excel");
            await _dialogs.ShowErrorAsync("Export Error", ex.Message);
        }
    }

    private async Task ExportPdfAsync()
    {
        try
        {
            var defaultFileName = $"Weighment_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            var filter = "PDF Document (*.pdf)|*.pdf|All Files (*.*)|*.*";
            var chosenPath = await _dialogs.ShowSaveFileDialogAsync("Save PDF Report", defaultFileName, filter);
            if (string.IsNullOrWhiteSpace(chosenPath)) return;

            var result = await _reportService.ExportAsync(Document, ReportFormat.Pdf, chosenPath);
            if (result.Succeeded)
            {
                await _dialogs.ShowSuccessAsync("PDF Export Complete", $"File saved to:\n{result.OutputPath}");
            }
            else
            {
                await _dialogs.ShowErrorAsync("Export Failed", result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export report to PDF");
            await _dialogs.ShowErrorAsync("Export Error", ex.Message);
        }
    }
}
