using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using WeighBridge.App.Controls;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Dialogs;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Navigation;
using WeighBridge.Core.Security;
using WeighBridge.Core.Threading;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Masters;
using WeighBridge.Domain.Weighments;
using WeighBridge.Services.Weighments;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// The weighbridge operator's screen: open a weighment, record the gross and tare weights, and close the slip.
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

    private readonly AsyncRelayCommand _openWeighment;
    private readonly AsyncRelayCommand _recordWeight;
    private readonly AsyncRelayCommand _cancelWeighment;
    private readonly AsyncRelayCommand _readIndicator;
    private readonly AsyncRelayCommand _refresh;
    private readonly RelayCommand _clearForm;
    private readonly AsyncRelayCommand _printSlip;

    private string _vehicleNumber = string.Empty;
    private WeighmentModeOption _selectedArrivalMode = ArrivalModes[0];
    private string? _partyName;
    private string? _materialName;
    private string? _driverName;
    private string? _transporterName;
    private string? _remarks;

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

    private WeighmentSummary? _current;
    private WeighmentSummary? _selectedAwaiting;
    private string _weightInput = string.Empty;
    private WeightSource _weightSource = WeightSource.Manual;
    private string _cancellationReason = string.Empty;

    private string _statusMessage = string.Empty;
    private BadgeSeverity _statusSeverity = BadgeSeverity.Neutral;
    private ConnectionState _indicatorState = ConnectionState.Unknown;

    private decimal _liveWeightKg;
    private string _liveWeightUnit = "kg";
    private bool _isWeightStable;
    private string _stabilityStatusText = "DISCONNECTED";
    private BadgeSeverity _stabilitySeverity = BadgeSeverity.Neutral;
    private string _liveSourceText = "[Manual]";
    private string _cameraStatusText = "Cameras Offline";

    /// <summary>Creates the ViewModel. Everything it needs is injected.</summary>
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
        ILogger<VehicleEntryViewModel> logger)
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

        Title = "Vehicle Entry";
        Description = "Open a weighment, record the gross and tare weights, and close the slip.";

        _openWeighment = new AsyncRelayCommand(OpenWeighmentAsync, () => !IsBusy, OnUnhandled);
        _recordWeight = new AsyncRelayCommand(
            RecordWeightAsync,
            () => !IsBusy && Current is { IsOpen: true },
            OnUnhandled);
        _cancelWeighment = new AsyncRelayCommand(
            CancelWeighmentAsync,
            () => !IsBusy && CanCancel && Current is { IsOpen: true },
            OnUnhandled);
        _readIndicator = new AsyncRelayCommand(
            ReadIndicatorAsync,
            () => !IsBusy && IndicatorState == ConnectionState.Connected,
            OnUnhandled);
        _refresh = new AsyncRelayCommand(RefreshAsync, () => !IsBusy, OnUnhandled);
        _clearForm = new RelayCommand(ClearForm);
        _printSlip = new AsyncRelayCommand(
            PrintSlipAsync,
            () => !IsBusy && Current is { Status: WeighmentStatus.Completed },
            OnUnhandled);

        PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(IsBusy))
            {
                RefreshCommandStates();
            }
        };
    }

    public ObservableCollection<VehicleOption> ActiveVehicles { get; } = [];
    public ObservableCollection<PartyOption> ActiveParties { get; } = [];
    public ObservableCollection<MaterialOption> ActiveMaterials { get; } = [];
    public ObservableCollection<VehicleTypeOption> ActiveVehicleTypes { get; } = [];

    /// <summary>Whether the vehicle arrived loaded or empty.</summary>
    public IReadOnlyList<WeighmentModeOption> ArrivalModeOptions => ArrivalModes;

    /// <summary>Registration of the vehicle being weighed.</summary>
    public string VehicleNumber
    {
        get => _vehicleNumber;
        set
        {
            if (SetProperty(ref _vehicleNumber, value))
            {
                MatchVehicleFromText(value);
            }
        }
    }

    /// <summary>Selected master vehicle option.</summary>
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

                // Auto-populate vehicle type if the vehicle has one
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

    /// <summary>Standard tare weight if known from master record.</summary>
    public decimal? StandardTareWeightKg
    {
        get => _standardTareWeightKg;
        private set => SetProperty(ref _standardTareWeightKg, value, () => OnPropertyChanged(nameof(HasStandardTareWeight)));
    }

    public bool HasStandardTareWeight => StandardTareWeightKg.HasValue;

    /// <summary>Which weight is taken first.</summary>
    public WeighmentModeOption SelectedArrivalMode
    {
        get => _selectedArrivalMode;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _selectedArrivalMode, value);
            }
        }
    }

    /// <summary>Party the load belongs to. Optional.</summary>
    public string? PartyName
    {
        get => _partyName;
        set
        {
            if (SetProperty(ref _partyName, value))
            {
                MatchPartyFromText(value);
            }
        }
    }

    /// <summary>Selected master party option.</summary>
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

    /// <summary>What is being carried. Optional.</summary>
    public string? MaterialName
    {
        get => _materialName;
        set
        {
            if (SetProperty(ref _materialName, value))
            {
                MatchMaterialFromText(value);
            }
        }
    }

    /// <summary>Selected master material option.</summary>
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

    /// <summary>Selected vehicle type.</summary>
    public VehicleTypeOption? SelectedVehicleTypeOption
    {
        get => _selectedVehicleTypeOption;
        set
        {
            if (SetProperty(ref _selectedVehicleTypeOption, value))
            {
                _selectedVehicleTypeId = value?.Id;
                _selectedVehicleTypeName = value?.Name;
            }
        }
    }

    /// <summary>Who is driving. Optional.</summary>
    public string? DriverName
    {
        get => _driverName;
        set => SetProperty(ref _driverName, value);
    }

    /// <summary>Whose lorry it is. Optional.</summary>
    public string? TransporterName
    {
        get => _transporterName;
        set => SetProperty(ref _transporterName, value);
    }

    /// <summary>Anything else worth recording. Optional.</summary>
    public string? Remarks
    {
        get => _remarks;
        set => SetProperty(ref _remarks, value);
    }

    /// <summary>The weighment the operator is working on, or <c>null</c> when there is none.</summary>
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

    /// <summary>True when a weighment is on screen.</summary>
    public bool HasCurrent => Current is not null;

    /// <summary>The weight the operator is about to record, as typed.</summary>
    public string WeightInput
    {
        get => _weightInput;
        set => SetProperty(ref _weightInput, value, () => _weightSource = WeightSource.Manual);
    }

    /// <summary>Why an open weighment is being abandoned.</summary>
    public string CancellationReason
    {
        get => _cancellationReason;
        set => SetProperty(ref _cancellationReason, value);
    }

    /// <summary>Vehicles that have been weighed once and are expected back.</summary>
    public ObservableCollection<WeighmentSummary> AwaitingSecondWeight { get; } = [];

    /// <summary>The row picked out of <see cref="AwaitingSecondWeight"/>.</summary>
    public WeighmentSummary? SelectedAwaiting
    {
        get => _selectedAwaiting;
        set
        {
            if (value is null || !SetProperty(ref _selectedAwaiting, value))
            {
                return;
            }

            Current = value;
            WeightInput = string.Empty;
        }
    }

    /// <summary>What the last operation said. Empty until something has happened.</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value, () => OnPropertyChanged(nameof(HasStatus)));
    }

    /// <summary>True once there is something to show in the status line.</summary>
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);

    /// <summary>How the status line should read.</summary>
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
        private set => SetProperty(ref _liveWeightUnit, value);
    }

    public string LiveWeightDisplay => IndicatorState == ConnectionState.Connected
        ? $"{LiveWeightKg:N0} {LiveWeightUnit}"
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

    /// <summary>What the weight indicator is doing.</summary>
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

    public bool CanCancel => _permissions.HasPermission(Permissions.WeighmentCancel);

    public ICommand OpenWeighmentCommand => _openWeighment;
    public ICommand RecordWeightCommand => _recordWeight;
    public ICommand CancelWeighmentCommand => _cancelWeighment;
    public ICommand ReadIndicatorCommand => _readIndicator;
    public ICommand RefreshCommand => _refresh;
    public ICommand ClearFormCommand => _clearForm;
    public ICommand PrintSlipCommand => _printSlip;

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
        }

        if (_cameraService.State != ConnectionState.Connected)
        {
            await _cameraService.ConnectAsync().ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
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
            LiveWeightKg = reading.Value;
            LiveWeightUnit = string.IsNullOrWhiteSpace(reading.Unit) ? "kg" : reading.Unit;
            IsWeightStable = reading.IsStable;

            if (reading.IsZero)
            {
                StabilityStatusText = "ZERO";
                StabilitySeverity = BadgeSeverity.Neutral;
            }
            else if (reading.IsNegative)
            {
                StabilityStatusText = "NEGATIVE";
                StabilitySeverity = BadgeSeverity.Danger;
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
        }
        else
        {
            _selectedVehicleId = null;
            _selectedVehicleOption = null;
            StandardTareWeightKg = null;
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

    private async Task OpenWeighmentAsync()
    {
        var request = new NewWeighment
        {
            VehicleNumber = VehicleNumber,
            Mode = SelectedArrivalMode.Value,
            PartyName = PartyName,
            MaterialName = MaterialName,
            DriverName = DriverName,
            TransporterName = TransporterName,
            Remarks = Remarks,
            VehicleId = _selectedVehicleId,
            PartyId = _selectedPartyId,
            MaterialId = _selectedMaterialId,
            VehicleTypeId = _selectedVehicleTypeId,
            VehicleTypeName = _selectedVehicleTypeName,
        };

        var canonical = Weighment.NormaliseVehicleNumber(VehicleNumber);
        if (!string.IsNullOrWhiteSpace(canonical))
        {
            var pending = await _weighments.GetAwaitingSecondWeightAsync().ConfigureAwait(true);
            var existing = pending.FirstOrDefault(w => string.Equals(w.VehicleNumber, canonical, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                var resume = await _dialogs.ShowConfirmationAsync(
                    "Existing Weighment Found",
                    $"Vehicle {VehicleNumber} already has an open transaction (Slip #{existing.SlipNumber}) awaiting second weight. Would you like to resume it now?").ConfigureAwait(true);

                if (resume)
                {
                    Current = WeighmentSummary.From(existing);
                    WeightInput = string.Empty;
                    ClearForm();
                    return;
                }
            }
        }

        var result = await _executor
            .ExecuteAsync(new CreateWeighmentCommand(_weighments, request))
            .ConfigureAwait(true);

        Report(result);

        if (result is not { IsSuccess: true, Value: not null })
        {
            return;
        }

        Current = WeighmentSummary.From(result.Value);
        WeightInput = string.Empty;
        ClearForm();
        await LoadAsync().ConfigureAwait(true);
    }

    private async Task RecordWeightAsync()
    {
        if (Current is not { IsOpen: true } current)
        {
            Show("Open a weighment, or pick a vehicle from the waiting list, before recording a weight.", BadgeSeverity.Warning);
            return;
        }

        if (!TryReadWeight(out var kilograms))
        {
            return;
        }

        var source = _weightSource;

        var result = current.Status == WeighmentStatus.Created
            ? await _executor
                .ExecuteAsync(new RecordFirstWeightCommand(_weighments, current.Id, kilograms, source))
                .ConfigureAwait(true)
            : await _executor
                .ExecuteAsync(new RecordSecondWeightCommand(_weighments, current.Id, kilograms, source))
                .ConfigureAwait(true);

        Report(result);

        if (result is not { IsSuccess: true, Value: not null })
        {
            return;
        }

        var savedWeighment = result.Value;
        var stage = current.Status == WeighmentStatus.Created ? "FirstWeight" : "SecondWeight";

        // Capture snapshot from cameras if enabled
        if (_cameraService.State == ConnectionState.Connected)
        {
            var devices = _cameraService.ConfiguredDevices.Count > 0
                ? _cameraService.ConfiguredDevices
                : (IReadOnlyList<string>)["Camera 1"];

            foreach (var device in devices)
            {
                try
                {
                    var snap = await _cameraService.CaptureSnapshotAsync(device, stage, savedWeighment.SlipNumber).ConfigureAwait(true);
                    if (snap.Success && snap.FilePath is not null)
                    {
                        await _weighments.AttachImageAsync(
                            savedWeighment.Id,
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
                    _logger.LogWarning(ex, "Failed to capture snapshot from {Device} for {SlipNumber}", device, savedWeighment.SlipNumber);
                }
            }
        }

        Current = WeighmentSummary.From(savedWeighment);
        WeightInput = string.Empty;
        await LoadAsync().ConfigureAwait(true);
    }

    private async Task CancelWeighmentAsync()
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

        Current = null;
        CancellationReason = string.Empty;
        WeightInput = string.Empty;
        await LoadAsync().ConfigureAwait(true);
    }

    private async Task PrintSlipAsync()
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

        var printResult = await _printService.PrintAsync("WeighmentSlip", data).ConfigureAwait(true);
        if (printResult.Succeeded)
        {
            Show(printResult.Message, BadgeSeverity.Success);
        }
        else
        {
            Show(printResult.Message, BadgeSeverity.Danger);
        }
    }

    private async Task ReadIndicatorAsync()
    {
        var reading = await _indicator.ReadAsync(CancellationToken.None).ConfigureAwait(true);

        if (!reading.IsStable)
        {
            Show("The indicator reading has not settled. Wait for the weight to hold steady.", BadgeSeverity.Warning);
            return;
        }

        WeightInput = reading.Value.ToString("0.##", CultureInfo.InvariantCulture);
        _weightSource = reading.Source;

        string sourceLabel = _weightSource == WeightSource.Simulator ? "simulated" : "read from the indicator";
        Show($"{reading.Value:0.##} {reading.Unit} {sourceLabel}.", BadgeSeverity.Information);
    }

    private async Task RefreshAsync()
    {
        await RunBusyAsync(async () =>
        {
            await LoadMastersAsync().ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }, "Loading weighments and master data…").ConfigureAwait(true);
    }

    private async Task LoadMastersAsync()
    {
        var types = await _vehicleTypeService.GetAllAsync(includeInactive: false).ConfigureAwait(true);
        ActiveVehicleTypes.Clear();
        foreach (var t in types)
        {
            ActiveVehicleTypes.Add(new VehicleTypeOption(t.Id, t.TypeName));
        }

        var typeMap = types.ToDictionary(t => t.Id, t => t.TypeName);

        var vehicles = await _vehicleService.GetAllAsync(includeInactive: false).ConfigureAwait(true);
        ActiveVehicles.Clear();
        foreach (var v in vehicles)
        {
            var typeName = v.VehicleTypeId.HasValue && typeMap.TryGetValue(v.VehicleTypeId.Value, out var tn) ? tn : null;
            ActiveVehicles.Add(new VehicleOption(v.Id, v.VehicleNumber, v.VehicleTypeId, typeName, v.TareWeightKg));
        }

        var parties = await _partyService.GetAllAsync(includeInactive: false).ConfigureAwait(true);
        ActiveParties.Clear();
        foreach (var p in parties)
        {
            ActiveParties.Add(new PartyOption(p.Id, p.Name, p.Code));
        }

        var materials = await _materialService.GetAllAsync(includeInactive: false).ConfigureAwait(true);
        ActiveMaterials.Clear();
        foreach (var m in materials)
        {
            ActiveMaterials.Add(new MaterialOption(m.Id, m.Name, m.Code));
        }
    }

    private async Task LoadAsync()
    {
        var awaiting = await _weighments.GetAwaitingSecondWeightAsync().ConfigureAwait(true);

        Fill(AwaitingSecondWeight, awaiting);

        var reselected = _selectedAwaiting is null
            ? null
            : AwaitingSecondWeight.FirstOrDefault(item => item.Id == _selectedAwaiting.Id);

        if (!Equals(_selectedAwaiting, reselected))
        {
            _selectedAwaiting = reselected;
            OnPropertyChanged(nameof(SelectedAwaiting));
        }
    }

    private bool TryReadWeight(out decimal kilograms)
    {
        // The operator types in their own locale; the indicator path writes in invariant
        // culture regardless of locale. Accepting both here is what keeps "12,5" and
        // "12.5" meaning the same truck weight instead of one of them failing.
        if (decimal.TryParse(WeightInput, NumberStyles.Number, CultureInfo.CurrentCulture, out kilograms) ||
            decimal.TryParse(WeightInput, NumberStyles.Number, CultureInfo.InvariantCulture, out kilograms))
        {
            return true;
        }

        Show("Enter the weight in kilograms, for example 15250.5.", BadgeSeverity.Warning);
        return false;
    }

    private void ClearForm()
    {
        VehicleNumber = string.Empty;
        SelectedVehicleOption = null;
        SelectedArrivalMode = ArrivalModes[0];
        PartyName = null;
        SelectedPartyOption = null;
        MaterialName = null;
        SelectedMaterialOption = null;
        SelectedVehicleTypeOption = null;
        StandardTareWeightKg = null;
        DriverName = null;
        TransporterName = null;
        Remarks = null;
    }

    private void Report(CommandResult result)
    {
        var severity = result.Outcome switch
        {
            CommandOutcome.Succeeded => BadgeSeverity.Success,
            CommandOutcome.ValidationFailed => BadgeSeverity.Warning,
            CommandOutcome.Cancelled => BadgeSeverity.Neutral,
            _ => BadgeSeverity.Danger,
        };

        var message = result.Validation is { } validation && validation.Blocking.Count() > 1
            ? validation.ToSummary()
            : result.Message ?? result.Outcome.ToString();

        Show(message, severity);

        if (result.Outcome == CommandOutcome.Failed && result.Error is { } error)
        {
            _logger.LogError(error, "Vehicle Entry operation failed.");
        }
    }

    private void Show(string message, BadgeSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
    }

    private void OnUnhandled(Exception error)
    {
        _logger.LogError(error, "Vehicle Entry could not complete an action.");
        Show("Something went wrong on this screen. The log has the detail.", BadgeSeverity.Danger);
    }

    private void RefreshCommandStates()
    {
        _openWeighment.NotifyCanExecuteChanged();
        _recordWeight.NotifyCanExecuteChanged();
        _cancelWeighment.NotifyCanExecuteChanged();
        _readIndicator.NotifyCanExecuteChanged();
        _refresh.NotifyCanExecuteChanged();
        _printSlip.NotifyCanExecuteChanged();
    }

    private static void Fill(ObservableCollection<WeighmentSummary> target, IEnumerable<Weighment> source)
    {
        target.Clear();

        foreach (var weighment in source)
        {
            target.Add(WeighmentSummary.From(weighment));
        }
    }
}

#region Master Lookup Records

public sealed record VehicleOption(long Id, string VehicleNumber, long? VehicleTypeId, string? VehicleTypeName, decimal? TareWeightKg)
{
    public override string ToString() => VehicleNumber;
}

public sealed record PartyOption(long Id, string Name, string? Code)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Code) ? Name : $"{Name} ({Code})";
}

public sealed record MaterialOption(long Id, string Name, string? Code)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Code) ? Name : $"{Name} ({Code})";
}

#endregion
