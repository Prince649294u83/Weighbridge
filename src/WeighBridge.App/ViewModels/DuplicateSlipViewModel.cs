using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.App.Controls;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Navigation;
using WeighBridge.Core.Printing;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;
using WeighBridge.Printing.Services;

namespace WeighBridge.App.ViewModels;

public sealed class DuplicateSlipViewModel : ViewModelBase
{
    private readonly IRepository<Weighment> _weighments;
    private readonly IPrintService _printService;
    private readonly IOptions<CompanyOptions> _companyOptions;
    private readonly IAuditLogger _audit;
    private readonly ILogger<DuplicateSlipViewModel> _logger;
    private readonly AsyncRelayCommand _searchCommand;
    private readonly AsyncRelayCommand<WeighmentSummary> _reprintCommand;
    private readonly RelayCommand _clearCommand;

    private string _searchText = string.Empty;
    private DateTime _startDate = DateTime.Today;
    private DateTime _endDate = DateTime.Today;
    private WeighmentSummary? _selectedWeighment;
    private string _statusMessage = string.Empty;
    private BadgeSeverity _statusSeverity = BadgeSeverity.Neutral;

    public DuplicateSlipViewModel(
        IRepository<Weighment> weighments,
        IPrintService printService,
        IOptions<CompanyOptions> companyOptions,
        IAuditLogger audit,
        ILogger<DuplicateSlipViewModel> logger)
    {
        _weighments = weighments ?? throw new ArgumentNullException(nameof(weighments));
        _printService = printService ?? throw new ArgumentNullException(nameof(printService));
        _companyOptions = companyOptions ?? throw new ArgumentNullException(nameof(companyOptions));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _searchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy, OnUnhandled);
        _reprintCommand = new AsyncRelayCommand<WeighmentSummary>(ReprintAsync, w => !IsBusy && w is not null, OnUnhandled);
        _clearCommand = new RelayCommand(Clear);
    }

    public new string Title => "Duplicate Slip";
    public new string Description => "Find a completed weighment and reissue its slip.";

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public DateTime StartDate
    {
        get => _startDate;
        set => SetProperty(ref _startDate, value);
    }

    public DateTime EndDate
    {
        get => _endDate;
        set => SetProperty(ref _endDate, value);
    }

    public WeighmentSummary? SelectedWeighment
    {
        get => _selectedWeighment;
        set
        {
            if (SetProperty(ref _selectedWeighment, value))
            {
                _reprintCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public BadgeSeverity StatusSeverity
    {
        get => _statusSeverity;
        private set => SetProperty(ref _statusSeverity, value);
    }

    public ObservableCollection<WeighmentSummary> SearchResults { get; } = new();

    public ICommand SearchCommand => _searchCommand;
    public ICommand ReprintCommand => _reprintCommand;
    public ICommand ClearCommand => _clearCommand;

    public override Task OnNavigatedToAsync(NavigationContext context)
    {
        Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// The most rows one search will materialise. Capped and ordered newest-first.
    /// </summary>
    private const int MaxResults = 500;

    private async Task SearchAsync()
    {
        IsBusy = true;
        ClearStatus();

        try
        {
            SearchResults.Clear();
            SelectedWeighment = null;

            var startUtc = StartDate.ToUniversalTime();
            var endUtc = EndDate.AddDays(1).ToUniversalTime();

            if (EndDate < StartDate)
            {
                Show("The end date cannot be before the start date.", BadgeSeverity.Warning);
                return;
            }

            var term = SearchText.Trim();

            var results = await _weighments.QueryAsync(
                w =>
                    w.Status == WeighmentStatus.Completed &&
                    w.CompletedAtUtc >= startUtc &&
                    w.CompletedAtUtc < endUtc,
                query => query
                    .OrderByDescending(w => w.CompletedAtUtc)
                    .Take(MaxResults),
                CancellationToken.None).ConfigureAwait(true);

            foreach (var weighment in results)
            {
                if (!Matches(weighment, term))
                {
                    continue;
                }

                SearchResults.Add(WeighmentSummary.From(weighment));
            }

            if (SearchResults.Count == MaxResults)
            {
                Show($"Showing the first {MaxResults} matches. Narrow the date range or search term to see more.", BadgeSeverity.Warning);
            }
            else if (SearchResults.Count == 0)
            {
                Show("No weighments found matching the search criteria.", BadgeSeverity.Information);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool Matches(Weighment weighment, string term)
    {
        if (string.IsNullOrEmpty(term))
        {
            return true;
        }

        return weighment.SlipNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               weighment.VehicleNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               (weighment.PartyName != null && weighment.PartyName.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ReprintAsync(WeighmentSummary? summary)
    {
        if (summary is null) return;

        IsBusy = true;

        try
        {
            var weighment = await _weighments.GetByIdAsync(summary.Id).ConfigureAwait(true);
            if (weighment is null)
            {
                Show($"Weighment with slip '{summary.SlipNumber}' was not found.", BadgeSeverity.Danger);
                return;
            }

            if (weighment.Status != WeighmentStatus.Completed)
            {
                Show($"Cannot reprint slip for weighment in status '{weighment.Status}'. Only completed weighments can be reprinted.", BadgeSeverity.Warning);
                return;
            }

            // Construct calculation-free print data from authoritative snapshot + current company options
            var printData = WeighmentPrintDataFactory.Create(
                weighment,
                _companyOptions.Value,
                isDuplicate: true,
                duplicateWatermark: "WEIGHMENT SLIP (DUPLICATE)");

            PrintResult printResult;
            if (_printService is WindowsPrintService wps)
            {
                printResult = await wps.PrintSlipAsync(printData).ConfigureAwait(true);
            }
            else
            {
                var dict = new Dictionary<string, object?>
                {
                    ["SlipNumber"] = printData.SlipNumber,
                    ["VehicleNumber"] = printData.VehicleNumber,
                    ["PartyName"] = printData.PartyName,
                    ["MaterialName"] = printData.MaterialName,
                    ["DriverName"] = printData.DriverName,
                    ["TransporterName"] = printData.TransporterName,
                    ["GatePassNumber"] = printData.GatePassNumber,
                    ["CustomField1"] = printData.CustomField1,
                    ["CustomField2"] = printData.CustomField2,
                    ["GrossWeightKg"] = printData.GrossWeightKg,
                    ["TareWeightKg"] = printData.TareWeightKg,
                    ["NetWeightKg"] = printData.NetWeightKg,
                    ["Charges"] = printData.TotalCharges,
                    ["Remarks"] = printData.Remarks,
                    ["IsDuplicate"] = true
                };
                printResult = await _printService.PrintAsync("WeighmentSlip", dict).ConfigureAwait(true);
            }

            if (printResult.Succeeded)
            {
                _audit.Record(
                    "Reprint",
                    "Weighment",
                    summary.SlipNumber,
                    $"Vehicle: {summary.VehicleNumber}, Outcome: SUCCESS, Copies: 1");

                Show(printResult.Message, BadgeSeverity.Success);
            }
            else
            {
                _audit.Record(
                    "Reprint",
                    "Weighment",
                    summary.SlipNumber,
                    $"Vehicle: {summary.VehicleNumber}, Outcome: FAILURE, Reason: {printResult.Message}");

                Show(printResult.Message, BadgeSeverity.Danger);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Clear()
    {
        SearchText = string.Empty;
        StartDate = DateTime.Today;
        EndDate = DateTime.Today;
        SearchResults.Clear();
        SelectedWeighment = null;
        ClearStatus();
    }

    private void Show(string message, BadgeSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
    }

    private void ClearStatus()
    {
        StatusMessage = string.Empty;
        StatusSeverity = BadgeSeverity.Neutral;
    }

    private void OnUnhandled(Exception error)
    {
        _logger.LogError(error, "Duplicate Slip could not complete an action.");
        Show("Something went wrong on this screen. The log has the detail.", BadgeSeverity.Danger);
    }
}
