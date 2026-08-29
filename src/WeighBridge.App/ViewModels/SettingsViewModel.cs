using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Dialogs;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Navigation;
using WeighBridge.Core.Security;
using WeighBridge.Core.Settings;
using WeighBridge.Core.Theming;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// The Settings screen: operator preferences, and the hardware, printing, camera and
/// reporting configuration.
/// </summary>
/// <remarks>
/// <para>
/// Every hardware field here is editable and persisted. The screen previously bound the
/// same values as read-only text, which told an operator whose indicator had moved to a
/// different COM port to go and hand-edit a JSON file under <c>%LOCALAPPDATA%</c>.
/// </para>
/// <para>
/// Writes go through <see cref="IConfigurationWriter"/>, never straight to the file:
/// the view model does not know that configuration is JSON, and the secret keys in the
/// same document are protected on the way through.
/// </para>
/// </remarks>
public sealed class SettingsViewModel : ViewModelBase
{
    /// <summary>Rates offered in the drop-down. The scanner probes the same set.</summary>
    private static readonly int[] BaudRateChoices = [2400, 4800, 9600, 19200, 38400, 57600, 115200];

    private static readonly string[] DriverTypeChoices = ["Serial", "Simulator", "Disabled"];
    private static readonly string[] ParityChoices = ["None", "Odd", "Even", "Mark", "Space"];
    private static readonly string[] StopBitsChoices = ["One", "OnePointFive", "Two"];

    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly IDialogService _dialogService;
    private readonly IConfigurationWriter _configurationWriter;
    private readonly IIndicatorPortScanner _portScanner;
    private readonly IWeightIndicatorService _indicator;
    private readonly IPermissionService _permissions;
    private readonly ILogger<SettingsViewModel> _logger;

    private readonly AsyncRelayCommand _saveHardware;
    private readonly AsyncRelayCommand _detectPort;

    private AppTheme _selectedTheme;
    private bool _isNavigationCollapsed;

    // Edit buffers. Bound to the screen so a half-finished edit cannot reach the live
    // options object, and so Discard has something to revert to.
    private bool _indicatorEnabled;
    private string _driverType = "Serial";
    private string _portName = "COM1";
    private int _baudRate = 9600;
    private int _dataBits = 8;
    private string _parity = "None";
    private string _stopBits = "One";
    private int _stabilitySampleCount;
    private decimal _stabilityToleranceKg;
    private int _stabilityDurationMs;
    private bool _autoReconnect;
    private int _reconnectIntervalMs;
    private bool _dtrEnable = true;
    private bool _rtsEnable = true;
    private string _handshake = "None";

    private bool _cameraEnabled;
    private bool _captureOnWeighment;
    private bool _printingEnabled;
    private string _defaultPrinterName = string.Empty;
    private int _copyCount = 1;
    private string _reportOutputDirectory = string.Empty;
    private int _maxRowsPerReport;

    private string? _detectionStatus;
    private bool _isDetecting;

    public SettingsViewModel(
        ISettingsService settingsService,
        IThemeService themeService,
        IDialogService dialogService,
        IConfigurationWriter configurationWriter,
        IIndicatorPortScanner portScanner,
        IWeightIndicatorService indicator,
        IPermissionService permissions,
        IOptions<HardwareOptions> hardwareOptions,
        IOptions<PrinterOptions> printerOptions,
        IOptions<CameraOptions> cameraOptions,
        IOptions<ReportingOptions> reportingOptions,
        IOptions<DatabaseOptions> databaseOptions,
        ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _configurationWriter = configurationWriter ?? throw new ArgumentNullException(nameof(configurationWriter));
        _portScanner = portScanner ?? throw new ArgumentNullException(nameof(portScanner));
        _indicator = indicator ?? throw new ArgumentNullException(nameof(indicator));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Hardware = hardwareOptions?.Value ?? new HardwareOptions();
        Printer = printerOptions?.Value ?? new PrinterOptions();
        Camera = cameraOptions?.Value ?? new CameraOptions();
        Reporting = reportingOptions?.Value ?? new ReportingOptions();
        Database = databaseOptions?.Value ?? new DatabaseOptions();

        Title = "Settings";
        Description = "Operator preferences, and the weight indicator, printing, camera and reporting configuration.";

        _selectedTheme = _settingsService.Preferences.Theme;
        _isNavigationCollapsed = _settingsService.Preferences.IsNavigationCollapsed;

        LoadFromOptions();

        SavePreferencesCommand = new AsyncRelayCommand(SavePreferencesAsync);
        ResetPreferencesCommand = new AsyncRelayCommand(ResetPreferencesAsync);

        _saveHardware = new AsyncRelayCommand(SaveConfigurationAsync, () => CanEditConfiguration && !IsDetecting);
        _detectPort = new AsyncRelayCommand(DetectIndicatorAsync, () => CanEditConfiguration && !IsDetecting);

        SaveConfigurationCommand = _saveHardware;
        DetectIndicatorCommand = _detectPort;
        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        DiscardConfigurationCommand = new RelayCommand(LoadFromOptions);

        RefreshPorts();
    }

    /// <summary>The options objects, still bound for the values this screen does not edit.</summary>
    public HardwareOptions Hardware { get; }
    public PrinterOptions Printer { get; }
    public CameraOptions Camera { get; }
    public ReportingOptions Reporting { get; }
    public DatabaseOptions Database { get; }

    public IReadOnlyList<AppTheme> AvailableThemes { get; } = Enum.GetValues<AppTheme>();
    public IReadOnlyList<int> BaudRates => BaudRateChoices;
    public IReadOnlyList<string> DriverTypes => DriverTypeChoices;
    public IReadOnlyList<string> ParityOptions => ParityChoices;
    public IReadOnlyList<string> StopBitsOptions => StopBitsChoices;

    /// <summary>The COM ports Windows currently reports.</summary>
    public ObservableCollection<string> AvailablePorts { get; } = [];

    /// <summary>What each port said during the last scan, newest scan only.</summary>
    public ObservableCollection<PortProbeResult> DetectionResults { get; } = [];

    /// <summary>
    /// Whether the signed-in operator may change configuration.
    /// </summary>
    /// <remarks>
    /// Enforced here and re-checked in <see cref="SaveConfigurationAsync"/>: a disabled
    /// button is a courtesy, and the check that matters is the one in front of the write.
    /// </remarks>
    public bool CanEditConfiguration => _permissions.HasPermission(Permissions.SettingsEdit);

    /// <summary>
    /// The inverse, so the read-only banner can bind to it directly.
    /// </summary>
    /// <remarks>
    /// A property rather than an inverting converter: the converter this replaces existed
    /// for exactly one binding, and WPF's own <c>BooleanToVisibilityConverter</c> cannot
    /// invert.
    /// </remarks>
    public bool IsConfigurationReadOnly => !CanEditConfiguration;

    public bool IsDetecting
    {
        get => _isDetecting;
        private set
        {
            if (SetProperty(ref _isDetecting, value))
            {
                _saveHardware.NotifyCanExecuteChanged();
                _detectPort.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>The outcome of the last scan, in one line, for the screen.</summary>
    public string? DetectionStatus
    {
        get => _detectionStatus;
        private set => SetProperty(ref _detectionStatus, value);
    }

    public bool IndicatorEnabled
    {
        get => _indicatorEnabled;
        set => SetProperty(ref _indicatorEnabled, value);
    }

    public string DriverType
    {
        get => _driverType;
        set
        {
            if (SetProperty(ref _driverType, value))
            {
                OnPropertyChanged(nameof(IsSerialDriver));
            }
        }
    }

    /// <summary>True when the serial fields matter — the simulator has no port.</summary>
    public bool IsSerialDriver => DriverType.Equals("Serial", StringComparison.OrdinalIgnoreCase);

    public string PortName
    {
        get => _portName;
        set => SetProperty(ref _portName, value);
    }

    public int BaudRate
    {
        get => _baudRate;
        set => SetProperty(ref _baudRate, value);
    }

    public int DataBits
    {
        get => _dataBits;
        set => SetProperty(ref _dataBits, value);
    }

    public string Parity
    {
        get => _parity;
        set => SetProperty(ref _parity, value);
    }

    public string StopBits
    {
        get => _stopBits;
        set => SetProperty(ref _stopBits, value);
    }

    public int StabilitySampleCount
    {
        get => _stabilitySampleCount;
        set => SetProperty(ref _stabilitySampleCount, value);
    }

    public decimal StabilityToleranceKg
    {
        get => _stabilityToleranceKg;
        set => SetProperty(ref _stabilityToleranceKg, value);
    }

    public int StabilityDurationMs
    {
        get => _stabilityDurationMs;
        set => SetProperty(ref _stabilityDurationMs, value);
    }

    public bool AutoReconnect
    {
        get => _autoReconnect;
        set => SetProperty(ref _autoReconnect, value);
    }

    public int ReconnectIntervalMs
    {
        get => _reconnectIntervalMs;
        set => SetProperty(ref _reconnectIntervalMs, value);
    }

    public bool DtrEnable
    {
        get => _dtrEnable;
        set => SetProperty(ref _dtrEnable, value);
    }

    public bool RtsEnable
    {
        get => _rtsEnable;
        set => SetProperty(ref _rtsEnable, value);
    }

    public string Handshake
    {
        get => _handshake;
        set => SetProperty(ref _handshake, value);
    }

    public bool CameraEnabled
    {
        get => _cameraEnabled;
        set => SetProperty(ref _cameraEnabled, value);
    }

    public bool CaptureOnWeighment
    {
        get => _captureOnWeighment;
        set => SetProperty(ref _captureOnWeighment, value);
    }

    public bool PrintingEnabled
    {
        get => _printingEnabled;
        set => SetProperty(ref _printingEnabled, value);
    }

    public string DefaultPrinterName
    {
        get => _defaultPrinterName;
        set => SetProperty(ref _defaultPrinterName, value ?? string.Empty);
    }

    public int CopyCount
    {
        get => _copyCount;
        set => SetProperty(ref _copyCount, value);
    }

    public string ReportOutputDirectory
    {
        get => _reportOutputDirectory;
        set => SetProperty(ref _reportOutputDirectory, value);
    }

    public int MaxRowsPerReport
    {
        get => _maxRowsPerReport;
        set => SetProperty(ref _maxRowsPerReport, value);
    }

    public AppTheme SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                _themeService.ApplyTheme(value);
            }
        }
    }

    public bool IsNavigationCollapsed
    {
        get => _isNavigationCollapsed;
        set
        {
            if (SetProperty(ref _isNavigationCollapsed, value))
            {
                _settingsService.Preferences.IsNavigationCollapsed = value;
            }
        }
    }

    public ICommand SavePreferencesCommand { get; }
    public ICommand ResetPreferencesCommand { get; }
    public ICommand SaveConfigurationCommand { get; }
    public ICommand DetectIndicatorCommand { get; }
    public ICommand RefreshPortsCommand { get; }
    public ICommand DiscardConfigurationCommand { get; }

    /// <summary>
    /// Re-reads the permission-derived state and the port list each time the screen opens.
    /// </summary>
    /// <remarks>
    /// The view model is built once, which may be before anyone has signed in, so the
    /// permission checks it made in its constructor can be stale. A USB-to-serial adapter
    /// can also have been plugged in since, so the port list is read again here rather
    /// than only once.
    /// </remarks>
    public override Task OnNavigatedToAsync(NavigationContext context)
    {
        OnPropertyChanged(nameof(CanEditConfiguration));
        OnPropertyChanged(nameof(IsConfigurationReadOnly));
        _saveHardware.NotifyCanExecuteChanged();
        _detectPort.NotifyCanExecuteChanged();

        LoadFromOptions();
        RefreshPorts();

        return base.OnNavigatedToAsync(context);
    }

    /// <summary>
    /// Copies the live options into the edit buffers, discarding any pending edit.
    /// </summary>
    private void LoadFromOptions()
    {
        var indicator = Hardware.WeightIndicator;

        IndicatorEnabled = indicator.Enabled;
        DriverType = indicator.DriverType;
        PortName = indicator.PortName;
        BaudRate = indicator.BaudRate;
        DataBits = indicator.DataBits;
        Parity = indicator.Parity;
        StopBits = indicator.StopBits;
        StabilitySampleCount = indicator.StabilitySampleCount;
        StabilityToleranceKg = indicator.StabilityToleranceKg;
        StabilityDurationMs = indicator.StabilityDurationMs;
        DtrEnable = indicator.DtrEnable;
        RtsEnable = indicator.RtsEnable;
        Handshake = indicator.Handshake;
        AutoReconnect = indicator.AutoReconnect;
        ReconnectIntervalMs = indicator.ReconnectIntervalMs;

        CameraEnabled = Camera.Enabled;
        CaptureOnWeighment = Camera.CaptureOnWeighment;

        PrintingEnabled = Printer.Enabled;
        DefaultPrinterName = Printer.DefaultPrinterName;
        CopyCount = Printer.CopyCount;

        ReportOutputDirectory = Reporting.OutputDirectory;
        MaxRowsPerReport = Reporting.MaxRowsPerReport;

        OnPropertyChanged(nameof(IsSerialDriver));
    }

    /// <summary>Re-reads the port list, keeping the current selection if it is still there.</summary>
    private void RefreshPorts()
    {
        var ports = _portScanner.GetAvailablePorts();

        AvailablePorts.Clear();

        foreach (var port in ports)
        {
            AvailablePorts.Add(port);
        }

        // The configured port stays in the list even when Windows does not report it. It is
        // the value that will be saved, and dropping it from a bound ComboBox would clear
        // the selection and quietly rewrite the operator's port to nothing.
        if (!string.IsNullOrWhiteSpace(PortName) &&
            !AvailablePorts.Contains(PortName, StringComparer.OrdinalIgnoreCase))
        {
            AvailablePorts.Add(PortName);
        }

        DetectionStatus = ports.Count == 0
            ? "Windows reports no serial ports on this machine. Check the cable and the USB-to-serial driver."
            : $"{ports.Count} serial port(s) available: {string.Join(", ", ports)}.";
    }

    /// <summary>
    /// Tests the indicator connection on the selected port and baud rate using the live framing and parser pipeline.
    /// </summary>
    /// <remarks>
    /// The running driver holds the configured port, so it is disconnected for the duration
    /// of the test and reconnected afterwards.
    /// </remarks>
    private async Task DetectIndicatorAsync()
    {
        IsDetecting = true;
        DetectionResults.Clear();
        DetectionStatus = $"Testing indicator connection on {PortName} at {BaudRate} baud…";

        var wasConnected = _indicator.State == Domain.Enums.ConnectionState.Connected;

        try
        {
            if (wasConnected)
            {
                await _indicator.DisconnectAsync().ConfigureAwait(true);
            }

            RefreshPorts();

            var targetPorts = !string.IsNullOrWhiteSpace(PortName) ? new[] { PortName } : null;
            var targetBauds = BaudRate > 0 ? new[] { BaudRate } : new[] { 2400 };

            var results = await _portScanner
                .ScanAsync(portNames: targetPorts, baudRates: targetBauds)
                .ConfigureAwait(true);

            foreach (var result in results)
            {
                DetectionResults.Add(result);
            }

            var found = results.FirstOrDefault(result => result.SpeaksProtocol);

            if (found is null)
            {
                DetectionStatus = results.Count == 0
                    ? "No serial ports available to test."
                    : $"No valid indicator frames detected on {PortName} at {BaudRate} baud after {results.Count} attempt(s). " +
                      "Verify that the indicator is powered on, wired with DTR/RTS asserted, and sending continuous weight frames.";

                _logger.LogWarning("Indicator connection test found nothing across {Attempts} attempt(s)", results.Count);
                return;
            }

            PortName = found.PortName;
            BaudRate = found.BaudRate;
            DriverType = "Serial";
            IndicatorEnabled = true;
            OnPropertyChanged(nameof(IsSerialDriver));
            RefreshPorts();

            DetectionStatus =
                $"Connection successful on {found.PortName} at {found.BaudRate} baud! Live reading: " +
                $"{found.SampleWeightKg?.ToString("0.##", CultureInfo.CurrentCulture)} kg " +
                $"(frame '{found.RawSample}'). Click Save to apply.";

            _logger.LogInformation(
                "Indicator connection verified on {PortName} at {BaudRate} baud", found.PortName, found.BaudRate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Indicator detection failed");
            DetectionStatus = $"The test could not be completed: {ex.Message}";
        }
        finally
        {
            IsDetecting = false;

            if (wasConnected)
            {
                try
                {
                    await _indicator.ConnectAsync().ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    // Reported, not thrown: the test's own result is the useful outcome here,
                    // and the indicator's status bar tile already shows it is disconnected.
                    _logger.LogWarning(ex, "Could not reconnect the indicator after the test");
                }
            }
        }
    }

    /// <summary>
    /// Validates the edits, writes them to configuration, and reconnects the indicator.
    /// </summary>
    private async Task SaveConfigurationAsync()
    {
        if (!CanEditConfiguration)
        {
            await _dialogService.ShowErrorAsync(
                "Not permitted",
                "Your role does not permit changing the application configuration.").ConfigureAwait(true);
            return;
        }

        if (Validate() is { } problem)
        {
            await _dialogService.ShowErrorAsync("Check the settings", problem).ConfigureAwait(true);
            return;
        }

        try
        {
            await _configurationWriter.SaveAsync(new Dictionary<string, object?>
            {
                ["Hardware:WeightIndicator:Enabled"] = IndicatorEnabled,
                ["Hardware:WeightIndicator:DriverType"] = DriverType,
                ["Hardware:WeightIndicator:PortName"] = PortName,
                ["Hardware:WeightIndicator:BaudRate"] = BaudRate,
                ["Hardware:WeightIndicator:DataBits"] = DataBits,
                ["Hardware:WeightIndicator:Parity"] = Parity,
                ["Hardware:WeightIndicator:StopBits"] = StopBits,
                ["Hardware:WeightIndicator:StabilitySampleCount"] = StabilitySampleCount,
                ["Hardware:WeightIndicator:StabilityToleranceKg"] = StabilityToleranceKg,
                ["Hardware:WeightIndicator:StabilityDurationMs"] = StabilityDurationMs,
                ["Hardware:WeightIndicator:DtrEnable"] = DtrEnable,
                ["Hardware:WeightIndicator:RtsEnable"] = RtsEnable,
                ["Hardware:WeightIndicator:Handshake"] = Handshake,
                ["Hardware:WeightIndicator:AutoReconnect"] = AutoReconnect,
                ["Hardware:WeightIndicator:ReconnectIntervalMs"] = ReconnectIntervalMs,
                ["Camera:Enabled"] = CameraEnabled,
                ["Camera:CaptureOnWeighment"] = CaptureOnWeighment,
                ["Printer:Enabled"] = PrintingEnabled,
                ["Printer:DefaultPrinterName"] = DefaultPrinterName,
                ["Printer:CopyCount"] = CopyCount,
                ["Reporting:OutputDirectory"] = ReportOutputDirectory,
                ["Reporting:MaxRowsPerReport"] = MaxRowsPerReport,
            }).ConfigureAwait(true);

            // The options objects are the ones every other screen already holds, so they are
            // updated in place. Without this the Settings screen would show the new port
            // while the status bar and Vehicle Entry still showed the old one.
            ApplyToOptions();

            _logger.LogInformation(
                "Configuration saved by {Operator}: indicator {DriverType} on {PortName} at {BaudRate} baud",
                _permissions.CurrentOperator.UserName,
                DriverType,
                PortName,
                BaudRate);

            // Which implementation serves IWeightIndicatorService was decided when the
            // container was built, so a change from Simulator to Serial (or the reverse)
            // genuinely needs a restart. Reconnecting covers a port or baud change, which
            // is the case this screen exists for; the message is honest about the rest.
            var reconnected = await TryReconnectIndicatorAsync().ConfigureAwait(true);

            await _dialogService.ShowInformationAsync(
                "Settings saved",
                reconnected
                    ? $"Saved. The indicator was reconnected on {PortName} at {BaudRate} baud."
                    : "Saved. Restart the application for the driver change to take effect.")
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration");
            await _dialogService.ShowErrorAsync("Error", "Could not save the settings: " + ex.Message)
                .ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Rejects values the subsystems cannot use, before anything reaches the file.
    /// </summary>
    /// <returns>The problem to show the operator, or <c>null</c> when the edits are usable.</returns>
    private string? Validate()
    {
        if (IsSerialDriver && string.IsNullOrWhiteSpace(PortName))
        {
            return "A serial indicator needs a port name, for example COM3. Use Detect to find it.";
        }

        if (BaudRate <= 0)
        {
            return "The baud rate must be a positive number.";
        }

        if (DataBits is < 5 or > 8)
        {
            return "Data bits must be between 5 and 8.";
        }

        if (!ParityChoices.Contains(Parity, StringComparer.OrdinalIgnoreCase))
        {
            return $"Parity must be one of: {string.Join(", ", ParityChoices)}.";
        }

        if (!StopBitsChoices.Contains(StopBits, StringComparer.OrdinalIgnoreCase))
        {
            return $"Stop bits must be one of: {string.Join(", ", StopBitsChoices)}.";
        }

        if (StabilitySampleCount < 1)
        {
            return "At least one sample is needed before a weight can be called stable.";
        }

        if (StabilityToleranceKg < 0)
        {
            return "The stability tolerance cannot be negative.";
        }

        if (StabilityDurationMs < 0)
        {
            return "The stability duration cannot be negative.";
        }

        if (ReconnectIntervalMs < 100)
        {
            return "The reconnect interval must be at least 100 ms, or the retry loop will spin.";
        }

        if (CopyCount is < 1 or > 10)
        {
            return "Copies per slip must be between 1 and 10.";
        }

        if (MaxRowsPerReport < 1)
        {
            return "A report must be allowed at least one row.";
        }

        if (string.IsNullOrWhiteSpace(ReportOutputDirectory))
        {
            return "Reports need an output folder.";
        }

        try
        {
            // Catches an unusable path here rather than at the end of a long export.
            Path.GetFullPath(ReportOutputDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return $"'{ReportOutputDirectory}' is not a usable folder path.";
        }

        return null;
    }

    /// <summary>Copies the saved edits into the live options objects.</summary>
    private void ApplyToOptions()
    {
        var indicator = Hardware.WeightIndicator;

        indicator.Enabled = IndicatorEnabled;
        indicator.DriverType = DriverType;
        indicator.PortName = PortName;
        indicator.BaudRate = BaudRate;
        indicator.DataBits = DataBits;
        indicator.Parity = Parity;
        indicator.StopBits = StopBits;
        indicator.StabilitySampleCount = StabilitySampleCount;
        indicator.StabilityToleranceKg = StabilityToleranceKg;
        indicator.StabilityDurationMs = StabilityDurationMs;
        indicator.DtrEnable = DtrEnable;
        indicator.RtsEnable = RtsEnable;
        indicator.Handshake = Handshake;
        indicator.AutoReconnect = AutoReconnect;
        indicator.ReconnectIntervalMs = ReconnectIntervalMs;

        Camera.Enabled = CameraEnabled;
        Camera.CaptureOnWeighment = CaptureOnWeighment;

        Printer.Enabled = PrintingEnabled;
        Printer.DefaultPrinterName = DefaultPrinterName;
        Printer.CopyCount = CopyCount;

        Reporting.OutputDirectory = ReportOutputDirectory;
        Reporting.MaxRowsPerReport = MaxRowsPerReport;
    }

    /// <summary>
    /// Cycles the indicator so a new port or baud rate takes effect without a restart.
    /// </summary>
    private async Task<bool> TryReconnectIndicatorAsync()
    {
        if (!IndicatorEnabled || !IsSerialDriver)
        {
            return false;
        }

        try
        {
            await _indicator.DisconnectAsync().ConfigureAwait(true);
            return await _indicator.ConnectAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not reconnect the indicator after saving settings");
            return false;
        }
    }

    private async Task SavePreferencesAsync()
    {
        try
        {
            _settingsService.Preferences.Theme = SelectedTheme;
            _settingsService.Preferences.IsNavigationCollapsed = IsNavigationCollapsed;
            await _settingsService.SaveAsync().ConfigureAwait(true);
            await _dialogService.ShowInformationAsync("Preferences Saved", "Your preferences have been saved.").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save user preferences.");
            await _dialogService.ShowErrorAsync("Error", "Could not save preferences: " + ex.Message).ConfigureAwait(true);
        }
    }

    private async Task ResetPreferencesAsync()
    {
        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Reset Preferences",
            "Are you sure you want to reset your preferences to default values?").ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        try
        {
            await _settingsService.ResetAsync().ConfigureAwait(true);
            SelectedTheme = _settingsService.Preferences.Theme;
            IsNavigationCollapsed = _settingsService.Preferences.IsNavigationCollapsed;
            await _dialogService.ShowInformationAsync("Preferences Reset", "Preferences have been reset to defaults.").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset preferences.");
            await _dialogService.ShowErrorAsync("Error", "Could not reset preferences: " + ex.Message).ConfigureAwait(true);
        }
    }
}
