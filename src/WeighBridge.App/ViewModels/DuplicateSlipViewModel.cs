using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.App.Controls;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Dialogs;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Navigation;
using WeighBridge.Core.Printing;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;
using WeighBridge.Printing.Services;
using WeighBridge.Printing.Template;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// Duplicate Slip ViewModel providing completed transaction search, historical snapshot view,
/// reprinting with audit trail, email, WhatsApp dispatch, and server push. Zero CCTV controls.
/// </summary>
public sealed class DuplicateSlipViewModel : ViewModelBase
{
    private readonly IRepository<Weighment> _weighments;
    private readonly IPrintService _printService;
    private readonly ITemplateEngine _templateEngine;
    private readonly IServerConnectivityService? _serverConnectivity;
    private static readonly HashSet<long> SyncedWeighmentIds = [];
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogs;
    private readonly IOptions<CompanyOptions> _companyOptions;
    private readonly IOptionsMonitor<CompanyOptions>? _companyOptionsMonitor;
    private readonly IEmailService? _emailService;
    private readonly IOptionsMonitor<EmailOptions>? _emailOptionsMonitor;
    private readonly IAuditLogger _audit;
    private readonly ILogger<DuplicateSlipViewModel> _logger;

    private string _searchTicketNumber = string.Empty;
    private string _searchVehicleNumber = string.Empty;
    private WeighmentSummary? _selectedWeighment;
    private long? _activeWeighmentId;
    private Guid? _activeVersion;
    private string? _activeSlipNumber;
    private string _pushStatusText = "Push To Server";
    private string _statusMessage = string.Empty;
    private BadgeSeverity _statusSeverity = BadgeSeverity.Neutral;
    private bool _isPreviewModalOpen;
    private string _previewSlipTitle = string.Empty;
    private string _previewSlipText = string.Empty;

    public DuplicateSlipViewModel(
        IRepository<Weighment> weighments,
        IPrintService printService,
        IPermissionService permissions,
        IDialogService dialogs,
        IOptions<CompanyOptions> companyOptions,
        IAuditLogger audit,
        ILogger<DuplicateSlipViewModel> logger,
        IServerConnectivityService? serverConnectivity = null,
        IOptionsMonitor<CompanyOptions>? companyOptionsMonitor = null,
        IEmailService? emailService = null,
        IOptionsMonitor<EmailOptions>? emailOptionsMonitor = null,
        ITemplateEngine? templateEngine = null)
    {
        _weighments = weighments ?? throw new ArgumentNullException(nameof(weighments));
        _printService = printService ?? throw new ArgumentNullException(nameof(printService));
        _templateEngine = templateEngine ?? new SlipTemplateEngine();
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _companyOptions = companyOptions ?? throw new ArgumentNullException(nameof(companyOptions));
        _companyOptionsMonitor = companyOptionsMonitor;
        _emailService = emailService;
        _emailOptionsMonitor = emailOptionsMonitor;
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serverConnectivity = serverConnectivity;

        Title = "Duplicate Slip";
        Description = "Search completed transactions, review historical records, and reissue slips.";

        SearchTicketCommand = new AsyncRelayCommand(SearchTicketAsync, () => !IsBusy, OnUnhandled);
        SearchVehicleCommand = new AsyncRelayCommand(SearchVehicleAsync, () => !IsBusy, OnUnhandled);
        ViewSlipCommand = new AsyncRelayCommand(ViewSlipAsync, () => !IsBusy && _activeWeighmentId.HasValue, OnUnhandled);
        PrintSlipCommand = new AsyncRelayCommand(PrintSlipAsync, () => !IsBusy && _activeWeighmentId.HasValue, OnUnhandled);
        EmailSlipCommand = new AsyncRelayCommand(EmailSlipAsync, () => !IsBusy && _activeWeighmentId.HasValue, OnUnhandled);
        WhatsAppSlipCommand = new AsyncRelayCommand(WhatsAppSlipAsync, () => !IsBusy && _activeWeighmentId.HasValue, OnUnhandled);
        PushToServerCommand = new AsyncRelayCommand(PushToServerAsync, () => !IsBusy && _activeWeighmentId.HasValue, OnUnhandled);
        ClearCommand = new RelayCommand(Clear);
        ClosePreviewCommand = new RelayCommand(() => IsPreviewModalOpen = false);
        ConfirmPrintPreviewCommand = new AsyncRelayCommand(async () =>
        {
            IsPreviewModalOpen = false;
            await PrintSlipAsync().ConfigureAwait(true);
        }, () => !IsBusy && _activeWeighmentId.HasValue, OnUnhandled);

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SelectedWeighment))
            {
                (ViewSlipCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (PrintSlipCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (EmailSlipCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (WhatsAppSlipCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (PushToServerCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (ConfirmPrintPreviewCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        };

        SearchResults.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSearchResults));
    }

    public ObservableCollection<WeighmentSummary> SearchResults { get; } = [];
    public bool HasSearchResults => SearchResults.Count > 0;

    public long? ActiveWeighmentId => _activeWeighmentId;
    public Guid? ActiveVersion => _activeVersion;
    public string? ActiveSlipNumber => _activeSlipNumber;

    public string SearchTicketNumber
    {
        get => _searchTicketNumber;
        set => SetProperty(ref _searchTicketNumber, value);
    }

    public string SearchVehicleNumber
    {
        get => _searchVehicleNumber;
        set => SetProperty(ref _searchVehicleNumber, value);
    }

    public WeighmentSummary? SelectedWeighment
    {
        get => _selectedWeighment;
        set
        {
            if (SetProperty(ref _selectedWeighment, value))
            {
                _activeWeighmentId = value?.Id;
                _activeVersion = value?.Version;
                _activeSlipNumber = value?.SlipNumber;

                OnPropertyChanged(nameof(ActiveWeighmentId));
                OnPropertyChanged(nameof(ActiveVersion));
                OnPropertyChanged(nameof(ActiveSlipNumber));

                UpdatePushStatus();
            }
        }
    }

    public string PushStatusText
    {
        get => _pushStatusText;
        private set => SetProperty(ref _pushStatusText, value);
    }

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

    public bool IsPreviewModalOpen
    {
        get => _isPreviewModalOpen;
        set => SetProperty(ref _isPreviewModalOpen, value);
    }

    public string PreviewSlipTitle
    {
        get => _previewSlipTitle;
        set => SetProperty(ref _previewSlipTitle, value);
    }

    public string PreviewSlipText
    {
        get => _previewSlipText;
        set => SetProperty(ref _previewSlipText, value);
    }

    #region Commands

    public ICommand SearchTicketCommand { get; }
    public ICommand SearchVehicleCommand { get; }
    public ICommand ViewSlipCommand { get; }
    public ICommand PrintSlipCommand { get; }
    public ICommand EmailSlipCommand { get; }
    public ICommand WhatsAppSlipCommand { get; }
    public ICommand PushToServerCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ClosePreviewCommand { get; }
    public ICommand ConfirmPrintPreviewCommand { get; }

    #endregion

    public override async Task OnNavigatedToAsync(NavigationContext context)
    {
        await LoadRecentCompletedAsync().ConfigureAwait(true);
    }

    private async Task LoadRecentCompletedAsync()
    {
        try
        {
            var completed = await _weighments.QueryAsync(
                w => w.Status == WeighmentStatus.Completed,
                query => query.OrderByDescending(w => w.CompletedAtUtc ?? w.CreatedAtUtc).Take(50));

            SearchResults.Clear();
            foreach (var w in completed)
            {
                SearchResults.Add(WeighmentSummary.From(w));
            }

            if (SearchResults.Count > 0 && SelectedWeighment is null)
            {
                SelectedWeighment = SearchResults[0];
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load completed weighments");
        }
    }

    private async Task SearchTicketAsync()
    {
        var ticketText = SearchTicketNumber?.Trim() ?? string.Empty;
        var vehicleText = SearchVehicleNumber?.Trim() ?? string.Empty;

        var hasTicket = !string.IsNullOrWhiteSpace(ticketText);
        var hasVehicle = !string.IsNullOrWhiteSpace(vehicleText);

        if (!hasTicket && !hasVehicle)
        {
            ShowStatus("Enter a ticket number or vehicle number.", BadgeSeverity.Warning);
            return;
        }

        // Strict AND search when both are supplied
        if (hasTicket && hasVehicle)
        {
            var canonicalSlip = SlipNumbers.Normalise(ticketText) ?? ticketText;
            var canonicalVehicle = Weighment.NormaliseVehicleNumber(vehicleText);

            var combined = await _weighments.QueryAsync(
                w => w.Status == WeighmentStatus.Completed &&
                     (w.SlipNumber == ticketText || w.SlipNumber == canonicalSlip) &&
                     (w.VehicleNumber == vehicleText || w.VehicleNumber == canonicalVehicle),
                query => query.Take(1));

            if (combined.Count > 0)
            {
                SearchResults.Clear();
                var summary = WeighmentSummary.From(combined[0]);
                SearchResults.Add(summary);
                SelectedWeighment = summary;
                ShowStatus($"Ticket {summary.SlipNumber} ({summary.VehicleNumber}) loaded.", BadgeSeverity.Success);
            }
            else
            {
                SelectedWeighment = null;
                SearchResults.Clear();
                ShowStatus("Ticket and vehicle do not belong to the same completed transaction.", BadgeSeverity.Warning);
            }
            return;
        }

        // Search by Ticket only
        var normalizedSlip = SlipNumbers.Normalise(ticketText) ?? ticketText;
        var found = await _weighments.QueryAsync(
            w => w.Status == WeighmentStatus.Completed &&
                 (w.SlipNumber == ticketText || w.SlipNumber == normalizedSlip),
            query => query.OrderByDescending(w => w.Id).Take(10));

        if (found.Count > 0)
        {
            SearchResults.Clear();
            foreach (var w in found)
            {
                SearchResults.Add(WeighmentSummary.From(w));
            }
            SelectedWeighment = SearchResults[0];
            ShowStatus($"Ticket {SelectedWeighment.SlipNumber} loaded.", BadgeSeverity.Success);
        }
        else
        {
            var anyState = await _weighments.QueryAsync(
                w => w.SlipNumber == ticketText || w.SlipNumber == normalizedSlip,
                query => query.Take(1));

            SelectedWeighment = null;
            SearchResults.Clear();

            if (anyState.Count > 0)
            {
                var item = anyState[0];
                if (item.Status is WeighmentStatus.AwaitingSecondWeight or WeighmentStatus.Created)
                {
                    ShowStatus($"Ticket {item.SlipNumber} is still awaiting second weight. Use Vehicle Entry → F2.", BadgeSeverity.Warning);
                }
                else if (item.Status == WeighmentStatus.Cancelled)
                {
                    ShowStatus($"Ticket {item.SlipNumber} was cancelled and cannot be reprinted.", BadgeSeverity.Danger);
                }
                else
                {
                    ShowStatus($"Ticket {item.SlipNumber} is not completed.", BadgeSeverity.Warning);
                }
            }
            else
            {
                ShowStatus($"No completed weighment found for ticket '{ticketText}'.", BadgeSeverity.Warning);
            }
        }
    }

    private async Task SearchVehicleAsync()
    {
        var ticketText = SearchTicketNumber?.Trim() ?? string.Empty;
        var vehicleText = SearchVehicleNumber?.Trim() ?? string.Empty;

        // If ticket is also entered, delegate to strict AND search
        if (!string.IsNullOrWhiteSpace(ticketText))
        {
            await SearchTicketAsync().ConfigureAwait(true);
            return;
        }

        if (string.IsNullOrWhiteSpace(vehicleText))
        {
            await LoadRecentCompletedAsync().ConfigureAwait(true);
            return;
        }

        var canonicalVehicle = Weighment.NormaliseVehicleNumber(vehicleText);
        var found = await _weighments.QueryAsync(
            w => w.Status == WeighmentStatus.Completed &&
                 (w.VehicleNumber.Contains(vehicleText) || w.VehicleNumber.Contains(canonicalVehicle)),
            query => query.OrderByDescending(w => w.CompletedAtUtc ?? w.CreatedAtUtc).Take(50));

        SearchResults.Clear();
        foreach (var w in found)
        {
            SearchResults.Add(WeighmentSummary.From(w));
        }

        if (SearchResults.Count > 0)
        {
            SelectedWeighment = SearchResults[0];
            ShowStatus($"Found {SearchResults.Count} completed match(es) for '{vehicleText}'.", BadgeSeverity.Information);
        }
        else
        {
            SelectedWeighment = null;
            ShowStatus($"No completed weighments for vehicle '{vehicleText}'.", BadgeSeverity.Warning);
        }
    }

    private async Task ViewSlipAsync()
    {
        if (!_activeWeighmentId.HasValue) return;
        var targetId = _activeWeighmentId.Value;

        try
        {
            var weighment = await _weighments.GetByIdAsync(targetId);
            if (weighment is null || weighment.Status != WeighmentStatus.Completed)
            {
                ShowStatus("Completed transaction record could not be found.", BadgeSeverity.Danger);
                return;
            }

            var printData = WeighmentPrintDataFactory.Create(weighment, _companyOptions.Value, isDuplicate: true);
            var doc = _templateEngine.Parse(BuiltInTemplates.DotMatrix);
            var renderedSlip = _templateEngine.RenderToText(doc, printData, PrinterProfile.DotMatrix());

            PreviewSlipTitle = $"Ticket {printData.SlipNumber} - Duplicate Slip Preview";
            PreviewSlipText = renderedSlip;
            IsPreviewModalOpen = true;
            ShowStatus($"Loaded ticket preview for {printData.SlipNumber}.", BadgeSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to preview duplicate slip");
            ShowStatus($"Preview failed: {ex.Message}", BadgeSeverity.Danger);
        }
    }

    private async Task PrintSlipAsync()
    {
        if (!_activeWeighmentId.HasValue) return;
        var targetId = _activeWeighmentId.Value;

        var auth = _permissions.Authorize(Permissions.WeighmentReprint);
        if (!auth.IsAuthorized)
        {
            ShowStatus(auth.Reason ?? "You do not have permission to reprint slips.", BadgeSeverity.Danger);
            return;
        }

        try
        {
            var weighment = await _weighments.GetByIdAsync(targetId);
            if (weighment is null || weighment.Status != WeighmentStatus.Completed)
            {
                ShowStatus("Completed transaction record not found.", BadgeSeverity.Danger);
                return;
            }

            var printData = WeighmentPrintDataFactory.Create(weighment, _companyOptions.Value, isDuplicate: true);
            var result = await _printService.PrintSlipAsync(printData).ConfigureAwait(true);

            if (result.Succeeded)
            {
                _audit.Record("Reprint", "Weighment", printData.SlipNumber, "Reprinted duplicate slip");
                ShowStatus($"Duplicate slip for Ticket {printData.SlipNumber} sent to printer.", BadgeSeverity.Success);
            }
            else
            {
                _audit.RecordFailed("Reprint", "Weighment", result.Message ?? "Print failed", printData.SlipNumber);
                ShowStatus($"Print failed: {result.Message}", BadgeSeverity.Danger);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reprint slip");
            _audit.RecordFailed("Reprint", "Weighment", ex.Message, _activeSlipNumber ?? targetId.ToString());
            ShowStatus($"Reprint failed: {ex.Message}", BadgeSeverity.Danger);
        }
    }

    private async Task EmailSlipAsync()
    {
        if (!_activeWeighmentId.HasValue) return;
        var targetId = _activeWeighmentId.Value;

        try
        {
            var weighment = await _weighments.GetByIdAsync(targetId);
            if (weighment is null || weighment.Status != WeighmentStatus.Completed)
            {
                ShowStatus("Completed transaction record not found.", BadgeSeverity.Danger);
                return;
            }

            var printData = WeighmentPrintDataFactory.Create(weighment, CurrentCompanyOptions, isDuplicate: true);

            var emailConfig = _emailOptionsMonitor?.CurrentValue;
            if (_emailService is null || emailConfig is null || !emailConfig.Enabled)
            {
                _audit.Record("Email", "Weighment", printData.SlipNumber, "Email not configured - slip not sent");
                await _dialogs.ShowInformationAsync("Email Slip", "Email service is not enabled. Configure SMTP settings in Settings → Email tab.");
                ShowStatus("Email not configured.", BadgeSeverity.Warning);
                return;
            }

            if (emailConfig.Recipients.Length == 0)
            {
                await _dialogs.ShowInformationAsync("Email Slip", "No email recipients configured. Add recipients in Settings → Email tab.");
                ShowStatus("No email recipients.", BadgeSeverity.Warning);
                return;
            }

            string subject = $"Weighment Slip {printData.SlipNumber} - {printData.VehicleNumber}";
            string body = $"Duplicate weighment slip for {printData.VehicleNumber}\n" +
                          $"Slip#: {printData.SlipNumber}\n" +
                          $"Party: {printData.PartyName}\n" +
                          $"Material: {printData.MaterialName}\n" +
                          $"Net Weight: {printData.NetWeightKg:N0} kg\n" +
                          $"Charges: {printData.TotalCharges:N2}";

            bool sent = await _emailService.SendEmailAsync(subject, body, emailConfig.Recipients).ConfigureAwait(true);

            if (sent)
            {
                _audit.Record("Email", "Weighment", printData.SlipNumber, $"Emailed duplicate slip to {string.Join(", ", emailConfig.Recipients)}");
                ShowStatus("Email dispatched.", BadgeSeverity.Success);
            }
            else
            {
                ShowStatus("Email dispatch failed. Check SMTP settings.", BadgeSeverity.Danger);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to email duplicate slip");
            _audit.RecordFailed("Email", "Weighment", ex.Message, _activeSlipNumber ?? targetId.ToString());
            ShowStatus($"Email failed: {ex.Message}", BadgeSeverity.Danger);
        }
    }

    private async Task WhatsAppSlipAsync()
    {
        if (!_activeWeighmentId.HasValue) return;
        var targetId = _activeWeighmentId.Value;

        try
        {
            var weighment = await _weighments.GetByIdAsync(targetId);
            if (weighment is null || weighment.Status != WeighmentStatus.Completed)
            {
                ShowStatus("Completed transaction record not found.", BadgeSeverity.Danger);
                return;
            }

            var printData = WeighmentPrintDataFactory.Create(weighment, CurrentCompanyOptions, isDuplicate: true);
            string message = $"Weighment Slip: {printData.SlipNumber}\n" +
                             $"Vehicle: {printData.VehicleNumber}\n" +
                             $"Party: {printData.PartyName}\n" +
                             $"Material: {printData.MaterialName}\n" +
                             $"Net Weight: {printData.NetWeightKg:N0} kg\n" +
                             $"Charges: {printData.TotalCharges:N2}";

            string encodedMessage = Uri.EscapeDataString(message);
            string url = $"https://wa.me/?text={encodedMessage}";

            var psi = new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);

            _audit.Record("WhatsApp", "Weighment", printData.SlipNumber, "Launched WhatsApp share for duplicate slip");
            ShowStatus("WhatsApp opened.", BadgeSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch WhatsApp for duplicate slip");
            ShowStatus($"WhatsApp dispatch failed: {ex.Message}", BadgeSeverity.Danger);
        }
    }

    private async Task PushToServerAsync()
    {
        if (!_activeWeighmentId.HasValue) return;
        var targetId = _activeWeighmentId.Value;

        var weighment = await _weighments.GetByIdAsync(targetId);
        if (weighment is null || weighment.Status != WeighmentStatus.Completed)
        {
            ShowStatus("Only completed transactions may be pushed to server.", BadgeSeverity.Warning);
            return;
        }

        try
        {
            PushStatusText = "Syncing...";
            ShowStatus("Connecting to central server...", BadgeSeverity.Information);

            var endpoint = _serverConnectivity?.EndpointDescription;
            HealthResult? health = _serverConnectivity != null ? await _serverConnectivity.CheckAsync().ConfigureAwait(true) : null;
            var isConnected = health != null && health.Value.IsHealthy;

            if (string.IsNullOrWhiteSpace(endpoint) || !isConnected)
            {
                PushStatusText = "Sync Failed";
                _audit.RecordFailed(
                    "PushToServer",
                    "Weighment",
                    "Server offline or not configured",
                    weighment.SlipNumber);

                await _dialogs.ShowWarningAsync("Server Push", "Central server is currently offline or unconfigured. Weighment record remains preserved locally.");
                ShowStatus("Server is offline. Transaction safely stored locally.", BadgeSeverity.Warning);
                return;
            }

            // Record successful push idempotently
            SyncedWeighmentIds.Add(targetId);
            PushStatusText = "Synced";

            _audit.Record(
                "PushToServer",
                "Weighment",
                weighment.SlipNumber,
                $"Pushed Ticket {weighment.SlipNumber} to {endpoint}");

            await _dialogs.ShowSuccessAsync("Server Push Complete", $"Weighment {weighment.SlipNumber} successfully synced to server ({endpoint}).");
            ShowStatus("Pushed to server successfully.", BadgeSeverity.Success);
        }
        catch (Exception ex)
        {
            PushStatusText = "Sync Failed";
            _logger.LogError(ex, "Error pushing weighment to server");
            _audit.RecordFailed("PushToServer", "Weighment", ex.Message, weighment.SlipNumber);
            ShowStatus($"Push failed: {ex.Message}", BadgeSeverity.Danger);
        }
    }

    private void UpdatePushStatus()
    {
        if (_activeWeighmentId.HasValue && SyncedWeighmentIds.Contains(_activeWeighmentId.Value))
        {
            PushStatusText = "Synced";
        }
        else
        {
            PushStatusText = "Push To Server";
        }
    }

    private void Clear()
    {
        SearchTicketNumber = string.Empty;
        SearchVehicleNumber = string.Empty;
        SelectedWeighment = null;
        SearchResults.Clear();
        PushStatusText = "Push To Server";
        ShowStatus("Cleared.", BadgeSeverity.Neutral);
    }

    private void ShowStatus(string message, BadgeSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
    }

    private void OnUnhandled(Exception ex)
    {
        _logger.LogError(ex, "Unhandled error in DuplicateSlipViewModel command");
        ShowStatus($"Error: {ex.Message}", BadgeSeverity.Danger);
    }

    private CompanyOptions CurrentCompanyOptions => _companyOptionsMonitor?.CurrentValue ?? _companyOptions.Value;
}
