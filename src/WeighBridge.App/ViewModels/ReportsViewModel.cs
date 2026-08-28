using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using WeighBridge.App.Controls;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Navigation;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// Reporting view model. Provides date-ranged and party-wise weighment reports
/// with export output.
/// </summary>
public sealed class ReportsViewModel : ViewModelBase
{
    private readonly IReportService _reportService;
    private readonly IAuditLogger _audit;
    private readonly ILogger<ReportsViewModel> _logger;
    
    private readonly AsyncRelayCommand _generateCommand;

    private string _selectedReport = string.Empty;
    private DateTime _startDate = DateTime.Today;
    private DateTime _endDate = DateTime.Today;
    private string _partyName = string.Empty;
    private string _materialName = string.Empty;

    private string _statusMessage = string.Empty;
    private BadgeSeverity _statusSeverity = BadgeSeverity.Neutral;
    private string? _lastGeneratedPath;

    public ReportsViewModel(
        IReportService reportService,
        IAuditLogger audit,
        ILogger<ReportsViewModel> logger)
    {
        _reportService = reportService ?? throw new ArgumentNullException(nameof(reportService));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _generateCommand = new AsyncRelayCommand(GenerateAsync, () => !IsBusy && !string.IsNullOrEmpty(SelectedReport), OnUnhandled);

        foreach (var r in _reportService.AvailableReports)
        {
            AvailableReports.Add(r);
        }

        SelectedReport = AvailableReports.FirstOrDefault() ?? string.Empty;
        
        OpenFolderCommand = new RelayCommand(OpenFolder, () => !string.IsNullOrEmpty(_lastGeneratedPath));
    }

    public new string Title => "Reports";
    public new string Description => "Generate, print or export weighment reports.";

    public ObservableCollection<string> AvailableReports { get; } = new();

    public string SelectedReport
    {
        get => _selectedReport;
        set
        {
            if (SetProperty(ref _selectedReport, value))
            {
                _generateCommand.NotifyCanExecuteChanged();
            }
        }
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

    public string PartyName
    {
        get => _partyName;
        set => SetProperty(ref _partyName, value);
    }

    public string MaterialName
    {
        get => _materialName;
        set => SetProperty(ref _materialName, value);
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

    public ICommand GenerateCommand => _generateCommand;
    public ICommand OpenFolderCommand { get; }

    public override Task OnNavigatedToAsync(NavigationContext context)
    {
        ClearStatus();
        return Task.CompletedTask;
    }

    private async Task GenerateAsync()
    {
        IsBusy = true;
        ClearStatus();

        try
        {
            var parameters = new Dictionary<string, object?>
            {
                { "StartDate", StartDate },
                { "EndDate", EndDate },
                { "PartyName", PartyName },
                { "MaterialName", MaterialName }
            };

            var format = _reportService.SupportedFormats.FirstOrDefault();
            
            var result = await _reportService.GenerateAsync(SelectedReport, parameters, format, null, CancellationToken.None).ConfigureAwait(true);

            if (result.Succeeded)
            {
                // OutputPath, not Message. This read result.Message - "Report generated." - so
                // "Show in Explorer" ran explorer.exe /select,"Report generated.", and the
                // operator was never told where the file had gone.
                _lastGeneratedPath = result.OutputPath;
                _audit.Record("Generated", "Report", SelectedReport, $"Start: {StartDate:d}, End: {EndDate:d}");
                Show($"Report saved to {result.OutputPath}", BadgeSeverity.Success);
                (OpenFolderCommand as RelayCommand)?.NotifyCanExecuteChanged();
            }
            else
            {
                Show(result.Message, BadgeSeverity.Danger);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenFolder()
    {
        if (!string.IsNullOrEmpty(_lastGeneratedPath))
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{_lastGeneratedPath}\"");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open folder for {Path}", _lastGeneratedPath);
            }
        }
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
        _logger.LogError(error, "Reports could not complete an action.");
        Show("Something went wrong on this screen. The log has the detail.", BadgeSeverity.Danger);
    }
}
