using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using WeighBridge.App.Controls;
using WeighBridge.App.Views;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Dialogs;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Reporting;
using WeighBridge.Domain.Masters;
using WeighBridge.Reporting.Services;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// Reports ViewModel exposing the 8 canonical client filters and multi-channel document dispatch.
/// </summary>
public sealed class ReportsViewModel : ViewModelBase
{
    private readonly IReportService _reportService;
    private readonly IPrintService _printService;
    private readonly IVehicleService _vehicleService;
    private readonly IPartyService _partyService;
    private readonly IMaterialService _materialService;
    private readonly IVehicleTypeService _vehicleTypeService;
    private readonly IDialogService _dialogs;
    private readonly IAuditLogger _audit;
    private readonly ILogger<ReportsViewModel> _logger;
    private ReportDocument? _lastDocument;
    private string? _lastCriteriaKey;

    // 8 Screenshot Filters
    private string _startSlipInput = string.Empty;
    private string _endSlipInput = string.Empty;
    private DateTime _startDate = DateTime.Today.AddDays(-30);
    private DateTime _endDate = DateTime.Today;
    private string _vehicleNumber = string.Empty;
    private string _partyName = string.Empty;
    private string _materialName = string.Empty;
    private string _vehicleTypeName = string.Empty;

    private string _statusMessage = string.Empty;
    private BadgeSeverity _statusSeverity = BadgeSeverity.Neutral;

    public ReportsViewModel(
        IReportService reportService,
        IPrintService printService,
        IVehicleService vehicleService,
        IPartyService partyService,
        IMaterialService materialService,
        IVehicleTypeService vehicleTypeService,
        IDialogService dialogs,
        IAuditLogger audit,
        ILogger<ReportsViewModel> logger)
    {
        _reportService = reportService ?? throw new ArgumentNullException(nameof(reportService));
        _printService = printService ?? throw new ArgumentNullException(nameof(printService));
        _vehicleService = vehicleService ?? throw new ArgumentNullException(nameof(vehicleService));
        _partyService = partyService ?? throw new ArgumentNullException(nameof(partyService));
        _materialService = materialService ?? throw new ArgumentNullException(nameof(materialService));
        _vehicleTypeService = vehicleTypeService ?? throw new ArgumentNullException(nameof(vehicleTypeService));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Title = "Reports";
        Description = "Generate, preview, print, or export weighment reports with multi-criteria filtering.";

        ViewReportCommand = new AsyncRelayCommand(ViewReportAsync, () => !IsBusy, OnUnhandled);
        PrintCommand = new AsyncRelayCommand(PrintDirectAsync, () => !IsBusy, OnUnhandled);
        EmailCommand = new AsyncRelayCommand(EmailReportAsync, () => !IsBusy, OnUnhandled);
        ExportExcelCommand = new AsyncRelayCommand(ExportExcelAsync, () => !IsBusy, OnUnhandled);
        ExportPdfCommand = new AsyncRelayCommand(ExportPdfAsync, () => !IsBusy, OnUnhandled);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
    }

    public ObservableCollection<string> AvailableVehicles { get; } = [];
    public ObservableCollection<string> AvailableParties { get; } = [];
    public ObservableCollection<string> AvailableMaterials { get; } = [];
    public ObservableCollection<string> AvailableVehicleTypes { get; } = [];

    #region 8 Filters

    public string StartSlipInput
    {
        get => _startSlipInput;
        set
        {
            if (SetProperty(ref _startSlipInput, value))
            {
                InvalidateGeneratedDocument();
            }
        }
    }

    public string EndSlipInput
    {
        get => _endSlipInput;
        set
        {
            if (SetProperty(ref _endSlipInput, value))
            {
                InvalidateGeneratedDocument();
            }
        }
    }

    public DateTime StartDate
    {
        get => _startDate;
        set
        {
            if (SetProperty(ref _startDate, value))
            {
                InvalidateGeneratedDocument();
            }
        }
    }

    public DateTime EndDate
    {
        get => _endDate;
        set
        {
            if (SetProperty(ref _endDate, value))
            {
                InvalidateGeneratedDocument();
            }
        }
    }

    public string VehicleNumber
    {
        get => _vehicleNumber;
        set
        {
            if (SetProperty(ref _vehicleNumber, value))
            {
                InvalidateGeneratedDocument();
            }
        }
    }

    public string PartyName
    {
        get => _partyName;
        set
        {
            if (SetProperty(ref _partyName, value))
            {
                InvalidateGeneratedDocument();
            }
        }
    }

    public string MaterialName
    {
        get => _materialName;
        set
        {
            if (SetProperty(ref _materialName, value))
            {
                InvalidateGeneratedDocument();
            }
        }
    }

    public string VehicleTypeName
    {
        get => _vehicleTypeName;
        set
        {
            if (SetProperty(ref _vehicleTypeName, value))
            {
                InvalidateGeneratedDocument();
            }
        }
    }

    #endregion

    #region Commands

    public ICommand ViewReportCommand { get; }
    public ICommand PrintCommand { get; }
    public ICommand EmailCommand { get; }
    public ICommand ExportExcelCommand { get; }
    public ICommand ExportPdfCommand { get; }
    public ICommand ClearFiltersCommand { get; }

    #endregion

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value, () => OnPropertyChanged(nameof(HasStatus)));
    }

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public BadgeSeverity StatusSeverity
    {
        get => _statusSeverity;
        private set => SetProperty(ref _statusSeverity, value);
    }

    public override async Task OnNavigatedToAsync(WeighBridge.Core.Navigation.NavigationContext context)
    {
        try
        {
            var vehicles = await _vehicleService.GetAllAsync(includeInactive: false).ConfigureAwait(true);
            var parties = await _partyService.GetAllAsync(includeInactive: false).ConfigureAwait(true);
            var materials = await _materialService.GetAllAsync(includeInactive: false).ConfigureAwait(true);
            var vehicleTypes = await _vehicleTypeService.GetAllAsync(includeInactive: false).ConfigureAwait(true);

            AvailableVehicles.Clear();
            foreach (var v in vehicles.OrderBy(v => v.VehicleNumber)) AvailableVehicles.Add(v.VehicleNumber);

            AvailableParties.Clear();
            foreach (var p in parties.OrderBy(p => p.Name)) AvailableParties.Add(p.Name);

            AvailableMaterials.Clear();
            foreach (var m in materials.OrderBy(m => m.Name)) AvailableMaterials.Add(m.Name);

            AvailableVehicleTypes.Clear();
            foreach (var t in vehicleTypes.OrderBy(t => t.TypeName)) AvailableVehicleTypes.Add(t.TypeName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load master filter data for reports");
        }
    }

    private ReportFilterParameters CreateFilterParameters()
    {
        var startSlip = NormalizeOptionalFilter(StartSlipInput);
        var endSlip = NormalizeOptionalFilter(EndSlipInput);

        long? startSeq = CsvReportService.TryExtractSequenceNumber(startSlip);
        long? endSeq = CsvReportService.TryExtractSequenceNumber(endSlip);

        return new ReportFilterParameters(
            StartSlipSequence: startSeq,
            EndSlipSequence: endSeq,
            StartDateLocal: StartDate,
            EndDateLocal: EndDate,
            VehicleNumber: NormalizeOptionalFilter(VehicleNumber),
            PartyName: NormalizeOptionalFilter(PartyName),
            MaterialName: NormalizeOptionalFilter(MaterialName),
            VehicleTypeName: NormalizeOptionalFilter(VehicleTypeName));
    }

    private async Task ViewReportAsync()
    {
        try
        {
            ReportDocument? doc = null;
            await RunBusyAsync(async () =>
            {
                ShowStatus("Generating report document...", BadgeSeverity.Information);
                doc = await GetOrBuildDocumentAsync().ConfigureAwait(true);
            }, "Generating report...").ConfigureAwait(true);

            if (doc is null)
            {
                return;
            }

            _audit.Record(
                "ViewPreview",
                "Report",
                null,
                $"Viewed report preview with {doc.TotalRecordCount} records.");

            ShowDocumentStatus(doc);

            var previewVm = new ReportPreviewViewModel(
                doc,
                _reportService,
                _printService,
                _dialogs,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ReportPreviewViewModel>.Instance);

            var window = new ReportPreviewWindow(previewVm);
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build report document for preview");
            _audit.RecordFailed("ViewPreview", "Report", ex.Message);
            ShowStatus($"Failed to generate report: {ex.Message}", BadgeSeverity.Danger);
        }
    }

    private async Task PrintDirectAsync()
    {
        try
        {
            ReportDocument? doc = null;
            await RunBusyAsync(async () =>
            {
                ShowStatus("Printing report...", BadgeSeverity.Information);
                doc = await GetOrBuildDocumentAsync().ConfigureAwait(true);
            }, "Preparing report...").ConfigureAwait(true);

            if (doc is null)
            {
                return;
            }

            _audit.Record(
                "Print",
                "Report",
                null,
                $"Printed report directly with {doc.TotalRecordCount} records.");

            var result = await _printService.PrintAsync(
                "ReportDocument",
                CreateReportPrintPayload(doc),
                copies: 1).ConfigureAwait(true);

            if (result.Succeeded)
            {
                ShowStatus("Report dispatched to printer.", BadgeSeverity.Success);
                return;
            }

            await _dialogs.ShowWarningAsync("Print Report", result.Message);
            ShowStatus($"Print not completed: {result.Message}", BadgeSeverity.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to print report directly");
            _audit.RecordFailed("Print", "Report", ex.Message);
            ShowStatus($"Print failed: {ex.Message}", BadgeSeverity.Danger);
        }
    }

    private async Task EmailReportAsync()
    {
        try
        {
            ReportDocument? doc = null;
            await RunBusyAsync(async () =>
            {
                ShowStatus("Emailing report...", BadgeSeverity.Information);
                doc = await GetOrBuildDocumentAsync().ConfigureAwait(true);
            }, "Preparing report...").ConfigureAwait(true);

            if (doc is null)
            {
                return;
            }

            _audit.Record(
                "Email",
                "Report",
                null,
                $"Email requested for report with {doc.TotalRecordCount} records.");

            await _dialogs.ShowWarningAsync(
                "Email Report",
                $"Report email is not configured on this terminal. The generated report contains {doc.TotalRecordCount} row(s) and remains available for preview/export.");
            ShowStatus("Email not configured. Export PDF or Excel from the same report document.", BadgeSeverity.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to email report");
            _audit.RecordFailed("Email", "Report", ex.Message);
            ShowStatus($"Email failed: {ex.Message}", BadgeSeverity.Danger);
        }
    }

    private async Task ExportExcelAsync()
    {
        try
        {
            ReportResult? result = null;
            await RunBusyAsync(async () =>
            {
                ShowStatus("Exporting to Excel...", BadgeSeverity.Information);
                var doc = await GetOrBuildDocumentAsync().ConfigureAwait(true);
                result = await _reportService.ExportAsync(doc, ReportFormat.Excel).ConfigureAwait(true);
            }, "Exporting report...").ConfigureAwait(true);

            if (result is null)
            {
                return;
            }

            if (result.Succeeded)
            {
                _audit.Record(
                    "ExportExcel",
                    "Report",
                    null,
                    $"Exported Excel report: {result.OutputPath}");

                await _dialogs.ShowSuccessAsync("Excel Export Complete", $"File saved to:\n{result.OutputPath}");
                ShowStatus("Excel export complete.", BadgeSeverity.Success);
            }
            else
            {
                _audit.RecordFailed("ExportExcel", "Report", result.Message ?? "Export failed");
                ShowStatus($"Export failed: {result.Message}", BadgeSeverity.Danger);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export report to Excel");
            _audit.RecordFailed("ExportExcel", "Report", ex.Message);
            ShowStatus($"Export failed: {ex.Message}", BadgeSeverity.Danger);
        }
    }

    private async Task ExportPdfAsync()
    {
        try
        {
            ReportResult? result = null;
            await RunBusyAsync(async () =>
            {
                ShowStatus("Exporting to PDF...", BadgeSeverity.Information);
                var doc = await GetOrBuildDocumentAsync().ConfigureAwait(true);
                result = await _reportService.ExportAsync(doc, ReportFormat.Pdf).ConfigureAwait(true);
            }, "Exporting report...").ConfigureAwait(true);

            if (result is null)
            {
                return;
            }

            if (result.Succeeded)
            {
                _audit.Record(
                    "ExportPdf",
                    "Report",
                    null,
                    $"Exported PDF report: {result.OutputPath}");

                await _dialogs.ShowSuccessAsync("PDF Export Complete", $"File saved to:\n{result.OutputPath}");
                ShowStatus("PDF export complete.", BadgeSeverity.Success);
            }
            else
            {
                _audit.RecordFailed("ExportPdf", "Report", result.Message ?? "Export failed");
                ShowStatus($"Export failed: {result.Message}", BadgeSeverity.Danger);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export report to PDF");
            _audit.RecordFailed("ExportPdf", "Report", ex.Message);
            ShowStatus($"Export failed: {ex.Message}", BadgeSeverity.Danger);
        }
    }

    private void ClearFilters()
    {
        StartSlipInput = string.Empty;
        EndSlipInput = string.Empty;
        StartDate = DateTime.Today.AddDays(-30);
        EndDate = DateTime.Today;
        VehicleNumber = string.Empty;
        PartyName = string.Empty;
        MaterialName = string.Empty;
        VehicleTypeName = string.Empty;
        InvalidateGeneratedDocument();
        ShowStatus("Filters reset.", BadgeSeverity.Neutral);
    }

    private async Task<ReportDocument> GetOrBuildDocumentAsync()
    {
        var validation = ValidateCriteria();
        if (validation is not null)
        {
            throw new InvalidOperationException(validation);
        }

        var key = CreateCriteriaKey();
        if (_lastDocument is not null && string.Equals(_lastCriteriaKey, key, StringComparison.Ordinal))
        {
            return _lastDocument;
        }

        var filters = CreateFilterParameters();
        var doc = await _reportService.BuildDocumentAsync(filters).ConfigureAwait(true);
        _lastDocument = doc;
        _lastCriteriaKey = key;
        return doc;
    }

    private string? ValidateCriteria()
    {
        var startSlip = NormalizeOptionalFilter(StartSlipInput);
        var endSlip = NormalizeOptionalFilter(EndSlipInput);

        if (!string.IsNullOrWhiteSpace(startSlip) &&
            CsvReportService.TryExtractSequenceNumber(startSlip) is null)
        {
            return "Starting slip number must be a number such as 42, 000042, or WB-000042.";
        }

        if (!string.IsNullOrWhiteSpace(endSlip) &&
            CsvReportService.TryExtractSequenceNumber(endSlip) is null)
        {
            return "Ending slip number must be a number such as 42, 000042, or WB-000042.";
        }

        var startSeq = CsvReportService.TryExtractSequenceNumber(startSlip);
        var endSeq = CsvReportService.TryExtractSequenceNumber(endSlip);
        if (startSeq.HasValue && endSeq.HasValue && startSeq.Value > endSeq.Value)
        {
            return "Starting slip number cannot be greater than ending slip number.";
        }

        if (StartDate.Date > EndDate.Date)
        {
            return "Start date cannot be after end date.";
        }

        return null;
    }

    private string CreateCriteriaKey()
        => string.Join("|",
            NormalizeOptionalFilter(StartSlipInput) ?? string.Empty,
            NormalizeOptionalFilter(EndSlipInput) ?? string.Empty,
            StartDate.Date.ToString("O"),
            EndDate.Date.ToString("O"),
            NormalizeOptionalFilter(VehicleNumber) ?? string.Empty,
            NormalizeOptionalFilter(PartyName) ?? string.Empty,
            NormalizeOptionalFilter(MaterialName) ?? string.Empty,
            NormalizeOptionalFilter(VehicleTypeName) ?? string.Empty);

    private static string? NormalizeOptionalFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed is "-" or "–" or "—" ? null : trimmed;
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

    private void InvalidateGeneratedDocument()
    {
        _lastDocument = null;
        _lastCriteriaKey = null;
    }

    private void ShowDocumentStatus(ReportDocument doc)
    {
        if (doc.TotalRecordCount == 0)
        {
            ShowStatus("No records found for the selected criteria.", BadgeSeverity.Warning);
            return;
        }

        if (doc.IsRowLimited && doc.MaxRows.HasValue)
        {
            ShowStatus(
                $"Report loaded: limited to {doc.MaxRows.Value} rows. Refine the criteria for a complete result.",
                BadgeSeverity.Warning);
            return;
        }

        ShowStatus($"Report loaded: {doc.TotalRecordCount} records found.", BadgeSeverity.Success);
    }

    private void ShowStatus(string message, BadgeSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
    }

    private void OnUnhandled(Exception ex)
    {
        _logger.LogError(ex, "Unhandled error in ReportsViewModel command");
        ShowStatus($"Error: {ex.Message}", BadgeSeverity.Danger);
    }
}
