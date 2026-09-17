using System.Globalization;
using System.Text;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Dialogs;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Reporting;

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

    private static string FormatDocumentToText(ReportDocument doc)
    {
        var sb = new StringBuilder();
        var divider = "------------------------------------------------------------------------------------------------------------------------";

        sb.AppendLine(CenterText(doc.CompanyName, 120));
        sb.AppendLine(CenterText(doc.Title, 120));
        sb.AppendLine($" Period: {doc.StartDateLocal:dd/MM/yyyy} to {doc.EndDateLocal:dd/MM/yyyy}".PadRight(120));
        sb.AppendLine(divider);
        sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            " {0,-5} {1,-10} {2,-13} {3,-10} {4,-20} {5,8} {6,-12} {7,10} {8,10} {9,10} {10,-18}",
            "Sr.No", "Slip No.", "Vehicle No.", "Type", "Party Name", "Chg 1st", "Material", "Gross Wt.", "Tare Wt.", "Net Wt.", "Date & Time"));
        sb.AppendLine(divider);

        foreach (var r in doc.Rows)
        {
            var party = r.PartyName.Length > 20 ? r.PartyName[..20] : r.PartyName;
            var mat = r.MaterialName.Length > 12 ? r.MaterialName[..12] : r.MaterialName;
            var veh = r.VehicleNumber.Length > 13 ? r.VehicleNumber[..13] : r.VehicleNumber;
            var type = r.VehicleTypeName.Length > 10 ? r.VehicleTypeName[..10] : r.VehicleTypeName;
            var dateStr = r.GrossCapturedAtLocal?.ToString("dd/MM/yy HH:mm", CultureInfo.InvariantCulture) ?? "";

            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                " {0,-5} {1,-10} {2,-13} {3,-10} {4,-20} {5,8:N0} {6,-12} {7,10:N0} {8,10:N0} {9,10:N0} {10,-18}",
                r.SerialNumber, r.SlipNumber, veh, type, party, r.Charges1, mat, r.GrossWeightKg, r.TareWeightKg, r.NetWeightKg, dateStr));
        }

        sb.AppendLine(divider);
        sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            " Total No of Records : {0,-10} | Total Net Weight : {1:N0} Kg. | Total Charges : Rs. {2:N0} /-",
            doc.TotalRecordCount, doc.TotalNetWeightKg, doc.TotalCharges));
        sb.AppendLine(divider);

        return sb.ToString();
    }

    private static string CenterText(string text, int width)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        if (text.Length >= width) return text;
        int leftPadding = (width - text.Length) / 2;
        return text.PadLeft(leftPadding + text.Length);
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
            var result = await _reportService.ExportAsync(Document, ReportFormat.Excel);
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
            var result = await _reportService.ExportAsync(Document, ReportFormat.Pdf);
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
