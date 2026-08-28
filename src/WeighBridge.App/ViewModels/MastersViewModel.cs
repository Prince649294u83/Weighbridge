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
using WeighBridge.Domain.Masters;
using WeighBridge.Services.Masters;

namespace WeighBridge.App.ViewModels;

public enum MasterTab
{
    Vehicles,
    Parties,
    Materials,
    VehicleTypes,
}

public sealed class MastersViewModel : ViewModelBase
{
    private readonly ICommandExecutor _executor;
    private readonly IVehicleService _vehicleService;
    private readonly IPartyService _partyService;
    private readonly IMaterialService _materialService;
    private readonly IVehicleTypeService _vehicleTypeService;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<MastersViewModel> _logger;

    private readonly AsyncRelayCommand _refreshCommand;
    private readonly AsyncRelayCommand _searchCommand;
    private readonly RelayCommand _newItemCommand;
    private readonly AsyncRelayCommand _saveItemCommand;
    private readonly RelayCommand _cancelEditCommand;
    private readonly RelayCommand<MasterTab> _selectTabCommand;

    private MasterTab _selectedTab = MasterTab.Vehicles;
    private string _searchQuery = string.Empty;
    private bool _includeInactive = true;

    // Increments per requested load; a load whose generation is stale must not touch the collections.
    private int _loadGeneration;

    private bool _isEditing;
    private long? _editingId;
    private string _formTitle = string.Empty;
    private string _formSaveLabel = "Save";

    // Vehicle Form Fields
    private string _formVehicleNumber = string.Empty;
    private VehicleTypeOption? _formSelectedVehicleType;
    private string _formTareWeight = string.Empty;
    private string? _formVehicleRemarks;

    // Party Form Fields
    private string _formPartyName = string.Empty;
    private string? _formPartyCode;
    private string? _formPartyAddress;
    private string? _formPartyContact;
    private string? _formPartyEmail;
    private string? _formPartyRemarks;

    // Material Form Fields
    private string _formMaterialName = string.Empty;
    private string? _formMaterialCode;
    private string? _formMaterialDescription;

    // VehicleType Form Fields
    private string _formTypeName = string.Empty;
    private string? _formTypeDescription;

    private string _statusMessage = string.Empty;
    private BadgeSeverity _statusSeverity = BadgeSeverity.Neutral;

    public MastersViewModel(
        ICommandExecutor executor,
        IVehicleService vehicleService,
        IPartyService partyService,
        IMaterialService materialService,
        IVehicleTypeService vehicleTypeService,
        IPermissionService permissions,
        IDialogService dialogs,
        IUiDispatcher dispatcher,
        ILogger<MastersViewModel> logger)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _vehicleService = vehicleService ?? throw new ArgumentNullException(nameof(vehicleService));
        _partyService = partyService ?? throw new ArgumentNullException(nameof(partyService));
        _materialService = materialService ?? throw new ArgumentNullException(nameof(materialService));
        _vehicleTypeService = vehicleTypeService ?? throw new ArgumentNullException(nameof(vehicleTypeService));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Title = "Master Data";
        Description = "Maintain master records for vehicles, parties, materials and vehicle types.";

        _refreshCommand = new AsyncRelayCommand(LoadCurrentTabAsync, () => !IsBusy, OnUnhandled);
        _searchCommand = new AsyncRelayCommand(LoadCurrentTabAsync, () => !IsBusy, OnUnhandled);
        _newItemCommand = new RelayCommand(OpenNewItemForm, () => CanEdit && !IsBusy);
        _saveItemCommand = new AsyncRelayCommand(SaveItemAsync, () => CanEdit && !IsBusy, OnUnhandled);
        _cancelEditCommand = new RelayCommand(CancelEdit);
        _selectTabCommand = new RelayCommand<MasterTab>(tab => SelectedTab = tab);

        EditVehicleCommand = new RelayCommand<VehicleMasterItem>(OpenEditVehicle, _ => CanEdit && !IsBusy);
        EditPartyCommand = new RelayCommand<PartyMasterItem>(OpenEditParty, _ => CanEdit && !IsBusy);
        EditMaterialCommand = new RelayCommand<MaterialMasterItem>(OpenEditMaterial, _ => CanEdit && !IsBusy);
        EditVehicleTypeCommand = new RelayCommand<VehicleTypeMasterItem>(OpenEditVehicleType, _ => CanEdit && !IsBusy);
        ToggleActiveStateCommand = new AsyncRelayCommand<object>(ToggleActiveStateAsync, _ => CanDelete && !IsBusy, OnUnhandled);

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(IsBusy) or nameof(IsEditing))
            {
                RefreshCommandStates();
            }
        };
    }

    public ObservableCollection<VehicleMasterItem> Vehicles { get; } = [];
    public ObservableCollection<PartyMasterItem> Parties { get; } = [];
    public ObservableCollection<MaterialMasterItem> Materials { get; } = [];
    public ObservableCollection<VehicleTypeMasterItem> VehicleTypes { get; } = [];
    public ObservableCollection<VehicleTypeOption> AvailableVehicleTypes { get; } = [];

    public MasterTab SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value))
            {
                OnPropertyChanged(nameof(IsVehiclesTab));
                OnPropertyChanged(nameof(IsPartiesTab));
                OnPropertyChanged(nameof(IsMaterialsTab));
                OnPropertyChanged(nameof(IsVehicleTypesTab));
                OnPropertyChanged(nameof(TabTitle));
                CancelEdit();
                TriggerLoad();
            }
        }
    }

    public bool IsVehiclesTab => SelectedTab == MasterTab.Vehicles;
    public bool IsPartiesTab => SelectedTab == MasterTab.Parties;
    public bool IsMaterialsTab => SelectedTab == MasterTab.Materials;
    public bool IsVehicleTypesTab => SelectedTab == MasterTab.VehicleTypes;

    public string TabTitle => SelectedTab switch
    {
        MasterTab.Vehicles => "Vehicles",
        MasterTab.Parties => "Parties",
        MasterTab.Materials => "Materials",
        MasterTab.VehicleTypes => "Vehicle Types",
        _ => "Masters",
    };

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                TriggerLoad(debounceMilliseconds: 300);
            }
        }
    }

    public bool IncludeInactive
    {
        get => _includeInactive;
        set
        {
            if (SetProperty(ref _includeInactive, value))
            {
                TriggerLoad();
            }
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        private set => SetProperty(ref _isEditing, value);
    }

    public string FormTitle
    {
        get => _formTitle;
        private set => SetProperty(ref _formTitle, value);
    }

    public string FormSaveLabel
    {
        get => _formSaveLabel;
        private set => SetProperty(ref _formSaveLabel, value);
    }

    #region Vehicle Form Properties

    public string FormVehicleNumber
    {
        get => _formVehicleNumber;
        set => SetProperty(ref _formVehicleNumber, value);
    }

    public VehicleTypeOption? FormSelectedVehicleType
    {
        get => _formSelectedVehicleType;
        set => SetProperty(ref _formSelectedVehicleType, value);
    }

    public string FormTareWeight
    {
        get => _formTareWeight;
        set => SetProperty(ref _formTareWeight, value);
    }

    public string? FormVehicleRemarks
    {
        get => _formVehicleRemarks;
        set => SetProperty(ref _formVehicleRemarks, value);
    }

    #endregion

    #region Party Form Properties

    public string FormPartyName
    {
        get => _formPartyName;
        set => SetProperty(ref _formPartyName, value);
    }

    public string? FormPartyCode
    {
        get => _formPartyCode;
        set => SetProperty(ref _formPartyCode, value);
    }

    public string? FormPartyAddress
    {
        get => _formPartyAddress;
        set => SetProperty(ref _formPartyAddress, value);
    }

    public string? FormPartyContact
    {
        get => _formPartyContact;
        set => SetProperty(ref _formPartyContact, value);
    }

    public string? FormPartyEmail
    {
        get => _formPartyEmail;
        set => SetProperty(ref _formPartyEmail, value);
    }

    public string? FormPartyRemarks
    {
        get => _formPartyRemarks;
        set => SetProperty(ref _formPartyRemarks, value);
    }

    #endregion

    #region Material Form Properties

    public string FormMaterialName
    {
        get => _formMaterialName;
        set => SetProperty(ref _formMaterialName, value);
    }

    public string? FormMaterialCode
    {
        get => _formMaterialCode;
        set => SetProperty(ref _formMaterialCode, value);
    }

    public string? FormMaterialDescription
    {
        get => _formMaterialDescription;
        set => SetProperty(ref _formMaterialDescription, value);
    }

    #endregion

    #region VehicleType Form Properties

    public string FormTypeName
    {
        get => _formTypeName;
        set => SetProperty(ref _formTypeName, value);
    }

    public string? FormTypeDescription
    {
        get => _formTypeDescription;
        set => SetProperty(ref _formTypeDescription, value);
    }

    #endregion

    #region Status

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

    #endregion

    #region Permissions

    public bool CanEdit => _permissions.HasPermission(Permissions.MastersEdit);
    public bool CanDelete => _permissions.HasPermission(Permissions.MastersDelete);

    #endregion

    public ICommand RefreshCommand => _refreshCommand;
    public ICommand SearchCommand => _searchCommand;
    public ICommand NewItemCommand => _newItemCommand;
    public ICommand SaveItemCommand => _saveItemCommand;
    public ICommand CancelEditCommand => _cancelEditCommand;
    public ICommand SelectTabCommand => _selectTabCommand;
    public ICommand EditVehicleCommand { get; }
    public ICommand EditPartyCommand { get; }
    public ICommand EditMaterialCommand { get; }
    public ICommand EditVehicleTypeCommand { get; }
    public ICommand ToggleActiveStateCommand { get; }

    public override async Task OnNavigatedToAsync(NavigationContext context)
    {
        await LoadVehicleTypesLookupAsync().ConfigureAwait(true);
        await LoadCurrentTabAsync().ConfigureAwait(true);
    }

    public Task LoadCurrentTabAsync()
    {
        // Public entry point (refresh/search buttons, navigation): a new generation
        // supersedes anything already in flight.
        TriggerLoad();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts a load, superseding any load already running. Rapid typing or tab switches
    /// used to fire overlapping database reads whose Clear/Add fills raced on the same
    /// observable collections; now only the most recently requested generation is allowed
    /// to touch them.
    /// </summary>
    private async void TriggerLoad(int debounceMilliseconds = 0)
    {
        var generation = ++_loadGeneration;

        if (debounceMilliseconds > 0)
        {
            try
            {
                await Task.Delay(debounceMilliseconds).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (generation != _loadGeneration)
            {
                return;
            }
        }

        await RunBusyAsync(async () =>
        {
            switch (SelectedTab)
            {
                case MasterTab.Vehicles:
                    await LoadVehiclesAsync(generation).ConfigureAwait(true);
                    break;
                case MasterTab.Parties:
                    await LoadPartiesAsync(generation).ConfigureAwait(true);
                    break;
                case MasterTab.Materials:
                    await LoadMaterialsAsync(generation).ConfigureAwait(true);
                    break;
                case MasterTab.VehicleTypes:
                    await LoadVehicleTypesAsync(generation).ConfigureAwait(true);
                    break;
            }
        }, $"Loading {TabTitle.ToLowerInvariant()}â€¦").ConfigureAwait(true);
    }

    private async Task LoadVehicleTypesLookupAsync()
    {
        var types = await _vehicleTypeService.GetAllAsync(includeInactive: false).ConfigureAwait(true);
        AvailableVehicleTypes.Clear();
        AvailableVehicleTypes.Add(new VehicleTypeOption(null, "â€” None â€”"));
        foreach (var t in types)
        {
            AvailableVehicleTypes.Add(new VehicleTypeOption(t.Id, t.TypeName));
        }
    }

    private async Task LoadVehiclesAsync(int generation)
    {
        var items = await _vehicleService.SearchAsync(SearchQuery, IncludeInactive).ConfigureAwait(true);
        if (generation != _loadGeneration) return;
        var types = (await _vehicleTypeService.GetAllAsync(includeInactive: true).ConfigureAwait(true))
            .ToDictionary(t => t.Id, t => t.TypeName);

        Vehicles.Clear();
        foreach (var v in items)
        {
            var typeName = v.VehicleTypeId.HasValue && types.TryGetValue(v.VehicleTypeId.Value, out var tn)
                ? tn
                : "â€”";

            Vehicles.Add(new VehicleMasterItem(
                v.Id,
                v.VehicleNumber,
                v.VehicleTypeId,
                typeName,
                v.TareWeightKg,
                v.TareWeightKg.HasValue ? $"{v.TareWeightKg.Value:N1} kg" : "â€”",
                v.Remarks ?? "â€”",
                v.IsActive,
                v.IsActive ? BadgeSeverity.Success : BadgeSeverity.Neutral,
                v.IsActive ? "Active" : "Inactive",
                v.CreatedAtUtc.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.CurrentCulture)));
        }
    }

    private async Task LoadPartiesAsync(int generation)
    {
        var items = await _partyService.SearchAsync(SearchQuery, IncludeInactive).ConfigureAwait(true);
        if (generation != _loadGeneration) return;
        Parties.Clear();
        foreach (var p in items)
        {
            Parties.Add(new PartyMasterItem(
                p.Id,
                p.Name,
                p.Code ?? "â€”",
                p.Address ?? "â€”",
                p.ContactNumber ?? "â€”",
                p.Email ?? "â€”",
                p.Remarks ?? "â€”",
                p.IsActive,
                p.IsActive ? BadgeSeverity.Success : BadgeSeverity.Neutral,
                p.IsActive ? "Active" : "Inactive",
                p.CreatedAtUtc.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.CurrentCulture)));
        }
    }

    private async Task LoadMaterialsAsync(int generation)
    {
        var items = await _materialService.SearchAsync(SearchQuery, IncludeInactive).ConfigureAwait(true);
        if (generation != _loadGeneration) return;
        Materials.Clear();
        foreach (var m in items)
        {
            Materials.Add(new MaterialMasterItem(
                m.Id,
                m.Name,
                m.Code ?? "â€”",
                m.Description ?? "â€”",
                m.IsActive,
                m.IsActive ? BadgeSeverity.Success : BadgeSeverity.Neutral,
                m.IsActive ? "Active" : "Inactive",
                m.CreatedAtUtc.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.CurrentCulture)));
        }
    }

    private async Task LoadVehicleTypesAsync(int generation)
    {
        var items = await _vehicleTypeService.SearchAsync(SearchQuery, IncludeInactive).ConfigureAwait(true);
        if (generation != _loadGeneration) return;
        VehicleTypes.Clear();
        foreach (var vt in items)
        {
            VehicleTypes.Add(new VehicleTypeMasterItem(
                vt.Id,
                vt.TypeName,
                vt.Description ?? "â€”",
                vt.IsActive,
                vt.IsActive ? BadgeSeverity.Success : BadgeSeverity.Neutral,
                vt.IsActive ? "Active" : "Inactive",
                vt.CreatedAtUtc.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.CurrentCulture)));
        }
    }

    public void OpenNewItemForm()
    {
        _editingId = null;
        FormTitle = $"New {TabTitle.TrimEnd('s')}";
        FormSaveLabel = "Create";

        FormVehicleNumber = string.Empty;
        FormSelectedVehicleType = AvailableVehicleTypes.FirstOrDefault();
        FormTareWeight = string.Empty;
        FormVehicleRemarks = null;

        FormPartyName = string.Empty;
        FormPartyCode = null;
        FormPartyAddress = null;
        FormPartyContact = null;
        FormPartyEmail = null;
        FormPartyRemarks = null;

        FormMaterialName = string.Empty;
        FormMaterialCode = null;
        FormMaterialDescription = null;

        FormTypeName = string.Empty;
        FormTypeDescription = null;

        IsEditing = true;
    }

    public void OpenEditVehicle(VehicleMasterItem? item)
    {
        if (item is null) return;
        _editingId = item.Id;
        FormTitle = $"Edit Vehicle ({item.VehicleNumber})";
        FormSaveLabel = "Save changes";

        FormVehicleNumber = item.VehicleNumber;
        FormSelectedVehicleType = AvailableVehicleTypes.FirstOrDefault(opt => opt.Id == item.VehicleTypeId)
            ?? AvailableVehicleTypes.FirstOrDefault();
        FormTareWeight = item.TareWeightKg?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;
        FormVehicleRemarks = item.Remarks == "â€”" ? null : item.Remarks;

        IsEditing = true;
    }

    public void OpenEditParty(PartyMasterItem? item)
    {
        if (item is null) return;
        _editingId = item.Id;
        FormTitle = $"Edit Party ({item.Name})";
        FormSaveLabel = "Save changes";

        FormPartyName = item.Name;
        FormPartyCode = item.Code == "â€”" ? null : item.Code;
        FormPartyAddress = item.Address == "â€”" ? null : item.Address;
        FormPartyContact = item.ContactNumber == "â€”" ? null : item.ContactNumber;
        FormPartyEmail = item.Email == "â€”" ? null : item.Email;
        FormPartyRemarks = item.Remarks == "â€”" ? null : item.Remarks;

        IsEditing = true;
    }

    public void OpenEditMaterial(MaterialMasterItem? item)
    {
        if (item is null) return;
        _editingId = item.Id;
        FormTitle = $"Edit Material ({item.Name})";
        FormSaveLabel = "Save changes";

        FormMaterialName = item.Name;
        FormMaterialCode = item.Code == "â€”" ? null : item.Code;
        FormMaterialDescription = item.Description == "â€”" ? null : item.Description;

        IsEditing = true;
    }

    public void OpenEditVehicleType(VehicleTypeMasterItem? item)
    {
        if (item is null) return;
        _editingId = item.Id;
        FormTitle = $"Edit Vehicle Type ({item.TypeName})";
        FormSaveLabel = "Save changes";

        FormTypeName = item.TypeName;
        FormTypeDescription = item.Description == "â€”" ? null : item.Description;

        IsEditing = true;
    }

    public void CancelEdit()
    {
        IsEditing = false;
        _editingId = null;
    }

    private async Task SaveItemAsync()
    {
        CommandResult result;

        switch (SelectedTab)
        {
            case MasterTab.Vehicles:
                decimal? tare = null;
                if (!string.IsNullOrWhiteSpace(FormTareWeight))
                {
                    if (decimal.TryParse(FormTareWeight, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsedTare))
                    {
                        tare = parsedTare;
                    }
                    else
                    {
                        Show("Enter a valid numeric tare weight in kilograms.", BadgeSeverity.Warning);
                        return;
                    }
                }

                if (_editingId.HasValue)
                {
                    var req = new UpdateVehicleRequest(
                        _editingId.Value,
                        FormVehicleNumber,
                        FormSelectedVehicleType?.Id,
                        tare,
                        FormVehicleRemarks);
                    result = await _executor.ExecuteAsync(new UpdateVehicleCommand(_vehicleService, req)).ConfigureAwait(true);
                }
                else
                {
                    var req = new CreateVehicleRequest(
                        FormVehicleNumber,
                        FormSelectedVehicleType?.Id,
                        tare,
                        FormVehicleRemarks);
                    result = await _executor.ExecuteAsync(new CreateVehicleCommand(_vehicleService, req)).ConfigureAwait(true);
                }
                break;

            case MasterTab.Parties:
                if (_editingId.HasValue)
                {
                    var req = new UpdatePartyRequest(
                        _editingId.Value,
                        FormPartyName,
                        FormPartyCode,
                        FormPartyAddress,
                        FormPartyContact,
                        FormPartyEmail,
                        FormPartyRemarks);
                    result = await _executor.ExecuteAsync(new UpdatePartyCommand(_partyService, req)).ConfigureAwait(true);
                }
                else
                {
                    var req = new CreatePartyRequest(
                        FormPartyName,
                        FormPartyCode,
                        FormPartyAddress,
                        FormPartyContact,
                        FormPartyEmail,
                        FormPartyRemarks);
                    result = await _executor.ExecuteAsync(new CreatePartyCommand(_partyService, req)).ConfigureAwait(true);
                }
                break;

            case MasterTab.Materials:
                if (_editingId.HasValue)
                {
                    var req = new UpdateMaterialRequest(
                        _editingId.Value,
                        FormMaterialName,
                        FormMaterialCode,
                        FormMaterialDescription);
                    result = await _executor.ExecuteAsync(new UpdateMaterialCommand(_materialService, req)).ConfigureAwait(true);
                }
                else
                {
                    var req = new CreateMaterialRequest(
                        FormMaterialName,
                        FormMaterialCode,
                        FormMaterialDescription);
                    result = await _executor.ExecuteAsync(new CreateMaterialCommand(_materialService, req)).ConfigureAwait(true);
                }
                break;

            case MasterTab.VehicleTypes:
                if (_editingId.HasValue)
                {
                    var req = new UpdateVehicleTypeRequest(
                        _editingId.Value,
                        FormTypeName,
                        FormTypeDescription);
                    result = await _executor.ExecuteAsync(new UpdateVehicleTypeCommand(_vehicleTypeService, req)).ConfigureAwait(true);
                }
                else
                {
                    var req = new CreateVehicleTypeRequest(
                        FormTypeName,
                        FormTypeDescription);
                    result = await _executor.ExecuteAsync(new CreateVehicleTypeCommand(_vehicleTypeService, req)).ConfigureAwait(true);
                }
                break;

            default:
                return;
        }

        Report(result);

        if (result.IsSuccess)
        {
            IsEditing = false;
            _editingId = null;
            if (SelectedTab == MasterTab.VehicleTypes)
            {
                await LoadVehicleTypesLookupAsync().ConfigureAwait(true);
            }
            await LoadCurrentTabAsync().ConfigureAwait(true);
        }
    }

    public async Task ToggleActiveStateAsync(object? item)
    {
        if (item is null) return;
        if (item is VehicleMasterItem vehicle)
        {
            if (vehicle.IsActive)
            {
                var confirm = await _dialogs.ShowConfirmationAsync(
                    "Deactivate Vehicle",
                    $"Deactivate vehicle {vehicle.VehicleNumber}? It will no longer appear in new weighment selections.",
                    confirmText: "Deactivate",
                    isDestructive: true).ConfigureAwait(true);

                if (confirm)
                {
                    var result = await _executor.ExecuteAsync(new DeactivateVehicleCommand(_vehicleService, vehicle.Id)).ConfigureAwait(true);
                    Report(result);
                    await LoadVehiclesAsync(_loadGeneration).ConfigureAwait(true);
                }
            }
            else
            {
                var result = await _executor.ExecuteAsync(new ReactivateVehicleCommand(_vehicleService, vehicle.Id)).ConfigureAwait(true);
                Report(result);
                await LoadVehiclesAsync(_loadGeneration).ConfigureAwait(true);
            }
        }
        else if (item is PartyMasterItem party)
        {
            if (party.IsActive)
            {
                var confirm = await _dialogs.ShowConfirmationAsync(
                    "Deactivate Party",
                    $"Deactivate party '{party.Name}'? It will no longer appear in new weighment selections.",
                    confirmText: "Deactivate",
                    isDestructive: true).ConfigureAwait(true);

                if (confirm)
                {
                    var result = await _executor.ExecuteAsync(new DeactivatePartyCommand(_partyService, party.Id)).ConfigureAwait(true);
                    Report(result);
                    await LoadPartiesAsync(_loadGeneration).ConfigureAwait(true);
                }
            }
            else
            {
                var result = await _executor.ExecuteAsync(new ReactivatePartyCommand(_partyService, party.Id)).ConfigureAwait(true);
                Report(result);
                await LoadPartiesAsync(_loadGeneration).ConfigureAwait(true);
            }
        }
        else if (item is MaterialMasterItem material)
        {
            if (material.IsActive)
            {
                var confirm = await _dialogs.ShowConfirmationAsync(
                    "Deactivate Material",
                    $"Deactivate material '{material.Name}'? It will no longer appear in new weighment selections.",
                    confirmText: "Deactivate",
                    isDestructive: true).ConfigureAwait(true);

                if (confirm)
                {
                    var result = await _executor.ExecuteAsync(new DeactivateMaterialCommand(_materialService, material.Id)).ConfigureAwait(true);
                    Report(result);
                    await LoadMaterialsAsync(_loadGeneration).ConfigureAwait(true);
                }
            }
            else
            {
                var result = await _executor.ExecuteAsync(new ReactivateMaterialCommand(_materialService, material.Id)).ConfigureAwait(true);
                Report(result);
                await LoadMaterialsAsync(_loadGeneration).ConfigureAwait(true);
            }
        }
        else if (item is VehicleTypeMasterItem vehicleType)
        {
            if (vehicleType.IsActive)
            {
                var confirm = await _dialogs.ShowConfirmationAsync(
                    "Deactivate Vehicle Type",
                    $"Deactivate vehicle type '{vehicleType.TypeName}'? It will no longer appear in new weighment selections.",
                    confirmText: "Deactivate",
                    isDestructive: true).ConfigureAwait(true);

                if (confirm)
                {
                    var result = await _executor.ExecuteAsync(new DeactivateVehicleTypeCommand(_vehicleTypeService, vehicleType.Id)).ConfigureAwait(true);
                    Report(result);
                    await LoadVehicleTypesLookupAsync().ConfigureAwait(true);
                    await LoadVehicleTypesAsync(_loadGeneration).ConfigureAwait(true);
                }
            }
            else
            {
                var result = await _executor.ExecuteAsync(new ReactivateVehicleTypeCommand(_vehicleTypeService, vehicleType.Id)).ConfigureAwait(true);
                Report(result);
                await LoadVehicleTypesLookupAsync().ConfigureAwait(true);
                await LoadVehicleTypesAsync(_loadGeneration).ConfigureAwait(true);
            }
        }
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
            _logger.LogError(error, "Masters operation failed.");
        }
    }

    private void Show(string message, BadgeSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
    }

    private void OnUnhandled(Exception error)
    {
        _logger.LogError(error, "Masters unhandled action failure.");
        Show("Something went wrong on the masters screen. Details are in the log.", BadgeSeverity.Danger);
    }

    private void RefreshCommandStates()
    {
        _refreshCommand.NotifyCanExecuteChanged();
        _searchCommand.NotifyCanExecuteChanged();
        _newItemCommand.NotifyCanExecuteChanged();
        _saveItemCommand.NotifyCanExecuteChanged();
    }
}

#region Master Item Records

public sealed record VehicleTypeOption(long? Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record VehicleMasterItem(
    long Id,
    string VehicleNumber,
    long? VehicleTypeId,
    string VehicleTypeName,
    decimal? TareWeightKg,
    string TareWeightText,
    string Remarks,
    bool IsActive,
    BadgeSeverity StatusSeverity,
    string StatusText,
    string CreatedAtText);

public sealed record PartyMasterItem(
    long Id,
    string Name,
    string Code,
    string Address,
    string ContactNumber,
    string Email,
    string Remarks,
    bool IsActive,
    BadgeSeverity StatusSeverity,
    string StatusText,
    string CreatedAtText);

public sealed record MaterialMasterItem(
    long Id,
    string Name,
    string Code,
    string Description,
    bool IsActive,
    BadgeSeverity StatusSeverity,
    string StatusText,
    string CreatedAtText);

public sealed record VehicleTypeMasterItem(
    long Id,
    string TypeName,
    string Description,
    bool IsActive,
    BadgeSeverity StatusSeverity,
    string StatusText,
    string CreatedAtText);

#endregion
