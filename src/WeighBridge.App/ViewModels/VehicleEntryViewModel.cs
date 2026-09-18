using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.App.Controls;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Dialogs;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Navigation;
using WeighBridge.Core.Security;
using WeighBridge.Core.Threading;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Masters;
using WeighBridge.Domain.Weighments;
using WeighBridge.Services.Weighments;
using WeighBridge.Core.Events;
using WeighBridge.Core.Events.Catalog;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// Presentation state machine for the Vehicle Entry screen.
/// </summary>
public enum WeighmentWorkflowState
{
    /// <summary>Default resting state, pending queue visible, live scale streaming.</summary>
    Idle = 0,

    /// <summary>F1 mode: Operator entering initial vehicle and master information.</summary>
    F1Entry = 1,

    /// <summary>F1 mode: Persisted transaction created in SQLite, authoritative SlipNumber allocated and displayed.</summary>
    TicketAllocated = 2,

    /// <summary>F1 mode: Live weight captured via F3 / Read Indicator, ready for F5 first weight commit.</summary>
    AwaitingFirstWeight = 3,

    /// <summary>F2 mode: Second entry search active (search textbox focused).</summary>
    F2Entry = 4,

    /// <summary>F2 mode: Pending transaction loaded into active context, historical snapshot locked, F2 fields editable.</summary>
    F2Selected = 5,

    /// <summary>F2 mode: Second weight captured via F3, ready for F5 final completion.</summary>
    AwaitingSecondWeightCapture = 6,

    /// <summary>Second weight and F2 details atomically committed, summary displayed.</summary>
    Completed = 7
}

/// <summary>
/// The weighbridge operator's screen ViewModel: manages F1 first entry, F2 second entry, live indicator streaming,
/// scanner-style ticket search, historical snapshot locking, and optimistic concurrency.
/// </summary>
public sealed class VehicleEntryViewModel : ViewModelBase
{
    private static readonly WeighmentModeOption[] ArrivalModes =
    [
        new(WeighmentMode.GrossFirst, "Arrived loaded — gross weight first"),
        new(WeighmentMode.TareFirst, "Arrived empty — tare weight first"),
    ];

    private readonly ICommandExecutor _executor;
    private readonly IWeighmentService _weighments;
    private readonly IVehicleService _vehicleService;
    private readonly IPartyService _partyService;
    private readonly IMaterialService _materialService;
    private readonly IVehicleTypeService _vehicleTypeService;
    private readonly IWeightIndicatorService _indicator;
    private readonly ICameraService _cameraService;
    private readonly IPrintService _printService;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<VehicleEntryViewModel> _logger;
    private readonly IOptionsMonitor<WeighmentOptions>? _optionsMonitor;

    // Workflow state
    private WeighmentWorkflowState _workflowState = WeighmentWorkflowState.F1Entry;
    private long? _activeReservationId;
    private long? _activeWeighmentId;
    private Guid? _activeVersion;
    private string? _activeSlipNumber;
    private bool _isDirty;

    // F1 Inputs
    private string _vehicleNumber = string.Empty;
    private WeighmentModeOption _selectedArrivalMode = ArrivalModes[0];
    private string? _partyName;
    private string? _materialName;
    private string? _driverName;
    private string? _transporterName;
    private string? _remarks;
    private decimal _charges;
    private string? _customField1;
    private string? _customField2;

    // Master entity references
    private long? _selectedVehicleId;
    private long? _selectedPartyId;
    private long? _selectedMaterialId;
    private long? _selectedVehicleTypeId;
    private string? _selectedVehicleTypeName;
    private decimal? _standardTareWeightKg;

    private VehicleOption? _selectedVehicleOption;
    private PartyOption? _selectedPartyOption;
    private MaterialOption? _selectedMaterialOption;
    private VehicleTypeOption? _selectedVehicleTypeOption;

    // F2 Inputs
    private string _f2SearchKey = string.Empty;
    private decimal _secondCharges;
    private int? _numberOfBags;
    private decimal? _bagWeightKg;
    private string? _gatePassNumber;
    private string? _f2Remarks;
    private string? _customField3;
    private string? _customField4;
    private bool _hasConcurrencyConflict;

    // Common / Summary / HUD
    private WeighmentSummary? _current;
    private WeighmentSummary? _selectedAwaiting;
    private string _weightInput = string.Empty;
    private WeightSource _weightSource = WeightSource.Manual;
    private string _cancellationReason = string.Empty;

    private string _statusMessage = string.Empty;
    private BadgeSeverity _statusSeverity = BadgeSeverity.Neutral;
    private ConnectionState _indicatorState = ConnectionState.Unknown;

    private decimal _liveWeightKg;
    private string _liveWeightUnit = "Kg";
    private bool _isWeightStable;
    private string _stabilityStatusText = "DISCONNECTED";
    private BadgeSeverity _stabilitySeverity = BadgeSeverity.Neutral;
    private string _liveSourceText = "[Manual]";
    private string _cameraStatusText = "Cameras Offline";
    private bool _isAutoTareMode;
    private bool _isManualTareMode;
    private decimal? _manualTareKg;
    private string _grossWeightInput = string.Empty;
    private string _tareWeightInput = string.Empty;

    // Commands
    private readonly AsyncRelayCommand _switchToFirstEntry;
    private readonly AsyncRelayCommand _switchToSecondEntry;
    private readonly RelayCommand _selectGrossMode;
    private readonly RelayCommand _selectTareMode;
    private readonly RelayCommand _selectAutoTareMode;
    private readonly RelayCommand _selectManualTareMode;
    private readonly AsyncRelayCommand _allocateTicket;
    private readonly AsyncRelayCommand _recordFirstWeight;
    private readonly AsyncRelayCommand _recordSecondWeight;
    private readonly AsyncRelayCommand _searchPendingSecondEntry;
    private readonly AsyncRelayCommand _selectPendingTransaction;
    private readonly AsyncRelayCommand _reloadActiveTransaction;
    private readonly AsyncRelayCommand _submitWorkflow;
    private readonly AsyncRelayCommand _cancelWeighment;
    private readonly AsyncRelayCommand _readIndicator;
    private readonly INavigationService? _navigationService;
    private readonly AsyncRelayCommand _refresh;
    private readonly AsyncRelayCommand _clearContext;
    private readonly AsyncRelayCommand _printSlip;
    private readonly AsyncRelayCommand _openSettings;

    public VehicleEntryViewModel(
        ICommandExecutor executor,
        IWeighmentService weighments,
        IVehicleService vehicleService,
        IPartyService partyService,
        IMaterialService materialService,
        IVehicleTypeService vehicleTypeService,
        IWeightIndicatorService indicator,
        ICameraService cameraService,
        IPrintService printService,
        IPermissionService permissions,
        IDialogService dialogs,
        IUiDispatcher dispatcher,
        ILogger<VehicleEntryViewModel> logger,
        INavigationService? navigationService = null,
        IOptionsMonitor<WeighmentOptions>? optionsMonitor = null,
        IEventSubscriber? eventSubscriber = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _weighments = weighments ?? throw new ArgumentNullException(nameof(weighments));
        _vehicleService = vehicleService ?? throw new ArgumentNullException(nameof(vehicleService));
        _partyService = partyService ?? throw new ArgumentNullException(nameof(partyService));
        _materialService = materialService ?? throw new ArgumentNullException(nameof(materialService));
        _vehicleTypeService = vehicleTypeService ?? throw new ArgumentNullException(nameof(vehicleTypeService));
        _indicator = indicator ?? throw new ArgumentNullException(nameof(indicator));
        _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
        _printService = printService ?? throw new ArgumentNullException(nameof(printService));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _navigationService = navigationService;
        _optionsMonitor = optionsMonitor;

        Title = "Vehicle Entry";
        Description = "Industrial F1/F2 Weighment Workflow: First Entry, Second Entry, and Slip Completion.";

        _switchToFirstEntry = new AsyncRelayCommand(SwitchToFirstEntryAsync, () => !IsBusy && CanCreate, OnUnhandled);
        _switchToSecondEntry = new AsyncRelayCommand(SwitchToSecondEntryAsync, () => !IsBusy && CanEdit, OnUnhandled);
        _allocateTicket = new AsyncRelayCommand(AllocateTicketAsync, () => !IsBusy && CanCreate && WorkflowState == WeighmentWorkflowState.F1Entry, OnUnhandled);
        _recordFirstWeight = new AsyncRelayCommand(RecordFirstWeightAsync, () => !IsBusy && CanCreate && WorkflowState is WeighmentWorkflowState.TicketAllocated or WeighmentWorkflowState.AwaitingFirstWeight, OnUnhandled);
        _recordSecondWeight = new AsyncRelayCommand(RecordSecondWeightAsync, () => !IsBusy && CanEdit && WorkflowState is WeighmentWorkflowState.F2Selected or WeighmentWorkflowState.AwaitingSecondWeightCapture, OnUnhandled);
        _searchPendingSecondEntry = new AsyncRelayCommand(SearchPendingSecondEntryAsync, () => !IsBusy && CanEdit, OnUnhandled);
        _selectPendingTransaction = new AsyncRelayCommand(SelectPendingTransactionAsync, () => !IsBusy && CanEdit && SelectedAwaiting is not null, OnUnhandled);
        _reloadActiveTransaction = new AsyncRelayCommand(ReloadActiveTransactionAsync, () => !IsBusy && ActiveWeighmentId.HasValue, OnUnhandled);
        _submitWorkflow = new AsyncRelayCommand(SubmitWorkflowAsync, () => !IsBusy && CanSubmitCurrentWorkflow, OnUnhandled);
        _cancelWeighment = new AsyncRelayCommand(CancelWeighmentAsync, () => !IsBusy && CanCancel && Current is { IsOpen: true }, OnUnhandled);
        _readIndicator = new AsyncRelayCommand(ReadIndicatorAsync, () => !IsBusy, OnUnhandled);
        _refresh = new AsyncRelayCommand(RefreshAsync, () => !IsBusy, OnUnhandled);
        _clearContext = new AsyncRelayCommand(ClearContextAsync, () => !IsBusy, OnUnhandled);
        _printSlip = new AsyncRelayCommand(PrintSlipAsync, () => !IsBusy && Current is { Status: WeighmentStatus.Completed }, OnUnhandled);
        _openSettings = new AsyncRelayCommand(() => _navigationService?.NavigateToAsync<SettingsViewModel>() ?? Task.CompletedTask);
        _selectGrossMode = new RelayCommand(() => GrossTareText = "G");
        _selectTareMode = new RelayCommand(() => GrossTareText = "T");
        _selectAutoTareMode = new RelayCommand(() => GrossTareText = "A", () => IsAutoTareWeightEnabled);
        _selectManualTareMode = new RelayCommand(() => GrossTareText = "M", () => CanEnterManualWeight);

        PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName is nameof(IsBusy) or nameof(WorkflowState) or nameof(Current) or nameof(IndicatorState) or nameof(SelectedAwaiting) or nameof(WeightInput) or nameof(SelectedArrivalMode))
            {
                RefreshCommandStates();
                OnPropertyChanged(nameof(DisplayGrossWeightKg));
                OnPropertyChanged(nameof(DisplayTareWeightKg));
                OnPropertyChanged(nameof(DisplayNetWeightKg));
                OnPropertyChanged(nameof(EstimatedActualWeightKg));
                OnPropertyChanged(nameof(TotalMaterialAmount));
                OnPropertyChanged(nameof(GrossWeightText));
                OnPropertyChanged(nameof(TareWeightText));
                OnPropertyChanged(nameof(EntryModeText));
                OnPropertyChanged(nameof(GrossTareText));
                OnPropertyChanged(nameof(ModeLabelText));
                OnPropertyChanged(nameof(F2DerivedModeText));
                OnPropertyChanged(nameof(IsGrossFirstSelected));
                OnPropertyChanged(nameof(IsTareFirstSelected));
                OnPropertyChanged(nameof(IsAutoTareModeSelected));
                OnPropertyChanged(nameof(IsManualTareModeSelected));
                OnPropertyChanged(nameof(IsTareWeightReadOnly));
                OnPropertyChanged(nameof(IsGrossWeightReadOnly));
                OnPropertyChanged(nameof(IsSecondChargesVisible));
            }
        };

        if (_optionsMonitor is not null)
        {
            _optionsMonitor.OnChange(_ => _dispatcher.Post(RaiseRuntimeSettingsChanged));
        }

        if (eventSubscriber is not null)
        {
            eventSubscriber.Subscribe<SettingsChangedEvent>(evt =>
            {
                _dispatcher.Post(RaiseRuntimeSettingsChanged);
                if (evt.SectionName is "Catalog" or "Masters" or "All")
                {
                    _dispatcher.Post(async () => await RefreshAsync());
                }
            });
            eventSubscriber.Subscribe<VehicleSavedEvent>(_ => _dispatcher.Post(async () => await RefreshAsync()));
            eventSubscriber.Subscribe<VehicleCreatedEvent>(_ => _dispatcher.Post(async () => await RefreshAsync()));
            eventSubscriber.Subscribe<VehicleUpdatedEvent>(_ => _dispatcher.Post(async () => await RefreshAsync()));
            eventSubscriber.Subscribe<PartyCreatedEvent>(_ => _dispatcher.Post(async () => await RefreshAsync()));
            eventSubscriber.Subscribe<PartyUpdatedEvent>(_ => _dispatcher.Post(async () => await RefreshAsync()));
            eventSubscriber.Subscribe<MaterialCreatedEvent>(_ => _dispatcher.Post(async () => await RefreshAsync()));
            eventSubscriber.Subscribe<MaterialUpdatedEvent>(_ => _dispatcher.Post(async () => await RefreshAsync()));
        }
    }

    public ObservableCollection<VehicleOption> ActiveVehicles { get; } = [];
    public ObservableCollection<PartyOption> ActiveParties { get; } = [];
    public ObservableCollection<MaterialOption> ActiveMaterials { get; } = [];
    public ObservableCollection<VehicleTypeOption> ActiveVehicleTypes { get; } = [];
    public ObservableCollection<WeighmentSummary> AwaitingSecondWeight { get; } = [];
    public IReadOnlyList<WeighmentModeOption> ArrivalModeOptions => ArrivalModes;

    public string CurrentTimeDisplay => DateTime.Now.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture);

    public bool CanEnterManualWeight => RuntimeOptions.ManualTareEntry;

    public bool IsSecondEntryChargesEnabled => RuntimeOptions.SecondEntryCharges;

    public bool IsUnitBagsWeightColumnEnabled => RuntimeOptions.UnitBagsWeightColumn;

    public bool IsOnlySingleEntryEnabled => RuntimeOptions.OnlySingleEntry;

    public bool IsAutoTareWeightEnabled => RuntimeOptions.AutoTareWeight;

    public bool IsWeightHoldEnabled => RuntimeOptions.WeightHold;

    public bool IsGstOnChargesEnabled => RuntimeOptions.GstOnCharges;

    public decimal GstPercentage => RuntimeOptions.GstPercentage;

    public decimal GstAmount => IsGstOnChargesEnabled ? Math.Round(Charges * (GstPercentage / 100m), 2) : 0m;

    public decimal TotalChargesWithGst => Charges + GstAmount;

    public bool IsPriceComputingEnabled => RuntimeOptions.PriceComputing;

    public bool IsSecondChargesVisible => IsF2Mode && IsSecondEntryChargesEnabled;

    public string EntryModeText
    {
        get => WorkflowState switch
        {
            WeighmentWorkflowState.F2Entry or WeighmentWorkflowState.F2Selected or WeighmentWorkflowState.AwaitingSecondWeightCapture => "F2",
            _ => "F1"
        };
        set
        {
            if (string.Equals(value?.Trim(), "F2", StringComparison.OrdinalIgnoreCase))
            {
                _ = SwitchToSecondEntryAsync();
            }
            else if (string.Equals(value?.Trim(), "F1", StringComparison.OrdinalIgnoreCase))
            {
                _ = SwitchToFirstEntryAsync();
            }
        }
    }

    public ICommand SelectGrossModeCommand => _selectGrossMode;
    public ICommand SelectTareModeCommand => _selectTareMode;
    public ICommand SelectAutoTareModeCommand => _selectAutoTareMode;
    public ICommand SelectManualTareModeCommand => _selectManualTareMode;

    public string ModeLabelText => IsF2Mode
        ? "2nd Weight Role"
        : "Gross/Tare/Auto/Manual";

    public string F2DerivedModeText
    {
        get
        {
            if (!IsF2Mode) return string.Empty;
            if (Current is null) return "- [Select Pending Ticket]";
            return Current.Mode == WeighmentMode.GrossFirst
                ? "Tare (T) [2nd Weight]"
                : "Gross (G) [2nd Weight]";
        }
    }

    public bool IsGrossFirstSelected => IsF1Mode && !_isAutoTareMode && !_isManualTareMode && SelectedArrivalMode?.Value == WeighmentMode.GrossFirst;
    public bool IsTareFirstSelected => IsF1Mode && !_isAutoTareMode && !_isManualTareMode && SelectedArrivalMode?.Value == WeighmentMode.TareFirst;
    public bool IsAutoTareModeSelected => IsF1Mode && _isAutoTareMode && IsAutoTareWeightEnabled;
    public bool IsManualTareModeSelected => IsF1Mode && _isManualTareMode;
    public bool IsTareWeightReadOnly => IsF2Mode || _isAutoTareMode || (!_isManualTareMode && !CanEnterManualWeight);
    public bool IsGrossWeightReadOnly => IsF2Mode ? Current?.Mode == WeighmentMode.GrossFirst : (SelectedArrivalMode.Value == WeighmentMode.TareFirst && !_isAutoTareMode && !_isManualTareMode);

    public string GrossTareText
    {
        get
        {
            if (IsF2Mode)
            {
                // In F2, mode is strictly locked to the stored transaction complementary role
                return Current?.Mode == WeighmentMode.GrossFirst ? "T" : "G";
            }
            if (_isManualTareMode)
            {
                return "M";
            }
            if (_isAutoTareMode && IsAutoTareWeightEnabled)
            {
                return "A";
            }
            return SelectedArrivalMode?.Value == WeighmentMode.TareFirst ? "T" : "G";
        }
        set
        {
            if (IsF2Mode)
            {
                // In F2, mode cannot be manually altered; it is derived from stored transaction state
                return;
            }

            var trimmed = value?.Trim() ?? string.Empty;
            if (string.Equals(trimmed, "A", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsAutoTareWeightEnabled)
                {
                    Show("Auto Tare Weight is disabled in site settings.", BadgeSeverity.Warning);
                    return;
                }
                _isAutoTareMode = true;
                _isManualTareMode = false;
                SelectedArrivalMode = ArrivalModes.FirstOrDefault(m => m.Value == WeighmentMode.GrossFirst) ?? ArrivalModes[0];
                if (StandardTareWeightKg.HasValue)
                {
                    Show($"Auto Tare applied from Vehicle Master: {StandardTareWeightKg.Value:N0} kg.", BadgeSeverity.Information);
                }
                else
                {
                    Show("Vehicle does not have a standard tare weight registered in Vehicle Master.", BadgeSeverity.Warning);
                }
            }
            else if (string.Equals(trimmed, "M", StringComparison.OrdinalIgnoreCase))
            {
                if (!CanEnterManualWeight)
                {
                    Show("Manual Tare Entry is disabled in site settings.", BadgeSeverity.Warning);
                    return;
                }
                _isManualTareMode = true;
                _isAutoTareMode = false;
                SelectedArrivalMode = ArrivalModes.FirstOrDefault(m => m.Value == WeighmentMode.GrossFirst) ?? ArrivalModes[0];
                Show("Manual Tare mode [M] selected. You can enter tare weight directly.", BadgeSeverity.Information);
            }
            else if (string.Equals(trimmed, "T", StringComparison.OrdinalIgnoreCase))
            {
                _isAutoTareMode = false;
                _isManualTareMode = false;
                SelectedArrivalMode = ArrivalModes.FirstOrDefault(m => m.Value == WeighmentMode.TareFirst) ?? ArrivalModes[1];
            }
            else
            {
                _isAutoTareMode = false;
                _isManualTareMode = false;
                SelectedArrivalMode = ArrivalModes.FirstOrDefault(m => m.Value == WeighmentMode.GrossFirst) ?? ArrivalModes[0];
            }

            OnPropertyChanged(nameof(GrossTareText));
            OnPropertyChanged(nameof(IsAutoTareModeSelected));
            OnPropertyChanged(nameof(IsManualTareModeSelected));
            OnPropertyChanged(nameof(IsGrossFirstSelected));
            OnPropertyChanged(nameof(IsTareFirstSelected));
            OnPropertyChanged(nameof(IsTareWeightReadOnly));
            OnPropertyChanged(nameof(IsGrossWeightReadOnly));
            OnPropertyChanged(nameof(DisplayGrossWeightKg));
            OnPropertyChanged(nameof(DisplayTareWeightKg));
            OnPropertyChanged(nameof(DisplayNetWeightKg));
            OnPropertyChanged(nameof(EstimatedActualWeightKg));
            OnPropertyChanged(nameof(TotalMaterialAmount));
            OnPropertyChanged(nameof(GrossWeightText));
            OnPropertyChanged(nameof(TareWeightText));
        }
    }

    public string GrossWeightText
    {
        get
        {
            if (Current?.GrossKg.HasValue == true)
            {
                return Current.GrossKg.Value.ToString("N0", CultureInfo.InvariantCulture);
            }
            if (!string.IsNullOrWhiteSpace(_grossWeightInput))
            {
                return _grossWeightInput;
            }
            if (IsF1Mode && (SelectedArrivalMode?.Value == WeighmentMode.GrossFirst || SelectedArrivalMode == null || _isAutoTareMode || _isManualTareMode) &&
                !string.IsNullOrWhiteSpace(WeightInput))
            {
                return WeightInput;
            }
            return "0";
        }
        set
        {
            if (IsGrossWeightReadOnly) return;
            _grossWeightInput = value;
            OnPropertyChanged(nameof(GrossWeightText));
            OnPropertyChanged(nameof(DisplayGrossWeightKg));
            OnPropertyChanged(nameof(DisplayNetWeightKg));
            OnPropertyChanged(nameof(EstimatedActualWeightKg));
            OnPropertyChanged(nameof(TotalMaterialAmount));
            IsDirty = true;
        }
    }

    public string TareWeightText
    {
        get
        {
            if (Current?.TareKg.HasValue == true)
            {
                return Current.TareKg.Value.ToString("N0", CultureInfo.InvariantCulture);
            }
            if (_isAutoTareMode && StandardTareWeightKg.HasValue)
            {
                return StandardTareWeightKg.Value.ToString("N0", CultureInfo.InvariantCulture);
            }
            if (_isManualTareMode && _manualTareKg.HasValue)
            {
                return _manualTareKg.Value.ToString("N0", CultureInfo.InvariantCulture);
            }
            if (!string.IsNullOrWhiteSpace(_tareWeightInput))
            {
                return _tareWeightInput;
            }
            if (IsF1Mode && SelectedArrivalMode?.Value == WeighmentMode.TareFirst &&
                !_isAutoTareMode && !_isManualTareMode && !string.IsNullOrWhiteSpace(WeightInput))
            {
                return WeightInput;
            }
            return "0";
        }
        set
        {
            if (IsTareWeightReadOnly) return;
            if (_isManualTareMode)
            {
                _tareWeightInput = value;
                if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) ||
                    decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
                {
                    _manualTareKg = parsed;
                }
                else if (string.IsNullOrWhiteSpace(value))
                {
                    _manualTareKg = null;
                }
            }
            else
            {
                _tareWeightInput = value;
            }
            OnPropertyChanged(nameof(TareWeightText));
            OnPropertyChanged(nameof(DisplayTareWeightKg));
            OnPropertyChanged(nameof(DisplayNetWeightKg));
            OnPropertyChanged(nameof(EstimatedActualWeightKg));
            OnPropertyChanged(nameof(TotalMaterialAmount));
            IsDirty = true;
        }
    }

    public decimal DisplayGrossWeightKg
    {
        get
        {
            if (Current?.GrossKg.HasValue == true) return Current.GrossKg.Value;

            // F1 mode: read from _grossWeightInput, fallback to WeightInput
            if (IsF1Mode)
            {
                if (decimal.TryParse(_grossWeightInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var g) ||
                    decimal.TryParse(_grossWeightInput, NumberStyles.Number, CultureInfo.InvariantCulture, out g))
                {
                    return g;
                }
                if (SelectedArrivalMode?.Value == WeighmentMode.GrossFirst || SelectedArrivalMode == null || _isAutoTareMode || _isManualTareMode)
                {
                    if (decimal.TryParse(WeightInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var gFallback) ||
                        decimal.TryParse(WeightInput, NumberStyles.Number, CultureInfo.InvariantCulture, out gFallback))
                    {
                        return gFallback;
                    }
                }
                return 0m;
            }

            // F2 mode: use stored first weight or captured second weight
            if (WorkflowState is WeighmentWorkflowState.F2Selected or WeighmentWorkflowState.AwaitingSecondWeightCapture)
            {
                if (Current?.Mode == WeighmentMode.GrossFirst && Current.FirstWeightKg.HasValue)
                {
                    return Current.FirstWeightKg.Value;
                }
                // TareFirst F2: second weight is gross, read from _grossWeightInput or WeightInput
                if (Current?.Mode == WeighmentMode.TareFirst)
                {
                    if (decimal.TryParse(_grossWeightInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var g2) ||
                        decimal.TryParse(_grossWeightInput, NumberStyles.Number, CultureInfo.InvariantCulture, out g2))
                    {
                        return g2;
                    }
                    if (decimal.TryParse(WeightInput, NumberStyles.Number, CultureInfo.CurrentCulture, out g2) ||
                        decimal.TryParse(WeightInput, NumberStyles.Number, CultureInfo.InvariantCulture, out g2))
                    {
                        return g2;
                    }
                }
            }

            return 0m;
        }
    }

    public decimal DisplayTareWeightKg
    {
        get
        {
            if (Current?.TareKg.HasValue == true) return Current.TareKg.Value;

            if (IsF1Mode && _isAutoTareMode && StandardTareWeightKg.HasValue)
            {
                return StandardTareWeightKg.Value;
            }

            if (IsF1Mode && _isManualTareMode && _manualTareKg.HasValue)
            {
                return _manualTareKg.Value;
            }

            // F1 TareFirst mode: read from _tareWeightInput, fallback to WeightInput
            if (IsF1Mode)
            {
                if (decimal.TryParse(_tareWeightInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var t) ||
                    decimal.TryParse(_tareWeightInput, NumberStyles.Number, CultureInfo.InvariantCulture, out t))
                {
                    return t;
                }
                if (SelectedArrivalMode?.Value == WeighmentMode.TareFirst &&
                    !_isAutoTareMode && !_isManualTareMode)
                {
                    if (decimal.TryParse(WeightInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var tFallback) ||
                        decimal.TryParse(WeightInput, NumberStyles.Number, CultureInfo.InvariantCulture, out tFallback))
                    {
                        return tFallback;
                    }
                }
                return 0m;
            }

            // F2 mode: use stored first weight or captured second weight
            if (WorkflowState is WeighmentWorkflowState.F2Selected or WeighmentWorkflowState.AwaitingSecondWeightCapture)
            {
                if (Current?.Mode == WeighmentMode.TareFirst && Current.FirstWeightKg.HasValue)
                {
                    return Current.FirstWeightKg.Value;
                }
                // GrossFirst F2: second weight is tare, read from _tareWeightInput or WeightInput
                if (Current?.Mode == WeighmentMode.GrossFirst)
                {
                    if (decimal.TryParse(_tareWeightInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var t2) ||
                        decimal.TryParse(_tareWeightInput, NumberStyles.Number, CultureInfo.InvariantCulture, out t2))
                    {
                        return t2;
                    }
                    if (decimal.TryParse(WeightInput, NumberStyles.Number, CultureInfo.CurrentCulture, out t2) ||
                        decimal.TryParse(WeightInput, NumberStyles.Number, CultureInfo.InvariantCulture, out t2))
                    {
                        return t2;
                    }
                }
            }

            return 0m;
        }
    }

    public decimal DisplayNetWeightKg
    {
        get
        {
            if (Current?.NetKg.HasValue == true) return Current.NetKg.Value;

            var gross = DisplayGrossWeightKg;
            var tare = DisplayTareWeightKg;
            if (gross > 0m && tare > 0m && gross >= tare)
            {
                return gross - tare;
            }

            return 0m;
        }
    }

    public ICommand OpenSettingsCommand => _openSettings;

    #region State & Authorization Properties

    public WeighmentWorkflowState WorkflowState
    {
        get => _workflowState;
        private set
        {
            if (SetProperty(ref _workflowState, value))
            {
                OnPropertyChanged(nameof(IsF1Mode));
                OnPropertyChanged(nameof(IsF2Mode));
                OnPropertyChanged(nameof(IsF2SearchActive));
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(EntryModeText));
                OnPropertyChanged(nameof(CanSubmitCurrentWorkflow));
                OnPropertyChanged(nameof(WorkflowStateBadgeText));
                OnPropertyChanged(nameof(WorkflowStateBadgeSeverity));
                RefreshCommandStates();
            }
        }
    }

    public bool IsF1Mode => WorkflowState is WeighmentWorkflowState.F1Entry or WeighmentWorkflowState.TicketAllocated or WeighmentWorkflowState.AwaitingFirstWeight;
    public bool IsF2Mode => WorkflowState is WeighmentWorkflowState.F2Entry or WeighmentWorkflowState.F2Selected or WeighmentWorkflowState.AwaitingSecondWeightCapture;
    public bool IsF2SearchActive => WorkflowState == WeighmentWorkflowState.F2Entry;
    public bool IsIdle => WorkflowState == WeighmentWorkflowState.Idle;

    public long? ActiveReservationId
    {
        get => _activeReservationId;
        private set => SetProperty(ref _activeReservationId, value);
    }

    public long? ActiveWeighmentId
    {
        get => _activeWeighmentId;
        private set => SetProperty(ref _activeWeighmentId, value);
    }

    public Guid? ActiveVersion
    {
        get => _activeVersion;
        private set => SetProperty(ref _activeVersion, value);
    }

    public string? ActiveSlipNumber
    {
        get => _activeSlipNumber;
        private set => SetProperty(ref _activeSlipNumber, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }

    public bool HasConcurrencyConflict
    {
        get => _hasConcurrencyConflict;
        private set => SetProperty(ref _hasConcurrencyConflict, value);
    }

    public bool CanCreate => _permissions.HasPermission(Permissions.WeighmentCreate);
    public bool CanEdit => _permissions.HasPermission(Permissions.WeighmentEdit);
    public bool CanCancel => _permissions.HasPermission(Permissions.WeighmentCancel);

    public bool CanSubmitCurrentWorkflow => WorkflowState switch
    {
        WeighmentWorkflowState.F1Entry => CanCreate,
        WeighmentWorkflowState.TicketAllocated or WeighmentWorkflowState.AwaitingFirstWeight => CanCreate,
        WeighmentWorkflowState.F2Entry => CanEdit,
        WeighmentWorkflowState.F2Selected or WeighmentWorkflowState.AwaitingSecondWeightCapture => CanEdit,
        _ => false
    };

    public string WorkflowStateBadgeText => WorkflowState switch
    {
        WeighmentWorkflowState.F1Entry => "F1: First Entry (Entering Details)",
        WeighmentWorkflowState.TicketAllocated => "F1: Ticket Allocated (Awaiting Weight)",
        WeighmentWorkflowState.AwaitingFirstWeight => "F1: First Weight Captured",
        WeighmentWorkflowState.F2Entry => "F2: Second Entry (Search Pending)",
        WeighmentWorkflowState.F2Selected => "F2: Pending Ticket Loaded (F1 Locked)",
        WeighmentWorkflowState.AwaitingSecondWeightCapture => "F2: Second Weight Captured",
        WeighmentWorkflowState.Completed => "Transaction Completed",
        _ => "Idle / Ready"
    };

    public BadgeSeverity WorkflowStateBadgeSeverity => WorkflowState switch
    {
        WeighmentWorkflowState.F1Entry => BadgeSeverity.Information,
        WeighmentWorkflowState.TicketAllocated or WeighmentWorkflowState.AwaitingFirstWeight => BadgeSeverity.Warning,
        WeighmentWorkflowState.F2Entry or WeighmentWorkflowState.F2Selected or WeighmentWorkflowState.AwaitingSecondWeightCapture => BadgeSeverity.Information,
        WeighmentWorkflowState.Completed => BadgeSeverity.Success,
        _ => BadgeSeverity.Neutral
    };

    #endregion

    #region F1 Form Properties

    public string VehicleNumber
    {
        get => _vehicleNumber;
        set
        {
            if (SetProperty(ref _vehicleNumber, value))
            {
                IsDirty = true;
                MatchVehicleFromText(value);
            }
        }
    }

    public VehicleOption? SelectedVehicleOption
    {
        get => _selectedVehicleOption;
        set
        {
            if (SetProperty(ref _selectedVehicleOption, value) && value is not null)
            {
                _selectedVehicleId = value.Id;
                _vehicleNumber = value.VehicleNumber;
                OnPropertyChanged(nameof(VehicleNumber));
                StandardTareWeightKg = value.TareWeightKg;
                if (value.VehicleTypeId.HasValue)
                {
                    SelectedVehicleTypeOption = ActiveVehicleTypes.FirstOrDefault(t => t.Id == value.VehicleTypeId);
                }
            }
            else if (value is null)
            {
                _selectedVehicleId = null;
                StandardTareWeightKg = null;
            }
        }
    }

    public decimal? StandardTareWeightKg
    {
        get => _standardTareWeightKg;
        private set => SetProperty(ref _standardTareWeightKg, value, () => OnPropertyChanged(nameof(HasStandardTareWeight)));
    }

    public bool HasStandardTareWeight => StandardTareWeightKg.HasValue;

    public WeighmentModeOption SelectedArrivalMode
    {
        get => _selectedArrivalMode;
        set
        {
            if (value is not null && SetProperty(ref _selectedArrivalMode, value))
            {
                IsDirty = true;
            }
        }
    }

    public string? PartyName
    {
        get => _partyName;
        set
        {
            if (SetProperty(ref _partyName, value))
            {
                IsDirty = true;
                MatchPartyFromText(value);
            }
        }
    }

    public PartyOption? SelectedPartyOption
    {
        get => _selectedPartyOption;
        set
        {
            if (SetProperty(ref _selectedPartyOption, value) && value is not null)
            {
                _selectedPartyId = value.Id;
                _partyName = value.Name;
                OnPropertyChanged(nameof(PartyName));
            }
            else if (value is null)
            {
                _selectedPartyId = null;
            }
        }
    }

    public string? MaterialName
    {
        get => _materialName;
        set
        {
            if (SetProperty(ref _materialName, value))
            {
                IsDirty = true;
                MatchMaterialFromText(value);
            }
        }
    }

    public MaterialOption? SelectedMaterialOption
    {
        get => _selectedMaterialOption;
        set
        {
            if (SetProperty(ref _selectedMaterialOption, value) && value is not null)
            {
                _selectedMaterialId = value.Id;
                _materialName = value.Name;
                OnPropertyChanged(nameof(MaterialName));
            }
            else if (value is null)
            {
                _selectedMaterialId = null;
            }
        }
    }

    public VehicleTypeOption? SelectedVehicleTypeOption
    {
        get => _selectedVehicleTypeOption;
        set
        {
            if (SetProperty(ref _selectedVehicleTypeOption, value))
            {
                IsDirty = true;
                _selectedVehicleTypeId = value?.Id;
                _selectedVehicleTypeName = value?.Name;
                OnPropertyChanged(nameof(SelectedVehicleTypeName));
            }
        }
    }

    public string? SelectedVehicleTypeName
    {
        get => _selectedVehicleTypeName;
        set
        {
            if (SetProperty(ref _selectedVehicleTypeName, value))
            {
                IsDirty = true;

                if (string.IsNullOrWhiteSpace(value))
                {
                    _selectedVehicleTypeId = null;
                    _selectedVehicleTypeOption = null;
                    OnPropertyChanged(nameof(SelectedVehicleTypeOption));
                    return;
                }

                var trimmed = value.Trim();
                var match = ActiveVehicleTypes.FirstOrDefault(t => string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    _selectedVehicleTypeId = match.Id;
                    _selectedVehicleTypeOption = match;
                    IsDirty = true;
                    OnPropertyChanged(nameof(SelectedVehicleTypeOption));
                }
                else
                {
                    _selectedVehicleTypeId = null;
                    _selectedVehicleTypeOption = null;
                    IsDirty = true;
                    OnPropertyChanged(nameof(SelectedVehicleTypeOption));
                }
            }
        }
    }

    public string? DriverName
    {
        get => _driverName;
        set => SetProperty(ref _driverName, value, () => IsDirty = true);
    }

    public string? TransporterName
    {
        get => _transporterName;
        set => SetProperty(ref _transporterName, value, () => IsDirty = true);
    }

    public string? Remarks
    {
        get => _remarks;
        set => SetProperty(ref _remarks, value, () => IsDirty = true);
    }

    public decimal Charges
    {
        get => _charges;
        set => SetProperty(ref _charges, value, () =>
        {
            IsDirty = true;
            OnPropertyChanged(nameof(GstAmount));
            OnPropertyChanged(nameof(TotalChargesWithGst));
        });
    }

    private decimal _materialRate;
    public decimal MaterialRate
    {
        get => _materialRate;
        set => SetProperty(ref _materialRate, value, () =>
        {
            IsDirty = true;
            OnPropertyChanged(nameof(TotalMaterialAmount));
        });
    }

    public decimal TotalMaterialAmount => IsPriceComputingEnabled
        ? Math.Round(((EstimatedActualWeightKg.HasValue && EstimatedActualWeightKg.Value > 0m) ? EstimatedActualWeightKg.Value : DisplayNetWeightKg) * MaterialRate, 2)
        : 0m;

    public string? CustomField1
    {
        get => _customField1;
        set => SetProperty(ref _customField1, value, () => IsDirty = true);
    }

    public string? CustomField2
    {
        get => _customField2;
        set => SetProperty(ref _customField2, value, () => IsDirty = true);
    }

    #endregion

    #region F2 Form Properties

    public string F2SearchKey
    {
        get => _f2SearchKey;
        set => SetProperty(ref _f2SearchKey, value);
    }

    public decimal SecondCharges
    {
        get => _secondCharges;
        set => SetProperty(ref _secondCharges, value, () => IsDirty = true);
    }

    public int? NumberOfBags
    {
        get => _numberOfBags;
        set => SetProperty(ref _numberOfBags, value, () =>
        {
            IsDirty = true;
            OnPropertyChanged(nameof(TotalBagWeightKg));
            OnPropertyChanged(nameof(EstimatedActualWeightKg));
            OnPropertyChanged(nameof(TotalMaterialAmount));
        });
    }

    public decimal? BagWeightKg
    {
        get => _bagWeightKg;
        set => SetProperty(ref _bagWeightKg, value, () =>
        {
            IsDirty = true;
            OnPropertyChanged(nameof(TotalBagWeightKg));
            OnPropertyChanged(nameof(EstimatedActualWeightKg));
            OnPropertyChanged(nameof(TotalMaterialAmount));
        });
    }

    public bool HasBagDeduction => TotalBagWeightKg.HasValue && TotalBagWeightKg.Value > 0;

    public decimal? TotalBagWeightKg => (NumberOfBags.HasValue && BagWeightKg.HasValue)
        ? NumberOfBags.Value * BagWeightKg.Value
        : null;

    public decimal? EstimatedActualWeightKg
    {
        get
        {
            if (!TotalBagWeightKg.HasValue)
            {
                return null;
            }

            var net = DisplayNetWeightKg;
            if (net > 0m)
            {
                var actual = net - TotalBagWeightKg.Value;
                return actual >= 0m ? actual : 0m;
            }

            if (Current is not null)
            {
                if (decimal.TryParse(WeightInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var currentWeight) ||
                    decimal.TryParse(WeightInput, NumberStyles.Number, CultureInfo.InvariantCulture, out currentWeight))
                {
                    var first = Current.FirstWeightKg ?? 0m;
                    var gross = Current.Mode == WeighmentMode.GrossFirst ? first : currentWeight;
                    var tare = Current.Mode == WeighmentMode.GrossFirst ? currentWeight : first;
                    var n = gross - tare;
                    return n >= 0 ? Math.Max(0m, n - TotalBagWeightKg.Value) : null;
                }
            }

            return null;
        }
    }

    public string? GatePassNumber
    {
        get => _gatePassNumber;
        set => SetProperty(ref _gatePassNumber, value, () => IsDirty = true);
    }

    public string? F2Remarks
    {
        get => _f2Remarks;
        set => SetProperty(ref _f2Remarks, value, () => IsDirty = true);
    }

    public string? CustomField3
    {
        get => _customField3;
        set => SetProperty(ref _customField3, value, () => IsDirty = true);
    }

    public string? CustomField4
    {
        get => _customField4;
        set => SetProperty(ref _customField4, value, () => IsDirty = true);
    }

    #endregion

    #region Common & HUD Properties

    public WeighmentSummary? Current
    {
        get => _current;
        private set
        {
            if (SetProperty(ref _current, value))
            {
                OnPropertyChanged(nameof(HasCurrent));
                RefreshCommandStates();
            }
        }
    }

    public bool HasCurrent => Current is not null;

    public string WeightInput
    {
        get => _weightInput;
        set => SetProperty(ref _weightInput, value, () =>
        {
            _weightSource = WeightSource.Manual;
            OnPropertyChanged(nameof(EstimatedActualWeightKg));
            OnPropertyChanged(nameof(DisplayGrossWeightKg));
            OnPropertyChanged(nameof(DisplayTareWeightKg));
            OnPropertyChanged(nameof(DisplayNetWeightKg));
            OnPropertyChanged(nameof(GrossWeightText));
            OnPropertyChanged(nameof(TareWeightText));
            if (WorkflowState == WeighmentWorkflowState.TicketAllocated && !string.IsNullOrWhiteSpace(value))
            {
                WorkflowState = WeighmentWorkflowState.AwaitingFirstWeight;
            }
            else if (WorkflowState == WeighmentWorkflowState.F2Selected && !string.IsNullOrWhiteSpace(value))
            {
                WorkflowState = WeighmentWorkflowState.AwaitingSecondWeightCapture;
            }
        });
    }

    public string CancellationReason
    {
        get => _cancellationReason;
        set => SetProperty(ref _cancellationReason, value);
    }

    public WeighmentSummary? SelectedAwaiting
    {
        get => _selectedAwaiting;
        set => SetProperty(ref _selectedAwaiting, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value, () => OnPropertyChanged(nameof(HasStatus)));
    }

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);

    public BadgeSeverity StatusSeverity
    {
        get => _statusSeverity;
        private set => SetProperty(ref _statusSeverity, value);
    }

    public decimal LiveWeightKg
    {
        get => _liveWeightKg;
        private set => SetProperty(ref _liveWeightKg, value, () => OnPropertyChanged(nameof(LiveWeightDisplay)));
    }

    public string LiveWeightUnit
    {
        get => _liveWeightUnit;
        private set
        {
            var normalized = string.Equals(value, "kg", StringComparison.OrdinalIgnoreCase) ? "Kg" : value;
            if (SetProperty(ref _liveWeightUnit, normalized))
            {
                OnPropertyChanged(nameof(LiveWeightDisplay));
            }
        }
    }

    public string LiveWeightDisplay => IndicatorState == ConnectionState.Connected
        ? (LiveWeightKg % 1 == 0 ? $"{LiveWeightKg:N0} {LiveWeightUnit}" : $"{LiveWeightKg:0.##} {LiveWeightUnit}")
        : "--- " + LiveWeightUnit;

    public bool IsWeightStable
    {
        get => _isWeightStable;
        private set
        {
            if (SetProperty(ref _isWeightStable, value))
            {
                RefreshCommandStates();
            }
        }
    }

    public string StabilityStatusText
    {
        get => _stabilityStatusText;
        private set => SetProperty(ref _stabilityStatusText, value);
    }

    public BadgeSeverity StabilitySeverity
    {
        get => _stabilitySeverity;
        private set => SetProperty(ref _stabilitySeverity, value);
    }

    public string LiveSourceText
    {
        get => _liveSourceText;
        private set => SetProperty(ref _liveSourceText, value);
    }

    public string CameraStatusText
    {
        get => _cameraStatusText;
        private set => SetProperty(ref _cameraStatusText, value);
    }

    public ConnectionState IndicatorState
    {
        get => _indicatorState;
        private set
        {
            if (SetProperty(ref _indicatorState, value))
            {
                OnPropertyChanged(nameof(IndicatorText));
                OnPropertyChanged(nameof(LiveWeightDisplay));
                RefreshCommandStates();
            }
        }
    }

    public string IndicatorText => IndicatorState switch
    {
        ConnectionState.Connected => "Indicator connected",
        ConnectionState.Connecting => "Indicator connecting",
        ConnectionState.Degraded => "Indicator unreliable — check the reading",
        _ => "No indicator connected — type the weight",
    };

    #endregion

    #region Commands

    public ICommand SwitchToFirstEntryCommand => _switchToFirstEntry;
    public ICommand SwitchToSecondEntryCommand => _switchToSecondEntry;
    public ICommand AllocateTicketCommand => _allocateTicket;
    public ICommand RecordFirstWeightCommand => _recordFirstWeight;
    public ICommand RecordSecondWeightCommand => _recordSecondWeight;
    public ICommand SearchPendingSecondEntryCommand => _searchPendingSecondEntry;
    public ICommand SelectPendingTransactionCommand => _selectPendingTransaction;
    public ICommand ReloadActiveTransactionCommand => _reloadActiveTransaction;
    public ICommand SubmitWorkflowCommand => _submitWorkflow;
    public ICommand CancelWeighmentCommand => _cancelWeighment;
    public ICommand ReadIndicatorCommand => _readIndicator;
    public ICommand RefreshCommand => _refresh;
    public ICommand ClearContextCommand => _clearContext;
    public ICommand PrintSlipCommand => _printSlip;

    #endregion

    #region Lifecycle & Navigation

    public override async Task OnNavigatedToAsync(NavigationContext context)
    {
        _indicator.StateChanged -= OnIndicatorStateChanged;
        _indicator.StateChanged += OnIndicatorStateChanged;
        _indicator.ReadingReceived -= OnReadingReceived;
        _indicator.ReadingReceived += OnReadingReceived;

        _cameraService.StateChanged -= OnCameraStateChanged;
        _cameraService.StateChanged += OnCameraStateChanged;

        IndicatorState = _indicator.State;
        UpdateCameraStatus(_cameraService.State);

        if (_indicator.State != ConnectionState.Connected)
        {
            await _indicator.ConnectAsync().ConfigureAwait(true);
            IndicatorState = _indicator.State;
        }

        if (_cameraService.State != ConnectionState.Connected)
        {
            await _cameraService.ConnectAsync().ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
        await EnsureReservationAsync().ConfigureAwait(true);
    }

    public override Task OnNavigatedFromAsync()
    {
        _indicator.StateChanged -= OnIndicatorStateChanged;
        _indicator.ReadingReceived -= OnReadingReceived;
        _cameraService.StateChanged -= OnCameraStateChanged;
        return Task.CompletedTask;
    }

    private void OnReadingReceived(object? sender, WeightReading reading)
    {
        _dispatcher.Post(() =>
        {
            var displayVal = reading.Value < 0m ? 0m : reading.Value;
            LiveWeightKg = displayVal;
            LiveWeightUnit = string.IsNullOrWhiteSpace(reading.Unit) ? "Kg" : (string.Equals(reading.Unit, "kg", StringComparison.OrdinalIgnoreCase) ? "Kg" : reading.Unit);
            IsWeightStable = reading.IsStable;

            if (reading.IsZero || reading.IsNegative || displayVal == 0m)
            {
                StabilityStatusText = "ZERO";
                StabilitySeverity = BadgeSeverity.Neutral;
            }
            else if (reading.IsStable)
            {
                StabilityStatusText = "STABLE";
                StabilitySeverity = BadgeSeverity.Success;
            }
            else
            {
                StabilityStatusText = "UNSTABLE";
                StabilitySeverity = BadgeSeverity.Warning;
            }

            LiveSourceText = reading.Source switch
            {
                WeightSource.Simulator => "[Simulator]",
                WeightSource.Indicator => "[Hardware]",
                _ => "[Manual]",
            };
        });
    }

    private void OnIndicatorStateChanged(object? sender, ConnectionState state)
    {
        _dispatcher.Post(() =>
        {
            IndicatorState = state;
            if (state != ConnectionState.Connected)
            {
                StabilityStatusText = "DISCONNECTED";
                StabilitySeverity = BadgeSeverity.Danger;
                LiveSourceText = "[Manual]";
            }
            RefreshCommandStates();
        });
    }

    private void OnCameraStateChanged(object? sender, ConnectionState state)
    {
        _dispatcher.Post(() => UpdateCameraStatus(state));
    }

    private void UpdateCameraStatus(ConnectionState state)
    {
        CameraStatusText = state switch
        {
            ConnectionState.Connected => _cameraService.ConfiguredDevices.Count > 0
                ? $"Cameras Ready ({_cameraService.ConfiguredDevices.Count})"
                : "Cameras Ready (Test Mode)",
            ConnectionState.Disabled => "Cameras Disabled",
            _ => "Cameras Disconnected",
        };
    }

    public async Task RefreshAsync()
    {
        try
        {
            var vehicles = await _vehicleService.GetAllAsync(includeInactive: false).ConfigureAwait(true);
            var parties = await _partyService.GetAllAsync(includeInactive: false).ConfigureAwait(true);
            var materials = await _materialService.GetAllAsync(includeInactive: false).ConfigureAwait(true);
            var vehicleTypes = await _vehicleTypeService.GetAllAsync(includeInactive: false).ConfigureAwait(true);
            var pending = await _weighments.GetAwaitingSecondWeightAsync().ConfigureAwait(true);

            ActiveVehicles.Clear();
            foreach (var v in vehicles.OrderBy(v => v.VehicleNumber))
            {
                ActiveVehicles.Add(new VehicleOption(v.Id, v.VehicleNumber, v.VehicleTypeId, v.TareWeightKg));
            }

            ActiveParties.Clear();
            foreach (var p in parties.OrderBy(p => p.Name))
            {
                ActiveParties.Add(new PartyOption(p.Id, p.Name));
            }

            ActiveMaterials.Clear();
            foreach (var m in materials.OrderBy(m => m.Name))
            {
                ActiveMaterials.Add(new MaterialOption(m.Id, m.Name));
            }

            ActiveVehicleTypes.Clear();
            foreach (var t in vehicleTypes.OrderBy(t => t.TypeName))
            {
                ActiveVehicleTypes.Add(new VehicleTypeOption(t.Id, t.TypeName));
            }

            AwaitingSecondWeight.Clear();
            foreach (var w in pending.OrderByDescending(w => w.FirstWeight?.CapturedAtUtc ?? w.CreatedAtUtc))
            {
                AwaitingSecondWeight.Add(WeighmentSummary.From(w));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load master options or waiting list");
            Show("Could not load master data. Working with typed values.", BadgeSeverity.Warning);
        }
    }

    #endregion

    #region F1 Workflow Actions

    public async Task SwitchToFirstEntryAsync()
    {
        if (WorkflowState is WeighmentWorkflowState.F1Entry or WeighmentWorkflowState.TicketAllocated && ActiveReservationId.HasValue)
        {
            return;
        }

        if (IsDirty || ActiveWeighmentId.HasValue)
        {
            var confirm = await _dialogs.ShowConfirmationAsync(
                "Switch to First Entry",
                "You have an active or unsaved weighment context. Discard it and start a new First Entry?",
                confirmText: "Discard and Start F1",
                cancelText: "Keep Current Context",
                isDestructive: true).ConfigureAwait(true);

            if (!confirm)
            {
                return;
            }
        }

        ResetWorkflowContext();
        WorkflowState = WeighmentWorkflowState.F1Entry;
        await EnsureReservationAsync().ConfigureAwait(true);
    }

    public async Task EnsureReservationAsync()
    {
        if (ActiveReservationId.HasValue || ActiveWeighmentId.HasValue)
        {
            return;
        }

        try
        {
            // First check if there is already an active unconsumed reservation for this terminal
            var existing = await _weighments.GetActiveReservationAsync("LOCAL").ConfigureAwait(true);
            if (existing is not null)
            {
                ActiveReservationId = existing.Id;
                ActiveSlipNumber = existing.SlipNumber;
                WorkflowState = WeighmentWorkflowState.TicketAllocated;
                Show($"Active Ticket {existing.SlipNumber} loaded. Enter vehicle details and capture weight (F3 / F5).", BadgeSeverity.Information);
                return;
            }

            var result = await _executor
                .ExecuteAsync(new ReserveTicketCommand(_weighments, tentativeVehicleNumber: VehicleNumber, terminalId: "LOCAL"))
                .ConfigureAwait(true);

            if (result is { IsSuccess: true, Value: not null })
            {
                var reservation = result.Value;
                ActiveReservationId = reservation.Id;
                ActiveSlipNumber = reservation.SlipNumber;
                WorkflowState = WeighmentWorkflowState.TicketAllocated;
                Show($"Ticket {reservation.SlipNumber} reserved. Enter vehicle details and capture weight (F3 / F5).", BadgeSeverity.Information);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to allocate persistent ticket reservation on F1 entry");
            Show("Could not allocate ticket reservation. Retry F1.", BadgeSeverity.Warning);
        }
    }

    public async Task AllocateTicketAsync()
    {
        await EnsureReservationAsync().ConfigureAwait(true);
    }

    public async Task RecordFirstWeightAsync()
    {
        if (string.IsNullOrWhiteSpace(VehicleNumber))
        {
            Show("Enter a vehicle number before recording first weight.", BadgeSeverity.Warning);
            return;
        }

        if (!ActiveReservationId.HasValue)
        {
            await EnsureReservationAsync().ConfigureAwait(true);
            if (!ActiveReservationId.HasValue)
            {
                Show("No ticket reservation is active. Cannot record weight.", BadgeSeverity.Warning);
                return;
            }
        }

        if (!TryReadWeight(out var kilograms))
        {
            return;
        }

        var options = _optionsMonitor?.CurrentValue;
        if (options?.ChargesMandatory == true && Charges <= 0m)
        {
            Show("Weighing charges are mandatory according to site settings.", BadgeSeverity.Warning);
            return;
        }

        if (options?.MinimumCharges > 0m && Charges < options.MinimumCharges)
        {
            Show($"Weighing charges must be at least ₹{options.MinimumCharges:0.##}.", BadgeSeverity.Warning);
            return;
        }

        var custom1 = CustomField1;
        if (IsPriceComputingEnabled && MaterialRate > 0m && string.IsNullOrWhiteSpace(custom1))
        {
            custom1 = $"Rate: {MaterialRate:N2} | Total: {TotalMaterialAmount:N2}";
        }

        var request = new NewWeighment
        {
            VehicleNumber = VehicleNumber,
            Mode = SelectedArrivalMode.Value,
            PartyName = PartyName,
            MaterialName = MaterialName,
            DriverName = DriverName,
            TransporterName = TransporterName,
            Remarks = Remarks,
            Charges = Charges,
            NumberOfBags = IsUnitBagsWeightColumnEnabled ? NumberOfBags : null,
            BagWeightKg = IsUnitBagsWeightColumnEnabled ? BagWeightKg : null,
            CustomField1 = custom1,
            CustomField2 = CustomField2,
            VehicleId = _selectedVehicleId,
            PartyId = _selectedPartyId,
            MaterialId = _selectedMaterialId,
            VehicleTypeId = _selectedVehicleTypeId,
            VehicleTypeName = _selectedVehicleTypeName,
        };

        var source = _weightSource;
        CommandResult<Weighment> result;
        decimal? singleTare = (_isAutoTareMode && IsAutoTareWeightEnabled)
            ? StandardTareWeightKg
            : (_isManualTareMode ? _manualTareKg : (IsOnlySingleEntryEnabled ? StandardTareWeightKg : null));

        if (IsOnlySingleEntryEnabled || (_isAutoTareMode && IsAutoTareWeightEnabled && singleTare.HasValue) || (_isManualTareMode && singleTare.HasValue))
        {
            if (!singleTare.HasValue)
            {
                Show("Single-entry mode requires Auto Tare Weight or Manual Tare entry.", BadgeSeverity.Warning);
                return;
            }

            result = await _executor
                .ExecuteAsync(new RecordSingleEntryWeightWithReservationCommand(
                    _weighments,
                    ActiveReservationId.Value,
                    request,
                    kilograms,
                    source,
                    singleTare.Value))
                .ConfigureAwait(true);
        }
        else
        {
            result = await _executor
                .ExecuteAsync(new RecordFirstWeightWithReservationCommand(
                    _weighments,
                    ActiveReservationId.Value,
                    request,
                    kilograms,
                    source))
                .ConfigureAwait(true);
        }

        Report(result);

        if (result is not { IsSuccess: true, Value: not null })
        {
            return;
        }

        var saved = result.Value;
        await CaptureCameraSnapshotAsync(saved, "FirstWeight").ConfigureAwait(true);

        Current = WeighmentSummary.From(saved);
        ResetWorkflowContext();
        WorkflowState = IsOnlySingleEntryEnabled ? WeighmentWorkflowState.Completed : WeighmentWorkflowState.F1Entry;
        if (saved.Status == WeighmentStatus.Completed)
        {
            WorkflowState = WeighmentWorkflowState.Completed;
        }

        Show(
            saved.Status == WeighmentStatus.Completed
                ? $"Single-entry weighment {saved.SlipNumber} completed. Net {saved.NetWeightKg:0.##} kg."
                : $"First weight {kilograms:0.##} kg recorded on {saved.SlipNumber}. Vehicle queued for second weight.",
            BadgeSeverity.Success);

        await RefreshAsync().ConfigureAwait(true);

        if (WorkflowState != WeighmentWorkflowState.Completed)
        {
            await EnsureReservationAsync().ConfigureAwait(true);
        }
    }

    #endregion

    #region F2 Workflow Actions

    public async Task SwitchToSecondEntryAsync()
    {
        if (WorkflowState == WeighmentWorkflowState.F2Entry && !ActiveWeighmentId.HasValue)
        {
            return;
        }

        if (IsDirty || ActiveWeighmentId.HasValue)
        {
            var confirm = await _dialogs.ShowConfirmationAsync(
                "Switch to Second Entry",
                "You have an active or unsaved weighment context. Discard it and open Second Entry search?",
                confirmText: "Discard and Open F2",
                cancelText: "Keep Current Context",
                isDestructive: true).ConfigureAwait(true);

            if (!confirm)
            {
                return;
            }
        }

        ResetWorkflowContext();
        WorkflowState = WeighmentWorkflowState.F2Entry;
        Show("Second Entry mode (F2) activated. Scan or enter ticket / vehicle number.", BadgeSeverity.Information);
    }

    public async Task SearchPendingSecondEntryAsync()
    {
        if (string.IsNullOrWhiteSpace(F2SearchKey))
        {
            Show("Enter a ticket number or vehicle registration to search.", BadgeSeverity.Warning);
            return;
        }

        try
        {
            var found = await _weighments.FindPendingSecondEntryAsync(F2SearchKey).ConfigureAwait(true);

            if (found is null)
            {
                // Check if ticket exists in completed/cancelled state to provide specific operator feedback
                if (SlipNumbers.Normalise(F2SearchKey) is { } canonicalSlip)
                {
                    var existing = await _weighments.GetBySlipNumberAsync(canonicalSlip).ConfigureAwait(true);
                    if (existing is not null)
                    {
                        if (existing.Status == WeighmentStatus.Completed)
                        {
                            Show($"Ticket {canonicalSlip} has already been completed.", BadgeSeverity.Warning);
                            return;
                        }
                        if (existing.Status == WeighmentStatus.Cancelled)
                        {
                            Show($"Ticket {canonicalSlip} was cancelled.", BadgeSeverity.Danger);
                            return;
                        }
                        if (existing.Status == WeighmentStatus.Created)
                        {
                            Show($"Ticket {canonicalSlip} is in First Entry mode (awaiting first weight).", BadgeSeverity.Information);
                            return;
                        }
                    }
                }

                Show($"No pending weighment found for '{F2SearchKey}'.", BadgeSeverity.Warning);
                return;
            }

            LoadPendingTransactionIntoF2(found);
            Show($"Pending ticket {found.SlipNumber} ({found.VehicleNumber}) loaded. F1 fields locked.", BadgeSeverity.Success);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Multiple pending transactions found"))
        {
            _logger.LogWarning(ex, "Multiple pending transactions found for {Key}", F2SearchKey);
            Show(ex.Message, BadgeSeverity.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching pending second entry for {Key}", F2SearchKey);
            Show("Error searching for pending weighment.", BadgeSeverity.Danger);
        }
    }

    public Task SelectPendingTransactionAsync()
    {
        if (SelectedAwaiting is null)
        {
            return Task.CompletedTask;
        }

        return LoadPendingTransactionByIdAsync(SelectedAwaiting.Id);
    }

    public async Task LoadPendingTransactionByIdAsync(long weighmentId)
    {
        if (IsDirty)
        {
            var confirm = await _dialogs.ShowConfirmationAsync(
                "Load Transaction",
                "You have unsaved changes in the current form. Discard them and load the selected transaction?",
                confirmText: "Discard and Load",
                cancelText: "Keep Editing",
                isDestructive: true).ConfigureAwait(true);

            if (!confirm)
            {
                return;
            }
        }

        var found = await _weighments.GetAsync(weighmentId).ConfigureAwait(true);
        if (found is null || found.Status != WeighmentStatus.AwaitingSecondWeight)
        {
            Show($"Weighment {weighmentId} is no longer awaiting second weight.", BadgeSeverity.Warning);
            await RefreshAsync().ConfigureAwait(true);
            return;
        }

        LoadPendingTransactionIntoF2(found);
        Show($"Ticket {found.SlipNumber} loaded into Second Entry. F1 data locked.", BadgeSeverity.Information);
    }

    private void LoadPendingTransactionIntoF2(Weighment weighment)
    {
        ActiveReservationId = null;
        ActiveWeighmentId = weighment.Id;
        ActiveVersion = weighment.Version;
        ActiveSlipNumber = weighment.SlipNumber;
        HasConcurrencyConflict = false;

        Current = WeighmentSummary.From(weighment);

        // Populate F1 fields so they display accurately in the locked display
        SelectedVehicleTypeName = weighment.VehicleTypeName;
        VehicleNumber = weighment.VehicleNumber;
        PartyName = weighment.PartyName;
        MaterialName = weighment.MaterialName;
        DriverName = weighment.DriverName;
        TransporterName = weighment.TransporterName;
        Remarks = weighment.Remarks;
        Charges = weighment.Charges;
        CustomField1 = weighment.CustomField1;
        CustomField2 = weighment.CustomField2;

        SelectedArrivalMode = ArrivalModes.FirstOrDefault(m => m.Value == weighment.Mode) ?? ArrivalModes[0];

        if (weighment.VehicleId.HasValue)
        {
            SelectedVehicleOption = ActiveVehicles.FirstOrDefault(v => v.Id == weighment.VehicleId);
        }
        else
        {
            _selectedVehicleId = null;
            _selectedVehicleOption = null;
            OnPropertyChanged(nameof(SelectedVehicleOption));
        }

        if (weighment.PartyId.HasValue)
        {
            SelectedPartyOption = ActiveParties.FirstOrDefault(p => p.Id == weighment.PartyId);
        }
        else
        {
            _selectedPartyId = null;
            _selectedPartyOption = null;
            OnPropertyChanged(nameof(SelectedPartyOption));
        }

        if (weighment.MaterialId.HasValue)
        {
            SelectedMaterialOption = ActiveMaterials.FirstOrDefault(m => m.Id == weighment.MaterialId);
        }
        else
        {
            _selectedMaterialId = null;
            _selectedMaterialOption = null;
            OnPropertyChanged(nameof(SelectedMaterialOption));
        }

        // Pre-populate F2 fields if any were already entered
        SecondCharges = weighment.SecondCharges;
        NumberOfBags = weighment.NumberOfBags;
        BagWeightKg = weighment.BagWeightKg;
        GatePassNumber = weighment.GatePassNumber;
        F2Remarks = null; // Do not overwrite F1 remarks; F2 remarks are separate optional additions
        CustomField3 = weighment.CustomField3;
        CustomField4 = weighment.CustomField4;

        WeightInput = string.Empty;
        _grossWeightInput = string.Empty;
        _tareWeightInput = string.Empty;
        IsDirty = false;
        WorkflowState = WeighmentWorkflowState.F2Selected;
    }

    public async Task RecordSecondWeightAsync()
    {
        if (!ActiveWeighmentId.HasValue)
        {
            Show("Search and select a pending ticket before completing second weight.", BadgeSeverity.Warning);
            return;
        }

        if (!TryReadWeight(out var kilograms))
        {
            return;
        }

        var request = new RecordSecondWeightRequest(
            WeighmentId: ActiveWeighmentId.Value,
            Kilograms: kilograms,
            Source: _weightSource,
            SecondCharges: IsSecondEntryChargesEnabled ? SecondCharges : 0m,
            NumberOfBags: IsUnitBagsWeightColumnEnabled ? NumberOfBags : null,
            BagWeightKg: IsUnitBagsWeightColumnEnabled ? BagWeightKg : null,
            GatePassNumber: GatePassNumber,
            Remarks: F2Remarks,
            CustomField3: CustomField3,
            CustomField4: CustomField4,
            ExpectedVersion: ActiveVersion);

        try
        {
            var result = await _executor
                .ExecuteAsync(new RecordSecondWeightCommand(_weighments, request))
                .ConfigureAwait(true);

            Report(result);

            if (result is not { IsSuccess: true, Value: not null })
            {
                return;
            }

            var saved = result.Value;
            await CaptureCameraSnapshotAsync(saved, "SecondWeight").ConfigureAwait(true);

            Current = WeighmentSummary.From(saved);
            ResetWorkflowContext();
            WorkflowState = WeighmentWorkflowState.Completed;

            Show($"Weighment {saved.SlipNumber} completed. Net {saved.NetWeightKg:0.##} kg.", BadgeSeverity.Success);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("modified by another operator or process"))
        {
            HasConcurrencyConflict = true;
            Show(ex.Message, BadgeSeverity.Danger);
        }
    }

    public async Task ReloadActiveTransactionAsync()
    {
        if (!ActiveWeighmentId.HasValue)
        {
            return;
        }

        var refreshed = await _weighments.GetAsync(ActiveWeighmentId.Value).ConfigureAwait(true);
        if (refreshed is null)
        {
            Show("Transaction was deleted or retired.", BadgeSeverity.Danger);
            ResetWorkflowContext();
            WorkflowState = WeighmentWorkflowState.F1Entry;
            return;
        }

        ActiveVersion = refreshed.Version;
        Current = WeighmentSummary.From(refreshed);
        HasConcurrencyConflict = false;
        Show($"Transaction {refreshed.SlipNumber} reloaded with latest version. Unsaved F2 edits preserved.", BadgeSeverity.Information);
    }

    #endregion

    #region Context Submissions & Clear

    public async Task SubmitWorkflowAsync()
    {
        switch (WorkflowState)
        {
            case WeighmentWorkflowState.F1Entry:
            case WeighmentWorkflowState.TicketAllocated:
            case WeighmentWorkflowState.AwaitingFirstWeight:
                await RecordFirstWeightAsync().ConfigureAwait(true);
                break;
            case WeighmentWorkflowState.F2Entry:
                await SearchPendingSecondEntryAsync().ConfigureAwait(true);
                break;
            case WeighmentWorkflowState.F2Selected:
            case WeighmentWorkflowState.AwaitingSecondWeightCapture:
                await RecordSecondWeightAsync().ConfigureAwait(true);
                break;
            default:
                Show("Select F1 (First Entry) or F2 (Second Entry) to start a transaction.", BadgeSeverity.Information);
                break;
        }
    }

    public async Task ClearContextAsync()
    {
        if (IsDirty || ActiveWeighmentId.HasValue || ActiveReservationId.HasValue)
        {
            var confirm = await _dialogs.ShowConfirmationAsync(
                "Clear Context",
                "Clear active weighment inputs and return to resting state? (Database records will remain untouched).",
                confirmText: "Clear Form",
                cancelText: "Cancel",
                isDestructive: false).ConfigureAwait(true);

            if (!confirm)
            {
                return;
            }
        }

        if (ActiveReservationId.HasValue && !ActiveWeighmentId.HasValue)
        {
            try
            {
                await _executor
                    .ExecuteAsync(new CancelReservationCommand(_weighments, ActiveReservationId.Value, "Operator cleared form"))
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cancel reservation {Id} during ClearContext", ActiveReservationId.Value);
            }
        }

        ResetWorkflowContext();
        Current = null;
        WorkflowState = WeighmentWorkflowState.F1Entry;
        Show("Context cleared. Allocating new ticket for First Entry (F1)...", BadgeSeverity.Information);
        await EnsureReservationAsync().ConfigureAwait(true);
    }

    private void ResetWorkflowContext()
    {
        ActiveReservationId = null;
        ActiveWeighmentId = null;
        ActiveVersion = null;
        ActiveSlipNumber = null;
        HasConcurrencyConflict = false;

        // Reset F1
        VehicleNumber = string.Empty;
        PartyName = null;
        MaterialName = null;
        DriverName = null;
        TransporterName = null;
        Remarks = null;
        Charges = 0m;
        CustomField1 = null;
        CustomField2 = null;
        _selectedVehicleId = null;
        _selectedPartyId = null;
        _selectedMaterialId = null;
        _selectedVehicleTypeId = null;
        _selectedVehicleTypeName = null;
        _selectedVehicleOption = null;
        _selectedPartyOption = null;
        _selectedMaterialOption = null;
        _selectedVehicleTypeOption = null;
        StandardTareWeightKg = null;
        OnPropertyChanged(nameof(SelectedVehicleOption));
        OnPropertyChanged(nameof(SelectedPartyOption));
        OnPropertyChanged(nameof(SelectedMaterialOption));
        OnPropertyChanged(nameof(SelectedVehicleTypeOption));

        // Reset F2
        F2SearchKey = string.Empty;
        SecondCharges = 0m;
        NumberOfBags = null;
        BagWeightKg = null;
        GatePassNumber = null;
        F2Remarks = null;
        CustomField3 = null;
        CustomField4 = null;

        WeightInput = string.Empty;
        _grossWeightInput = string.Empty;
        _tareWeightInput = string.Empty;
        CancellationReason = string.Empty;
        _isAutoTareMode = false;
        _isManualTareMode = false;
        _manualTareKg = null;
        IsDirty = false;
        OnPropertyChanged(nameof(GrossTareText));
        OnPropertyChanged(nameof(IsAutoTareModeSelected));
        OnPropertyChanged(nameof(IsManualTareModeSelected));
        OnPropertyChanged(nameof(IsGrossFirstSelected));
        OnPropertyChanged(nameof(IsTareFirstSelected));
        OnPropertyChanged(nameof(IsTareWeightReadOnly));
        OnPropertyChanged(nameof(IsGrossWeightReadOnly));
        OnPropertyChanged(nameof(GrossWeightText));
        OnPropertyChanged(nameof(TareWeightText));
        OnPropertyChanged(nameof(DisplayGrossWeightKg));
        OnPropertyChanged(nameof(DisplayTareWeightKg));
        OnPropertyChanged(nameof(DisplayNetWeightKg));
    }

    #endregion

    #region Helper Methods

    private bool TryReadWeight(out decimal kilograms)
    {
        kilograms = 0m;

        // Determine which field to read from based on the current mode.
        // In F1: GrossFirst/Auto/Manual → read gross; TareFirst → read tare.
        // In F2: complementary role → read from the second weight field.
        string? text;
        if (IsF2Mode)
        {
            // F2: second weight. GrossFirst → tare is 2nd, TareFirst → gross is 2nd.
            text = Current?.Mode == WeighmentMode.GrossFirst
                ? _tareWeightInput?.Trim()
                : _grossWeightInput?.Trim();
            // Fallback to WeightInput (Capture Weight box) if dedicated field is empty
            if (string.IsNullOrWhiteSpace(text)) text = WeightInput?.Trim();
        }
        else if (SelectedArrivalMode.Value == WeighmentMode.TareFirst && !_isAutoTareMode && !_isManualTareMode)
        {
            text = _tareWeightInput?.Trim();
            if (string.IsNullOrWhiteSpace(text)) text = WeightInput?.Trim();
        }
        else
        {
            text = _grossWeightInput?.Trim();
            if (string.IsNullOrWhiteSpace(text)) text = WeightInput?.Trim();
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            Show("Enter or capture a weight first.", BadgeSeverity.Warning);
            return false;
        }

        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out kilograms) &&
            !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out kilograms))
        {
            Show($"'{text}' is not a valid weight in kilograms.", BadgeSeverity.Warning);
            return false;
        }

        if (kilograms <= 0m)
        {
            Show("Enter a weight greater than zero.", BadgeSeverity.Warning);
            return false;
        }

        return true;
    }

    public Task ReadIndicatorAsync()
    {
        if (IndicatorState != ConnectionState.Connected)
        {
            if (LiveWeightKg > 0m)
            {
                ApplyCapturedWeight(LiveWeightKg, WeightSource.Simulator);
                return Task.CompletedTask;
            }

            var dest = (SelectedArrivalMode.Value == WeighmentMode.TareFirst && !_isAutoTareMode && !_isManualTareMode)
                ? "Tare Weight"
                : "Gross Weight";
            Show($"Indicator is disconnected. Type weight into {dest} or Capture Weight box.", BadgeSeverity.Warning);
            return Task.CompletedTask;
        }

        if (IsWeightHoldEnabled && !IsWeightStable)
        {
            Show("Weight Hold is enabled. Wait for a stable indicator reading before capturing weight.", BadgeSeverity.Warning);
            return Task.CompletedTask;
        }

        var source = LiveSourceText.Contains("Simulator")
            ? WeightSource.Simulator
            : WeightSource.Indicator;

        ApplyCapturedWeight(LiveWeightKg, source);
        return Task.CompletedTask;
    }

    private void ApplyCapturedWeight(decimal weight, WeightSource source)
    {
        var formatted = weight % 1 == 0
            ? weight.ToString("0", CultureInfo.InvariantCulture)
            : weight.ToString("0.##", CultureInfo.InvariantCulture);

        _weightSource = source;

        // Route captured weight to the correct field based on the active mode
        bool isTareTarget;
        if (IsF2Mode)
        {
            // F2: second weight is complementary role
            isTareTarget = Current?.Mode == WeighmentMode.GrossFirst;
        }
        else
        {
            isTareTarget = SelectedArrivalMode.Value == WeighmentMode.TareFirst && !_isAutoTareMode && !_isManualTareMode;
        }

        if (isTareTarget)
        {
            _tareWeightInput = formatted;
            OnPropertyChanged(nameof(TareWeightText));
            OnPropertyChanged(nameof(DisplayTareWeightKg));
        }
        else
        {
            _grossWeightInput = formatted;
            OnPropertyChanged(nameof(GrossWeightText));
            OnPropertyChanged(nameof(DisplayGrossWeightKg));
        }

        // Also update WeightInput so it shows the capture in the Capture Weight box
        WeightInput = formatted;

        OnPropertyChanged(nameof(DisplayNetWeightKg));

        var targetName = isTareTarget ? "Tare Weight" : "Gross Weight";
        Show($"Captured {weight:N0} kg into {targetName} [{source}].", BadgeSeverity.Success);
    }

    private async Task CaptureCameraSnapshotAsync(Weighment weighment, string stage)
    {
        if (_cameraService.State != ConnectionState.Connected)
        {
            return;
        }

        var devices = _cameraService.ConfiguredDevices.Count > 0
            ? _cameraService.ConfiguredDevices
            : (IReadOnlyList<string>)["Camera 1"];

        foreach (var device in devices)
        {
            try
            {
                var snap = await _cameraService.CaptureSnapshotAsync(device, stage, weighment.SlipNumber).ConfigureAwait(true);
                if (snap.Success && snap.FilePath is not null)
                {
                    await _weighments.AttachImageAsync(
                        weighment.Id,
                        device,
                        stage,
                        snap.Source,
                        snap.FilePath,
                        DateTime.UtcNow,
                        snap.FileSizeBytes,
                        snap.Checksum).ConfigureAwait(true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to capture snapshot from {Device} for {SlipNumber}", device, weighment.SlipNumber);
            }
        }
    }

    public async Task CancelWeighmentAsync()
    {
        if (Current is not { IsOpen: true } current)
        {
            Show("There is no open weighment to cancel.", BadgeSeverity.Warning);
            return;
        }

        var confirmed = await _dialogs.ShowConfirmationAsync(
            "Cancel weighment",
            $"Weighment {current.SlipNumber} for {current.VehicleNumber} will be cancelled. "
            + "The record and the reason are kept for the audit.",
            confirmText: "Cancel weighment",
            cancelText: "Keep it",
            isDestructive: true).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        var result = await _executor
            .ExecuteAsync(new CancelWeighmentCommand(_weighments, current.Id, CancellationReason))
            .ConfigureAwait(true);

        Report(result);

        if (!result.IsSuccess)
        {
            return;
        }

        ResetWorkflowContext();
        Current = null;
        WorkflowState = WeighmentWorkflowState.F1Entry;
        await RefreshAsync().ConfigureAwait(true);
    }

    public async Task PrintSlipAsync()
    {
        if (Current is not { Status: WeighmentStatus.Completed } current)
        {
            Show("Only completed weighments can be printed.", BadgeSeverity.Warning);
            return;
        }

        var data = new Dictionary<string, object?>
        {
            { "SlipNumber", current.SlipNumber },
            { "VehicleNumber", current.VehicleNumber },
            { "TimeIn", current.OpenedAtLocal.ToString("g", CultureInfo.CurrentCulture) },
            { "TimeOut", current.ClosedAtLocal?.ToString("g", CultureInfo.CurrentCulture) },
            { "PartyName", current.PartyName },
            { "MaterialName", current.MaterialName },
            { "DriverName", current.DriverName },
            { "TransporterName", current.TransporterName },
            { "GrossWeightKg", current.GrossKg },
            { "TareWeightKg", current.TareKg },
            { "NetWeightKg", current.NetKg },
            { "Remarks", current.Remarks }
        };

        try
        {
            var result = await _printService.PrintAsync("GenericAscii", data).ConfigureAwait(true);
            if (result.Succeeded)
            {
                Show($"Weighment slip {current.SlipNumber} sent to printer.", BadgeSeverity.Success);
            }
            else
            {
                Show($"Printing failed: {result.Message}", BadgeSeverity.Danger);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to print slip {SlipNumber}", current.SlipNumber);
            Show("Failed to send slip to printer.", BadgeSeverity.Danger);
        }
    }

    private void MatchVehicleFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _selectedVehicleId = null;
            _selectedVehicleOption = null;
            StandardTareWeightKg = null;
            OnPropertyChanged(nameof(SelectedVehicleOption));
            return;
        }

        var normalised = Vehicle.NormaliseVehicleNumber(text);
        var match = ActiveVehicles.FirstOrDefault(v => Vehicle.NormaliseVehicleNumber(v.VehicleNumber) == normalised);
        if (match is not null)
        {
            _selectedVehicleId = match.Id;
            _selectedVehicleOption = match;
            StandardTareWeightKg = match.TareWeightKg;
            if (match.VehicleTypeId.HasValue && SelectedVehicleTypeOption is null)
            {
                SelectedVehicleTypeOption = ActiveVehicleTypes.FirstOrDefault(t => t.Id == match.VehicleTypeId);
            }
            if (_isAutoTareMode && IsAutoTareWeightEnabled)
            {
                OnPropertyChanged(nameof(DisplayTareWeightKg));
                OnPropertyChanged(nameof(DisplayNetWeightKg));
                OnPropertyChanged(nameof(EstimatedActualWeightKg));
                OnPropertyChanged(nameof(TotalMaterialAmount));
                if (match.TareWeightKg.HasValue)
                {
                    Show($"Auto Tare applied from Vehicle Master: {match.TareWeightKg.Value:N0} kg.", BadgeSeverity.Information);
                }
                else
                {
                    Show("Vehicle does not have a standard tare weight registered in Vehicle Master.", BadgeSeverity.Warning);
                }
            }
        }
        else
        {
            _selectedVehicleId = null;
            _selectedVehicleOption = null;
            StandardTareWeightKg = null;
            if (_isAutoTareMode && IsAutoTareWeightEnabled)
            {
                OnPropertyChanged(nameof(DisplayTareWeightKg));
                OnPropertyChanged(nameof(DisplayNetWeightKg));
                OnPropertyChanged(nameof(EstimatedActualWeightKg));
                OnPropertyChanged(nameof(TotalMaterialAmount));
            }
        }
        OnPropertyChanged(nameof(SelectedVehicleOption));
    }

    private void MatchPartyFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _selectedPartyId = null;
            _selectedPartyOption = null;
            OnPropertyChanged(nameof(SelectedPartyOption));
            return;
        }

        var trimmed = text.Trim();
        var match = ActiveParties.FirstOrDefault(p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            _selectedPartyId = match.Id;
            _selectedPartyOption = match;
        }
        else
        {
            _selectedPartyId = null;
            _selectedPartyOption = null;
        }
        OnPropertyChanged(nameof(SelectedPartyOption));
    }

    private void MatchMaterialFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _selectedMaterialId = null;
            _selectedMaterialOption = null;
            OnPropertyChanged(nameof(SelectedMaterialOption));
            return;
        }

        var trimmed = text.Trim();
        var match = ActiveMaterials.FirstOrDefault(m => string.Equals(m.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            _selectedMaterialId = match.Id;
            _selectedMaterialOption = match;
        }
        else
        {
            _selectedMaterialId = null;
            _selectedMaterialOption = null;
        }
        OnPropertyChanged(nameof(SelectedMaterialOption));
    }

    private void Report(CommandResult result)
    {
        var message = result.Message ?? (result.IsSuccess ? "Operation completed." : "Operation failed.");
        Show(message, result.IsSuccess ? BadgeSeverity.Success : BadgeSeverity.Danger);
    }

    private void Show(string message, BadgeSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
    }

    private void OnUnhandled(Exception ex)
    {
        _logger.LogError(ex, "Unhandled error in VehicleEntryViewModel command");
        Show($"An error occurred: {ex.Message}", BadgeSeverity.Danger);
    }

    private void RefreshCommandStates()
    {
        _switchToFirstEntry.NotifyCanExecuteChanged();
        _switchToSecondEntry.NotifyCanExecuteChanged();
        _allocateTicket.NotifyCanExecuteChanged();
        _recordFirstWeight.NotifyCanExecuteChanged();
        _recordSecondWeight.NotifyCanExecuteChanged();
        _searchPendingSecondEntry.NotifyCanExecuteChanged();
        _selectPendingTransaction.NotifyCanExecuteChanged();
        _reloadActiveTransaction.NotifyCanExecuteChanged();
        _submitWorkflow.NotifyCanExecuteChanged();
        _cancelWeighment.NotifyCanExecuteChanged();
        _readIndicator.NotifyCanExecuteChanged();
        _refresh.NotifyCanExecuteChanged();
        _clearContext.NotifyCanExecuteChanged();
        _printSlip.NotifyCanExecuteChanged();
        _selectGrossMode.NotifyCanExecuteChanged();
        _selectTareMode.NotifyCanExecuteChanged();
        _selectAutoTareMode.NotifyCanExecuteChanged();
        _selectManualTareMode.NotifyCanExecuteChanged();
    }

    private void EnsureReadyForFirstEntry()
    {
        if (WorkflowState is WeighmentWorkflowState.Idle && !ActiveWeighmentId.HasValue)
        {
            WorkflowState = WeighmentWorkflowState.F1Entry;
            Show("First Entry mode (F1) ready. Enter vehicle details.", BadgeSeverity.Information);
        }
    }

    private WeighmentOptions RuntimeOptions => _optionsMonitor?.CurrentValue ?? new WeighmentOptions();

    private void RaiseRuntimeSettingsChanged()
    {
        if (_isAutoTareMode && !IsAutoTareWeightEnabled)
        {
            GrossTareText = "G";
        }
        if (_isManualTareMode && !CanEnterManualWeight)
        {
            GrossTareText = "G";
        }

        OnPropertyChanged(nameof(CanEnterManualWeight));
        OnPropertyChanged(nameof(IsSecondEntryChargesEnabled));
        OnPropertyChanged(nameof(IsSecondChargesVisible));
        OnPropertyChanged(nameof(IsUnitBagsWeightColumnEnabled));
        OnPropertyChanged(nameof(IsOnlySingleEntryEnabled));
        OnPropertyChanged(nameof(IsAutoTareWeightEnabled));
        OnPropertyChanged(nameof(IsWeightHoldEnabled));
        OnPropertyChanged(nameof(IsGstOnChargesEnabled));
        OnPropertyChanged(nameof(GstAmount));
        OnPropertyChanged(nameof(TotalChargesWithGst));
        OnPropertyChanged(nameof(IsPriceComputingEnabled));
        OnPropertyChanged(nameof(TotalMaterialAmount));
        OnPropertyChanged(nameof(EstimatedActualWeightKg));
        OnPropertyChanged(nameof(ModeLabelText));
        OnPropertyChanged(nameof(IsTareWeightReadOnly));
        OnPropertyChanged(nameof(GrossTareText));
        OnPropertyChanged(nameof(IsAutoTareModeSelected));
        OnPropertyChanged(nameof(IsManualTareModeSelected));
        OnPropertyChanged(nameof(IsGrossFirstSelected));
        OnPropertyChanged(nameof(IsTareFirstSelected));
        RefreshCommandStates();
    }

    #endregion
}

public sealed record VehicleOption(long Id, string VehicleNumber, long? VehicleTypeId, decimal? TareWeightKg)
{
    public override string ToString() => VehicleNumber;
}

public sealed record PartyOption(long Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record MaterialOption(long Id, string Name)
{
    public override string ToString() => Name;
}
