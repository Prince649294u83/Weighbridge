using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Dialogs;
using WeighBridge.Core.Events;
using WeighBridge.Core.Events.Catalog;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Navigation;
using WeighBridge.Core.Security;
using WeighBridge.Core.Settings;
using WeighBridge.Core.Theming;
using WeighBridge.App.Services;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// The Settings screen: operator preferences, weight indicator & semantic profile,
/// printing, input workflow rules, port multiplexing, and auxiliary system options.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private static readonly int[] BaudRateChoices = [1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200];
    private static readonly string[] DriverTypeChoices = ["Serial", "Simulator", "Disabled"];
    private static readonly string[] ParityChoices = ["None", "Odd", "Even", "Mark", "Space"];
    private static readonly string[] StopBitsChoices = ["One", "OnePointFive", "Two"];
    private static readonly string[] PrinterTypeChoices = ["Dot Matrix Printer", "Graphics Printer", "Label / Sticker Printer"];
    private static readonly string[] PaperSizeChoices = ["A4", "Half A4 / A5"];
    private static readonly string[] TimeFormatChoices = ["12 Hour", "24 Hour"];
    private static readonly string[] EmailFrequencyChoices = ["Email only Final Entry", "Email Both Entry"];
    private static readonly string[] SmsServiceChoices = ["Modem", "WhatsApp Web", "WhatsApp API", "Disable"];
    private static readonly string[] SmsFrequencyChoices = ["SMS only Final Entry", "SMS Both Entry", "SMS only First Entry"];
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly IDialogService _dialogService;
    private readonly IConfigurationWriter _configurationWriter;
    private readonly IConfiguration? _configuration;
    private readonly IIndicatorPortScanner _portScanner;
    private readonly IWeightIndicatorService _indicator;
    private readonly IPermissionService _permissions;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly IEmailService? _emailService;
    private readonly ILegacyDataImporter? _legacyImporter;
    private readonly IEventPublisher? _eventPublisher;

    private readonly AsyncRelayCommand _saveConfiguration;
    private readonly AsyncRelayCommand _testConnection;
    private readonly AsyncRelayCommand _testEmail;
    private readonly AsyncRelayCommand _importLegacyData;
    private bool _isTestingEmail;
    private bool _isImportingLegacyData;
    private string _legacyImportFilePath = string.Empty;
    private string? _legacyImportStatus;

    private string? _connectionTestStatus;
    private bool _isTestingConnection;

    private AppTheme _selectedTheme;
    private bool _isNavigationCollapsed;

    // Diagnostic Terminal
    private bool _isTerminalActive;
    private bool _isTerminalPoppedOut;
    private string _terminalStatusMessage = "Diagnostic Terminal ready. Launch terminal to release the COM port for raw signal testing.";

    // Weight Indicator
    private bool _indicatorEnabled;
    private string _driverType = "Serial";
    private string _portName = "COM1";
    private int _baudRate = 2400;
    private int _dataBits = 8;
    private string _parity = "None";
    private string _stopBits = "One";
    private int _stabilitySampleCount = 5;
    private decimal _stabilityToleranceKg = 5.0m;
    private int _stabilityDurationMs = 1000;
    private bool _autoReconnect = true;
    private int _reconnectIntervalMs = 1500;
    private bool _dtrEnable = true;
    private bool _rtsEnable = true;
    private string _handshake = "None";

    // Privileged Semantic Decoding Profile
    private string _frameStartChar = "[";
    private string _frameEndChar = "NUL";
    private int _weightDigits = 7;
    private int _decimalPlaces = 1;
    private bool _reversePayload = false;
    private int _trailingDigitsRemoved = 0;
    private decimal _scaleFactor = 1.0m;
    private string _targetUnit = "kg";

    // Printing Settings
    private bool _printingEnabled = true;
    private string _printerType = "Dot Matrix Printer";
    private bool _sideWisePrinting = false;
    private string _defaultPrinterName = string.Empty;
    private int _copyCount = 2;
    private string _paperSize = "A4";

    // Input Settings
    private bool _unitBagsWeightColumn = false;
    private bool _manualTareEntry = true;
    private bool _autoTareWeight = true;
    private bool _secondEntryCharges = false;
    private bool _gstOnCharges = false;
    private bool _onlySingleEntry = false;

    // Other Settings
    private bool _priceComputing = false;
    private int _disconnectTimeSeconds = 300;
    private bool _allowZeroNetWeight = false;
    private bool _autoApplicationShortcut = true;
    private bool _autoUpdateTareWeight = false;
    private bool _weightHold = false;
    private string _timeFormat = "12 Hour";
    private bool _printQrCode = false;
    private bool _chargesMandatory = false;
    private decimal _minimumCharges = 0m;

    // Auxiliary Ports
    private bool _receivePort1Enabled = false;
    private string _receivePort1Name = "COM4";
    private int _receivePort1Baud = 9600;

    private bool _receivePort2Enabled = false;
    private string _receivePort2Name = "COM5";
    private int _receivePort2Baud = 9600;

    private bool _sendDataPortEnabled = false;
    private string _sendDataPortName = "COM6";
    private int _sendDataPortBaud = 9600;

    // Email Settings
    private bool _emailEnabled = false;
    private string _emailFrequency = "Email only Final Entry";
    private bool _emailPdf = true;
    private string _emailSenderName = string.Empty;
    private string _emailSenderId = string.Empty;
    private string _emailPassword = string.Empty;
    private string _emailSmtpServer = string.Empty;
    private int _emailSmtpPort = 587;
    private bool _emailUseSsl = true;
    private string _newRecipientEmail = string.Empty;
    private string? _selectedRecipientEmail;

    // SMS & WhatsApp
    private string _smsService = "Disable";
    private string _smsFrequency = "SMS Both Entry";
    private string _smsNumbers = string.Empty;
    private string _whatsappToken = string.Empty;

    // WB Name Feeding (Company)
    private string _weighbridgeName = string.Empty;
    private string _weighbridgeAddress1 = string.Empty;
    private string _weighbridgeAddress2 = string.Empty;
    private string _companyPhone = string.Empty;
    private string _companyEmail = string.Empty;
    private string _companyTaxId = string.Empty;

    // Weight Indicator Extended
    private string _indicatorEndingString = "NUL (0x00)";
    private bool _indicatorHexValue = false;
    private bool _indicatorEssaeMode = false;
    private bool _indicatorRtsCts = false;
    private int _indicatorBufferData = 50;
    private int _indicatorDummyZero = 0;
    private int _indicatorStableWaitTime = 0;

    // Reporting
    private string _reportOutputDirectory = string.Empty;
    private int _maxRowsPerReport = 1000;

    private int _selectedTabIndex = 0;

    // Signal Diagnostic & Monitor
    private readonly DiagnosticSerialMonitor _diagnosticMonitor = new();
    private readonly StringBuilder _asciiLogBuilder = new();
    private readonly StringBuilder _hexLogBuilder = new();
    private const int MaxLogCharacters = 8000;
    private Process? _braysTerminalProcess;

    private string _diagnosticPort = "COM3";
    private int _diagnosticBaudRate = 2400;
    private int _diagnosticDataBits = 8;
    private string _diagnosticParity = "None";
    private string _diagnosticStopBits = "One";
    private bool _isDiagnosticMonitoring;
    private string _diagnosticDisplayMode = "ASCII";
    private string _diagnosticAsciiLog = string.Empty;
    private string _diagnosticHexLog = string.Empty;
    private string _diagnosticTransmitText = string.Empty;
    private bool _diagnosticAutoScroll = true;
    private long _diagnosticBytesReceivedCount;
    private string _diagnosticStatus = "Ready to monitor signal stream or launch Bray's Terminal.";
    private bool _isCtsHigh;
    private bool _isDsrHigh;
    private bool _isCdHigh;
    private bool _isBraysTerminalRunning;

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
        IOptions<ReportingOptions> reportingOptions,
        IOptions<DatabaseOptions> databaseOptions,
        IOptions<WeighmentOptions>? weighmentOptions,
        ILogger<SettingsViewModel> logger,
        IOptions<CompanyOptions>? companyOptions = null,
        IOptions<SmsOptions>? smsOptions = null,
        IConfiguration? configuration = null,
        IEmailService? emailService = null,
        ILegacyDataImporter? legacyImporter = null,
        IEventPublisher? eventPublisher = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _configurationWriter = configurationWriter ?? throw new ArgumentNullException(nameof(configurationWriter));
        _configuration = configuration;
        _portScanner = portScanner ?? throw new ArgumentNullException(nameof(portScanner));
        _indicator = indicator ?? throw new ArgumentNullException(nameof(indicator));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _emailService = emailService;
        _legacyImporter = legacyImporter;
        _eventPublisher = eventPublisher;

        Hardware = hardwareOptions?.Value ?? new HardwareOptions();
        Printer = printerOptions?.Value ?? new PrinterOptions();
        Reporting = reportingOptions?.Value ?? new ReportingOptions();
        Database = databaseOptions?.Value ?? new DatabaseOptions();
        Weighment = weighmentOptions?.Value ?? new WeighmentOptions();
        Company = companyOptions?.Value ?? new CompanyOptions();
        Sms = smsOptions?.Value ?? new SmsOptions();

        Title = "User Settings";
        Description = "Configure email, printing, input workflow, indicator, SMS, serial ports, and company details.";

        _selectedTheme = _settingsService.Preferences.Theme;
        _isNavigationCollapsed = _settingsService.Preferences.IsNavigationCollapsed;

        RecipientEmails = new ObservableCollection<string>();
        AddRecipientCommand = new RelayCommand(AddRecipient);
        DeleteRecipientCommand = new RelayCommand<string?>(DeleteRecipient, email => !string.IsNullOrWhiteSpace(email));

        LoadFromOptions();

        SavePreferencesCommand = new AsyncRelayCommand(SavePreferencesAsync);
        ResetPreferencesCommand = new AsyncRelayCommand(ResetPreferencesAsync);

        _saveConfiguration = new AsyncRelayCommand(SaveConfigurationAsync, () => CanEditConfiguration && !IsTestingConnection);
        _testConnection = new AsyncRelayCommand(TestConnectionAsync, () => CanEditConfiguration && !IsTestingConnection);
        _testEmail = new AsyncRelayCommand(TestEmailAsync, () => CanEditConfiguration && !IsTestingEmail);
        _importLegacyData = new AsyncRelayCommand(ImportLegacyDataAsync, () => CanEditConfiguration && !IsImportingLegacyData);

        SaveConfigurationCommand = _saveConfiguration;
        TestConnectionCommand = _testConnection;
        TestEmailCommand = _testEmail;
        ImportLegacyDataCommand = _importLegacyData;
        RefreshPortsCommand = new RelayCommand(() => RefreshPorts());
        RefreshPrintersCommand = new RelayCommand(() => RefreshAvailablePrinters());
        DiscardConfigurationCommand = new RelayCommand(LoadFromOptions);
        StartTerminalCommand = new AsyncRelayCommand(LaunchBraysTerminalAsync, () => !IsBraysTerminalRunning);
        LaunchBraysTerminalCommand = StartTerminalCommand;
        StopTerminalCommand = new AsyncRelayCommand(StopBraysTerminalAsync, () => IsBraysTerminalRunning);
        StopBraysTerminalCommand = StopTerminalCommand;
        ToggleTerminalPopOutCommand = new RelayCommand(ToggleTerminalPopOut);

        StartDiagnosticMonitoringCommand = new AsyncRelayCommand(StartDiagnosticMonitoringAsync, () => !IsDiagnosticMonitoring);
        StopDiagnosticMonitoringCommand = new AsyncRelayCommand(StopDiagnosticMonitoringAsync, () => IsDiagnosticMonitoring);
        ClearDiagnosticLogCommand = new RelayCommand(ClearDiagnosticLog);
        CopyDiagnosticLogCommand = new RelayCommand(CopyDiagnosticLog);
        SendDiagnosticTextCommand = new RelayCommand(SendDiagnosticText);
        SendDiagnosticHexCommand = new RelayCommand(SendDiagnosticHex);
        SetAsciiDisplayModeCommand = new RelayCommand(() => DiagnosticDisplayMode = "ASCII");
        SetHexDisplayModeCommand = new RelayCommand(() => DiagnosticDisplayMode = "HEX");

        _diagnosticMonitor.DataReceived += (s, chunk) =>
        {
            void Update()
            {
                if (_asciiLogBuilder.Length > MaxLogCharacters)
                {
                    _asciiLogBuilder.Remove(0, _asciiLogBuilder.Length - (MaxLogCharacters / 2));
                }
                if (_hexLogBuilder.Length > MaxLogCharacters)
                {
                    _hexLogBuilder.Remove(0, _hexLogBuilder.Length - (MaxLogCharacters / 2));
                }

                _asciiLogBuilder.Append(chunk.AsciiRepresentation);
                _hexLogBuilder.Append(chunk.HexRepresentation);

                DiagnosticAsciiLog = _asciiLogBuilder.ToString();
                DiagnosticHexLog = _hexLogBuilder.ToString();
                DiagnosticBytesReceivedCount += chunk.RawBytes.Length;

                IsCtsHigh = chunk.CtsHolding;
                IsDsrHigh = chunk.DsrHolding;
                IsCdHigh = chunk.CdHolding;
            }

            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(Update);
            }
            else
            {
                Update();
            }
        };

        _diagnosticMonitor.ErrorOccurred += (s, err) =>
        {
            void UpdateErr()
            {
                DiagnosticStatus = $"Monitor error: {err}";
            }

            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(UpdateErr);
            }
            else
            {
                UpdateErr();
            }
        };

        RefreshPorts();
        RefreshAvailablePrinters();
    }

    public HardwareOptions Hardware { get; }
    public PrinterOptions Printer { get; }
    public ReportingOptions Reporting { get; }
    public DatabaseOptions Database { get; }
    public WeighmentOptions Weighment { get; }

    public IReadOnlyList<AppTheme> AvailableThemes { get; } = Enum.GetValues<AppTheme>();
    public IReadOnlyList<int> BaudRates => BaudRateChoices;
    public IReadOnlyList<string> DriverTypes => DriverTypeChoices;
    public IReadOnlyList<string> ParityOptions => ParityChoices;
    public IReadOnlyList<string> StopBitsOptions => StopBitsChoices;
    public IReadOnlyList<string> PrinterTypes => PrinterTypeChoices;
    public IReadOnlyList<string> PaperSizes => PaperSizeChoices;
    public IReadOnlyList<string> TimeFormats => TimeFormatChoices;
    public IReadOnlyList<string> EmailFrequencies => EmailFrequencyChoices;
    public IReadOnlyList<string> SmsServices => SmsServiceChoices;
    public IReadOnlyList<string> SmsFrequencies => SmsFrequencyChoices;

    public ObservableCollection<string> AvailablePorts { get; } = [];

    public bool CanEditConfiguration => _permissions.HasPermission(Permissions.SettingsEdit);
    public bool IsConfigurationReadOnly => !CanEditConfiguration;

    public bool IsTestingConnection
    {
        get => _isTestingConnection;
        private set
        {
            if (SetProperty(ref _isTestingConnection, value))
            {
                _saveConfiguration.NotifyCanExecuteChanged();
                _testConnection.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsTestingEmail
    {
        get => _isTestingEmail;
        private set
        {
            if (SetProperty(ref _isTestingEmail, value))
            {
                _testEmail.NotifyCanExecuteChanged();
            }
        }
    }

    public string? ConnectionTestStatus
    {
        get => _connectionTestStatus;
        private set => SetProperty(ref _connectionTestStatus, value);
    }

    #region Preferences Properties

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
        set => SetProperty(ref _isNavigationCollapsed, value);
    }

    #endregion

    #region Weight Indicator Properties

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

    #endregion

    #region Privileged Semantic Decoding Profile

    public string FrameStartChar
    {
        get => _frameStartChar;
        set => SetProperty(ref _frameStartChar, value);
    }

    public string FrameEndChar
    {
        get => _frameEndChar;
        set => SetProperty(ref _frameEndChar, value);
    }

    public int WeightDigits
    {
        get => _weightDigits;
        set => SetProperty(ref _weightDigits, value);
    }

    public int DecimalPlaces
    {
        get => _decimalPlaces;
        set => SetProperty(ref _decimalPlaces, value);
    }

    public bool ReversePayload
    {
        get => _reversePayload;
        set => SetProperty(ref _reversePayload, value);
    }

    public int TrailingDigitsRemoved
    {
        get => _trailingDigitsRemoved;
        set => SetProperty(ref _trailingDigitsRemoved, value);
    }

    public decimal ScaleFactor
    {
        get => _scaleFactor;
        set => SetProperty(ref _scaleFactor, value);
    }

    public string TargetUnit
    {
        get => _targetUnit;
        set => SetProperty(ref _targetUnit, value);
    }

    #endregion

    #region Printing Settings Properties

    public bool PrintingEnabled
    {
        get => _printingEnabled;
        set => SetProperty(ref _printingEnabled, value);
    }

    public string PrinterType
    {
        get => _printerType;
        set => SetProperty(ref _printerType, value);
    }

    public bool SideWisePrinting
    {
        get => _sideWisePrinting;
        set => SetProperty(ref _sideWisePrinting, value);
    }

    public string DefaultPrinterName
    {
        get => _defaultPrinterName;
        set => SetProperty(ref _defaultPrinterName, value);
    }

    public int CopyCount
    {
        get => _copyCount;
        set => SetProperty(ref _copyCount, value);
    }

    public string PaperSize
    {
        get => _paperSize;
        set => SetProperty(ref _paperSize, value);
    }

    public ObservableCollection<string> AvailablePrinters { get; } = [];
    public ICommand RefreshPrintersCommand { get; }

    #endregion

    #region Input Settings Properties

    public bool UnitBagsWeightColumn
    {
        get => _unitBagsWeightColumn;
        set => SetProperty(ref _unitBagsWeightColumn, value);
    }

    public bool ManualTareEntry
    {
        get => _manualTareEntry;
        set => SetProperty(ref _manualTareEntry, value);
    }

    public bool AutoTareWeight
    {
        get => _autoTareWeight;
        set => SetProperty(ref _autoTareWeight, value);
    }

    public bool SecondEntryCharges
    {
        get => _secondEntryCharges;
        set => SetProperty(ref _secondEntryCharges, value);
    }

    public bool GstOnCharges
    {
        get => _gstOnCharges;
        set => SetProperty(ref _gstOnCharges, value);
    }

    public bool OnlySingleEntry
    {
        get => _onlySingleEntry;
        set => SetProperty(ref _onlySingleEntry, value);
    }

    #endregion

    #region Other Settings Properties

    public bool PriceComputing
    {
        get => _priceComputing;
        set => SetProperty(ref _priceComputing, value);
    }

    public int EmailSmtpPort
    {
        get => _emailSmtpPort;
        set => SetProperty(ref _emailSmtpPort, value);
    }

    public bool EmailUseSsl
    {
        get => _emailUseSsl;
        set => SetProperty(ref _emailUseSsl, value);
    }

    public string CompanyPhone
    {
        get => _companyPhone;
        set => SetProperty(ref _companyPhone, value);
    }

    public string CompanyEmail
    {
        get => _companyEmail;
        set => SetProperty(ref _companyEmail, value);
    }

    public string CompanyTaxId
    {
        get => _companyTaxId;
        set => SetProperty(ref _companyTaxId, value);
    }

    public int DisconnectTimeSeconds
    {
        get => _disconnectTimeSeconds;
        set => SetProperty(ref _disconnectTimeSeconds, value);
    }

    public bool AllowZeroNetWeight
    {
        get => _allowZeroNetWeight;
        set => SetProperty(ref _allowZeroNetWeight, value);
    }

    public bool AutoApplicationShortcut
    {
        get => _autoApplicationShortcut;
        set => SetProperty(ref _autoApplicationShortcut, value);
    }

    public bool AutoUpdateTareWeight
    {
        get => _autoUpdateTareWeight;
        set => SetProperty(ref _autoUpdateTareWeight, value);
    }

    public bool WeightHold
    {
        get => _weightHold;
        set => SetProperty(ref _weightHold, value);
    }

    public string TimeFormat
    {
        get => _timeFormat;
        set
        {
            if (SetProperty(ref _timeFormat, value))
            {
                OnPropertyChanged(nameof(Is12HourFormat));
                OnPropertyChanged(nameof(Is24HourFormat));
            }
        }
    }

    public bool Is12HourFormat
    {
        get => string.Equals(TimeFormat, "12 Hour", StringComparison.OrdinalIgnoreCase);
        set { if (value) TimeFormat = "12 Hour"; }
    }

    public bool Is24HourFormat
    {
        get => string.Equals(TimeFormat, "24 Hour", StringComparison.OrdinalIgnoreCase);
        set { if (value) TimeFormat = "24 Hour"; }
    }

    public bool PrintQrCode
    {
        get => _printQrCode;
        set => SetProperty(ref _printQrCode, value);
    }

    public bool ChargesMandatory
    {
        get => _chargesMandatory;
        set => SetProperty(ref _chargesMandatory, value);
    }

    public decimal MinimumCharges
    {
        get => _minimumCharges;
        set => SetProperty(ref _minimumCharges, value);
    }

    #endregion

    #region Port Settings Properties

    public bool ReceivePort1Enabled
    {
        get => _receivePort1Enabled;
        set => SetProperty(ref _receivePort1Enabled, value);
    }

    public string ReceivePort1Name
    {
        get => _receivePort1Name;
        set => SetProperty(ref _receivePort1Name, value);
    }

    public int ReceivePort1Baud
    {
        get => _receivePort1Baud;
        set => SetProperty(ref _receivePort1Baud, value);
    }

    public bool ReceivePort2Enabled
    {
        get => _receivePort2Enabled;
        set => SetProperty(ref _receivePort2Enabled, value);
    }

    public string ReceivePort2Name
    {
        get => _receivePort2Name;
        set => SetProperty(ref _receivePort2Name, value);
    }

    public int ReceivePort2Baud
    {
        get => _receivePort2Baud;
        set => SetProperty(ref _receivePort2Baud, value);
    }

    public bool SendDataPortEnabled
    {
        get => _sendDataPortEnabled;
        set => SetProperty(ref _sendDataPortEnabled, value);
    }

    public string SendDataPortName
    {
        get => _sendDataPortName;
        set => SetProperty(ref _sendDataPortName, value);
    }

    public int SendDataPortBaud
    {
        get => _sendDataPortBaud;
        set => SetProperty(ref _sendDataPortBaud, value);
    }

    #endregion

    #region Email Properties

    public bool EmailEnabled
    {
        get => _emailEnabled;
        set => SetProperty(ref _emailEnabled, value);
    }

    public string EmailFrequency
    {
        get => _emailFrequency;
        set => SetProperty(ref _emailFrequency, value, () =>
        {
            OnPropertyChanged(nameof(IsEmailFinalEntry));
            OnPropertyChanged(nameof(IsEmailBothEntry));
        });
    }

    public bool IsEmailFinalEntry
    {
        get => string.Equals(EmailFrequency, "Email only Final Entry", StringComparison.OrdinalIgnoreCase);
        set { if (value) EmailFrequency = "Email only Final Entry"; }
    }

    public bool IsEmailBothEntry
    {
        get => string.Equals(EmailFrequency, "Email Both Entry", StringComparison.OrdinalIgnoreCase);
        set { if (value) EmailFrequency = "Email Both Entry"; }
    }

    public bool EmailPdf
    {
        get => _emailPdf;
        set => SetProperty(ref _emailPdf, value);
    }

    public string EmailSenderName
    {
        get => _emailSenderName;
        set => SetProperty(ref _emailSenderName, value);
    }

    public string EmailSenderId
    {
        get => _emailSenderId;
        set => SetProperty(ref _emailSenderId, value);
    }

    public string EmailPassword
    {
        get => _emailPassword;
        set => SetProperty(ref _emailPassword, value);
    }

    public string EmailSmtpServer
    {
        get => _emailSmtpServer;
        set => SetProperty(ref _emailSmtpServer, value);
    }

    public string NewRecipientEmail
    {
        get => _newRecipientEmail;
        set => SetProperty(ref _newRecipientEmail, value);
    }

    public ObservableCollection<string> RecipientEmails { get; }

    public string? SelectedRecipientEmail
    {
        get => _selectedRecipientEmail;
        set
        {
            if (SetProperty(ref _selectedRecipientEmail, value))
            {
                (DeleteRecipientCommand as RelayCommand<string?>)?.NotifyCanExecuteChanged();
            }
        }
    }

    public ICommand AddRecipientCommand { get; }
    public ICommand DeleteRecipientCommand { get; }

    private void AddRecipient()
    {
        var email = NewRecipientEmail.Trim();
        if (!EmailPattern.IsMatch(email))
        {
            return;
        }

        if (!RecipientEmails.Contains(email, StringComparer.OrdinalIgnoreCase))
        {
            RecipientEmails.Add(email);
            NewRecipientEmail = string.Empty;
        }
    }

    private void DeleteRecipient(string? email)
    {
        if (email != null && RecipientEmails.Contains(email))
        {
            RecipientEmails.Remove(email);
            if (string.Equals(SelectedRecipientEmail, email, StringComparison.OrdinalIgnoreCase))
            {
                SelectedRecipientEmail = null;
            }
        }
    }

    #endregion

    #region SMS & WhatsApp Properties

    public string SmsService
    {
        get => _smsService;
        set => SetProperty(ref _smsService, NormalizeSmsService(value), () =>
        {
            OnPropertyChanged(nameof(IsSmsModem));
            OnPropertyChanged(nameof(IsSmsWhatsAppWeb));
            OnPropertyChanged(nameof(IsSmsWhatsAppApi));
            OnPropertyChanged(nameof(IsSmsDisabled));
        });
    }

    public string SmsFrequency
    {
        get => _smsFrequency;
        set => SetProperty(ref _smsFrequency, value, () =>
        {
            OnPropertyChanged(nameof(IsSmsFinalEntry));
            OnPropertyChanged(nameof(IsSmsBothEntry));
        });
    }

    public bool IsSmsModem
    {
        get => string.Equals(SmsService, "Modem", StringComparison.OrdinalIgnoreCase);
        set { if (value) SmsService = "Modem"; }
    }

    public bool IsSmsWhatsAppWeb
    {
        get => string.Equals(SmsService, "WhatsApp Web", StringComparison.OrdinalIgnoreCase);
        set { if (value) SmsService = "WhatsApp Web"; }
    }

    public bool IsSmsWhatsAppApi
    {
        get => string.Equals(SmsService, "WhatsApp API", StringComparison.OrdinalIgnoreCase);
        set { if (value) SmsService = "WhatsApp API"; }
    }

    public bool IsSmsDisabled
    {
        get => string.Equals(SmsService, "Disable", StringComparison.OrdinalIgnoreCase);
        set { if (value) SmsService = "Disable"; }
    }

    public bool IsSmsFinalEntry
    {
        get => string.Equals(SmsFrequency, "SMS only Final Entry", StringComparison.OrdinalIgnoreCase);
        set { if (value) SmsFrequency = "SMS only Final Entry"; }
    }

    public bool IsSmsBothEntry
    {
        get => string.Equals(SmsFrequency, "SMS Both Entry", StringComparison.OrdinalIgnoreCase);
        set { if (value) SmsFrequency = "SMS Both Entry"; }
    }

    public string SmsNumbers
    {
        get => _smsNumbers;
        set => SetProperty(ref _smsNumbers, value);
    }

    public string WhatsAppToken
    {
        get => _whatsappToken;
        set => SetProperty(ref _whatsappToken, value);
    }

    #endregion

    #region WB Name Feeding (Company) Properties

    public CompanyOptions Company { get; }
    public SmsOptions Sms { get; }

    public string WeighbridgeName
    {
        get => _weighbridgeName;
        set => SetProperty(ref _weighbridgeName, value);
    }

    public string WeighbridgeAddress1
    {
        get => _weighbridgeAddress1;
        set => SetProperty(ref _weighbridgeAddress1, value);
    }

    public string WeighbridgeAddress2
    {
        get => _weighbridgeAddress2;
        set => SetProperty(ref _weighbridgeAddress2, value);
    }

    #endregion

    #region Weight Indicator Extended Properties

    public string IndicatorEndingString
    {
        get => _indicatorEndingString;
        set => SetProperty(ref _indicatorEndingString, value);
    }

    public bool IndicatorHexValue
    {
        get => _indicatorHexValue;
        set => SetProperty(ref _indicatorHexValue, value);
    }

    public bool IndicatorEssaeMode
    {
        get => _indicatorEssaeMode;
        set => SetProperty(ref _indicatorEssaeMode, value);
    }

    public bool IndicatorRtsCts
    {
        get => _indicatorRtsCts;
        set => SetProperty(ref _indicatorRtsCts, value);
    }

    public int IndicatorBufferData
    {
        get => _indicatorBufferData;
        set => SetProperty(ref _indicatorBufferData, value);
    }

    public int IndicatorDummyZero
    {
        get => _indicatorDummyZero;
        set => SetProperty(ref _indicatorDummyZero, value);
    }

    public int IndicatorStableWaitTime
    {
        get => _indicatorStableWaitTime;
        set => SetProperty(ref _indicatorStableWaitTime, value);
    }

    #endregion

    #region Tab Control

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    #endregion

    #region Reporting Properties

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

    #endregion

    #region Commands

    public ICommand SavePreferencesCommand { get; }
    public ICommand ResetPreferencesCommand { get; }
    public ICommand SaveConfigurationCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand TestEmailCommand { get; }
    public ICommand ImportLegacyDataCommand { get; }
    public ICommand RefreshPortsCommand { get; }
    public ICommand DiscardConfigurationCommand { get; }
    public ICommand StartTerminalCommand { get; }
    public ICommand ToggleTerminalPopOutCommand { get; }
    public ICommand StopTerminalCommand { get; }

    public bool IsTerminalActive
    {
        get => _isTerminalActive;
        set
        {
            if (SetProperty(ref _isTerminalActive, value))
            {
                (StartTerminalCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (StopTerminalCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (ToggleTerminalPopOutCommand as RelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsTerminalPoppedOut
    {
        get => _isTerminalPoppedOut;
        set
        {
            if (SetProperty(ref _isTerminalPoppedOut, value))
            {
                OnPropertyChanged(nameof(PopOutButtonText));
            }
        }
    }

    public string PopOutButtonText => IsTerminalPoppedOut ? "Dock Back" : "Pop Out";

    public string TerminalStatusMessage
    {
        get => _terminalStatusMessage;
        set => SetProperty(ref _terminalStatusMessage, value);
    }

    public ICommand LaunchBraysTerminalCommand { get; }
    public ICommand StopBraysTerminalCommand { get; }
    public ICommand StartDiagnosticMonitoringCommand { get; }
    public ICommand StopDiagnosticMonitoringCommand { get; }
    public ICommand ClearDiagnosticLogCommand { get; }
    public ICommand CopyDiagnosticLogCommand { get; }
    public ICommand SendDiagnosticTextCommand { get; }
    public ICommand SendDiagnosticHexCommand { get; }
    public ICommand SetAsciiDisplayModeCommand { get; }
    public ICommand SetHexDisplayModeCommand { get; }

    public bool IsBraysTerminalRunning
    {
        get => _isBraysTerminalRunning;
        private set
        {
            if (SetProperty(ref _isBraysTerminalRunning, value))
            {
                IsTerminalActive = value;
                (StartTerminalCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (StopTerminalCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (LaunchBraysTerminalCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (StopBraysTerminalCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public string DiagnosticPort
    {
        get => _diagnosticPort;
        set => SetProperty(ref _diagnosticPort, value);
    }

    public int DiagnosticBaudRate
    {
        get => _diagnosticBaudRate;
        set => SetProperty(ref _diagnosticBaudRate, value);
    }

    public int DiagnosticDataBits
    {
        get => _diagnosticDataBits;
        set => SetProperty(ref _diagnosticDataBits, value);
    }

    public string DiagnosticParity
    {
        get => _diagnosticParity;
        set => SetProperty(ref _diagnosticParity, value);
    }

    public string DiagnosticStopBits
    {
        get => _diagnosticStopBits;
        set => SetProperty(ref _diagnosticStopBits, value);
    }

    public bool IsDiagnosticMonitoring
    {
        get => _isDiagnosticMonitoring;
        private set
        {
            if (SetProperty(ref _isDiagnosticMonitoring, value))
            {
                (StartDiagnosticMonitoringCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (StopDiagnosticMonitoringCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public string DiagnosticDisplayMode
    {
        get => _diagnosticDisplayMode;
        set
        {
            if (SetProperty(ref _diagnosticDisplayMode, value))
            {
                OnPropertyChanged(nameof(IsAsciiDisplayMode));
                OnPropertyChanged(nameof(IsHexDisplayMode));
            }
        }
    }

    public bool IsAsciiDisplayMode => DiagnosticDisplayMode == "ASCII";
    public bool IsHexDisplayMode => DiagnosticDisplayMode == "HEX";

    public string DiagnosticAsciiLog
    {
        get => _diagnosticAsciiLog;
        private set => SetProperty(ref _diagnosticAsciiLog, value);
    }

    public string DiagnosticHexLog
    {
        get => _diagnosticHexLog;
        private set => SetProperty(ref _diagnosticHexLog, value);
    }

    public string DiagnosticTransmitText
    {
        get => _diagnosticTransmitText;
        set => SetProperty(ref _diagnosticTransmitText, value);
    }

    public bool DiagnosticAutoScroll
    {
        get => _diagnosticAutoScroll;
        set => SetProperty(ref _diagnosticAutoScroll, value);
    }

    public long DiagnosticBytesReceivedCount
    {
        get => _diagnosticBytesReceivedCount;
        private set => SetProperty(ref _diagnosticBytesReceivedCount, value);
    }

    public string DiagnosticStatus
    {
        get => _diagnosticStatus;
        private set => SetProperty(ref _diagnosticStatus, value);
    }

    public bool IsCtsHigh
    {
        get => _isCtsHigh;
        private set => SetProperty(ref _isCtsHigh, value);
    }

    public bool IsDsrHigh
    {
        get => _isDsrHigh;
        private set => SetProperty(ref _isDsrHigh, value);
    }

    public bool IsCdHigh
    {
        get => _isCdHigh;
        private set => SetProperty(ref _isCdHigh, value);
    }

    public string LegacyImportFilePath
    {
        get => _legacyImportFilePath;
        set => SetProperty(ref _legacyImportFilePath, value);
    }

    public string? LegacyImportStatus
    {
        get => _legacyImportStatus;
        private set => SetProperty(ref _legacyImportStatus, value);
    }

    public bool IsImportingLegacyData
    {
        get => _isImportingLegacyData;
        private set
        {
            if (SetProperty(ref _isImportingLegacyData, value))
            {
                _importLegacyData.NotifyCanExecuteChanged();
            }
        }
    }

    #endregion

    public void RefreshPorts(string? explicitPortToSelect = null)
    {
        var targetPort = explicitPortToSelect ?? PortName;
        AvailablePorts.Clear();

        try
        {
            var ports = _portScanner.GetAvailablePorts();
            foreach (var port in ports)
            {
                AvailablePorts.Add(port);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate available serial ports");
        }

        if (!string.IsNullOrWhiteSpace(targetPort) && !AvailablePorts.Contains(targetPort))
        {
            AvailablePorts.Add(targetPort);
        }

        PortName = targetPort;
    }

    public void RefreshAvailablePrinters(string? explicitPrinterToSelect = null)
    {
        var targetPrinter = explicitPrinterToSelect ?? DefaultPrinterName;
        AvailablePrinters.Clear();

        try
        {
            foreach (string printer in PrinterSettings.InstalledPrinters)
            {
                if (!string.IsNullOrWhiteSpace(printer) && !AvailablePrinters.Contains(printer))
                {
                    AvailablePrinters.Add(printer);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate installed printers");
        }

        if (!string.IsNullOrWhiteSpace(targetPrinter) && !AvailablePrinters.Contains(targetPrinter))
        {
            AvailablePrinters.Add(targetPrinter);
        }

        if (string.IsNullOrWhiteSpace(targetPrinter) && AvailablePrinters.Count > 0)
        {
            try
            {
                using var printDoc = new PrintDocument();
                string defaultSys = printDoc.PrinterSettings.PrinterName;
                targetPrinter = !string.IsNullOrWhiteSpace(defaultSys) && AvailablePrinters.Contains(defaultSys)
                    ? defaultSys
                    : AvailablePrinters[0];
            }
            catch
            {
                targetPrinter = AvailablePrinters[0];
            }
        }

        DefaultPrinterName = targetPrinter;
    }

    private void LoadFromOptions()
    {
        var indicator = Hardware.WeightIndicator;
        _indicatorEnabled = indicator.Enabled;
        _driverType = indicator.DriverType;
        _portName = indicator.PortName;
        _baudRate = indicator.BaudRate;
        _dataBits = indicator.DataBits;
        _parity = indicator.Parity;
        _stopBits = ReadConfiguration("Hardware:WeightIndicator:StopBits", indicator.StopBits);
        _stabilitySampleCount = indicator.StabilitySampleCount;
        _stabilityToleranceKg = indicator.StabilityToleranceKg;
        _stabilityDurationMs = indicator.StabilityDurationMs;
        _autoReconnect = indicator.AutoReconnect;
        _reconnectIntervalMs = indicator.ReconnectIntervalMs;
        _dtrEnable = indicator.DtrEnable;
        _rtsEnable = indicator.RtsEnable;
        _handshake = indicator.Handshake;

        var decoding = indicator.Decoding;
        _weightDigits = decoding?.WeightDigits ?? 7;
        _decimalPlaces = decoding?.DecimalPlaces ?? 1;
        _reversePayload = decoding?.ReversePayload ?? false;
        _trailingDigitsRemoved = decoding?.DigitsToRemoveFromEnd ?? 0;
        _scaleFactor = decoding?.ScaleFactor ?? 1.0m;
        _targetUnit = indicator.Unit ?? "kg";

        // Extended indicator settings
        _indicatorEndingString = ReadConfiguration("Hardware:WeightIndicator:Decoding:EndingString", _indicatorEndingString);
        _indicatorHexValue = ReadConfiguration("Hardware:WeightIndicator:Decoding:HexValue", false);
        _indicatorEssaeMode = ReadConfiguration("Hardware:WeightIndicator:Decoding:EssaeMode", false);
        _indicatorRtsCts = ReadConfiguration("Hardware:WeightIndicator:Decoding:RtsCts", false);
        _indicatorBufferData = ReadConfiguration("Hardware:WeightIndicator:Decoding:BufferData", 50);
        _indicatorDummyZero = ReadConfiguration("Hardware:WeightIndicator:Decoding:DummyZero", 0);
        _indicatorStableWaitTime = ReadConfiguration("Hardware:WeightIndicator:Decoding:StableWaitTime", 0);

        _printingEnabled = Printer.Enabled;
        _printerType = Printer.PrinterType ?? "Dot Matrix Printer";
        _sideWisePrinting = Printer.SideWisePrinting;
        _defaultPrinterName = Printer.DefaultPrinterName;
        _copyCount = Printer.CopyCount;
        _paperSize = Printer.PaperSize ?? "A4";
        RefreshAvailablePrinters(_defaultPrinterName);

        _unitBagsWeightColumn = Weighment.UnitBagsWeightColumn;
        _manualTareEntry = Weighment.ManualTareEntry;
        _autoTareWeight = Weighment.AutoTareWeight;
        _secondEntryCharges = Weighment.SecondEntryCharges;
        _gstOnCharges = Weighment.GstOnCharges;
        _onlySingleEntry = Weighment.OnlySingleEntry;
        _priceComputing = Weighment.PriceComputing;
        _disconnectTimeSeconds = Weighment.DisconnectTimeSeconds;
        _allowZeroNetWeight = Weighment.AllowZeroNetWeight;
        _autoApplicationShortcut = Weighment.AutoApplicationShortcut;
        _autoUpdateTareWeight = Weighment.AutoUpdateTareWeight;
        _weightHold = Weighment.WeightHold;
        _timeFormat = Weighment.TimeFormat ?? "12 Hour";
        _printQrCode = Weighment.PrintQrCode;
        _chargesMandatory = Weighment.ChargesMandatory;
        _minimumCharges = Weighment.MinimumCharges;

        var ports = Hardware.PortSettings;
        _receivePort1Enabled = ports.ReceivePort1.Enabled;
        _receivePort1Name = ports.ReceivePort1.PortName;
        _receivePort1Baud = ports.ReceivePort1.BaudRate;

        _receivePort2Enabled = ports.ReceivePort2.Enabled;
        _receivePort2Name = ports.ReceivePort2.PortName;
        _receivePort2Baud = ports.ReceivePort2.BaudRate;

        _sendDataPortEnabled = ports.SendDataPort.Enabled;
        _sendDataPortName = ports.SendDataPort.PortName;
        _sendDataPortBaud = ports.SendDataPort.BaudRate;

        _weighbridgeName = Company.CompanyName;
        _weighbridgeAddress1 = Company.AddressLine1;
        _weighbridgeAddress2 = Company.AddressLine2;
        _companyPhone = ReadConfiguration("Company:Phone", string.Empty);
        _companyEmail = ReadConfiguration("Company:Email", string.Empty);
        _companyTaxId = ReadConfiguration("Company:TaxId", string.Empty);

        var smsEnabled = ReadConfiguration("Sms:Enabled", Sms.Enabled);
        var smsProvider = ReadConfiguration("Sms:Provider", Sms.Provider.ToString());
        _smsService = smsEnabled
            ? (string.Equals(smsProvider, SmsProviderType.GsmModem.ToString(), StringComparison.OrdinalIgnoreCase) ? "Modem" : "WhatsApp API")
            : "Disable";
        _smsFrequency = ReadConfiguration("Sms:MessageFrequency", _smsFrequency);
        _smsNumbers = Sms.DefaultRecipient ?? string.Empty;
        _whatsappToken = ReadConfiguration("Sms:WhatsAppToken", string.Empty);

        _emailEnabled = ReadConfiguration("Email:Enabled", false);
        _emailFrequency = ReadConfiguration("Email:Frequency", "Email only Final Entry");
        _emailPdf = ReadConfiguration("Email:PdfEnabled", true);
        _emailSenderName = ReadConfiguration("Email:SenderName", string.Empty);
        _emailSenderId = ReadConfiguration("Email:SenderEmail", string.Empty);
        _emailPassword = string.Empty;
        _emailSmtpServer = ReadConfiguration("Email:SmtpServer", string.Empty);
        _emailSmtpPort = ReadConfiguration("Email:SmtpPort", 587);
        _emailUseSsl = ReadConfiguration("Email:UseSsl", true);
        RecipientEmails.Clear();
        foreach (var recipient in ReadConfigurationList("Email:Recipients"))
        {
            RecipientEmails.Add(recipient);
        }
        _selectedRecipientEmail = null;

        _reportOutputDirectory = Reporting.OutputDirectory;
        _maxRowsPerReport = Reporting.MaxRowsPerReport;

        OnPropertyChanged(string.Empty);
    }

    private async Task SavePreferencesAsync()
    {
        try
        {
            _settingsService.Preferences.Theme = SelectedTheme;
            _settingsService.Preferences.IsNavigationCollapsed = IsNavigationCollapsed;
            await _settingsService.SaveAsync().ConfigureAwait(true);
            _themeService.ApplyTheme(SelectedTheme);
            await _dialogService.ShowInformationAsync("Saved", "Your preferences have been saved.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save preferences");
            await _dialogService.ShowErrorAsync("Error", "Could not save preferences: " + ex.Message);
        }
    }

    private async Task ResetPreferencesAsync()
    {
        await _settingsService.ResetAsync().ConfigureAwait(true);
        SelectedTheme = _settingsService.Preferences.Theme;
        IsNavigationCollapsed = _settingsService.Preferences.IsNavigationCollapsed;
        _themeService.ApplyTheme(SelectedTheme);
        await _dialogService.ShowInformationAsync("Reset", "Preferences reset to defaults.");
    }

    private async Task SaveConfigurationAsync()
    {
        if (!CanEditConfiguration)
        {
            await _dialogService.ShowWarningAsync("Permission Denied", "Your role does not permit editing system configuration.");
            return;
        }

        var validationError = Validate();
        if (validationError != null)
        {
            await _dialogService.ShowWarningAsync("Invalid Configuration", validationError);
            return;
        }

        try
        {
            var values = new Dictionary<string, object?>
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
                ["Hardware:WeightIndicator:AutoReconnect"] = AutoReconnect,
                ["Hardware:WeightIndicator:ReconnectIntervalMs"] = ReconnectIntervalMs,
                ["Hardware:WeightIndicator:DtrEnable"] = DtrEnable,
                ["Hardware:WeightIndicator:RtsEnable"] = RtsEnable,
                ["Hardware:WeightIndicator:Handshake"] = Handshake,
                ["Hardware:WeightIndicator:Unit"] = TargetUnit,
                ["Hardware:WeightIndicator:Decoding:WeightDigits"] = WeightDigits,
                ["Hardware:WeightIndicator:Decoding:DecimalPlaces"] = DecimalPlaces,
                ["Hardware:WeightIndicator:Decoding:ReversePayload"] = ReversePayload,
                ["Hardware:WeightIndicator:Decoding:DigitsToRemoveFromEnd"] = TrailingDigitsRemoved,
                ["Hardware:WeightIndicator:Decoding:ScaleFactor"] = ScaleFactor,
                ["Hardware:WeightIndicator:Decoding:EndingString"] = IndicatorEndingString,
                ["Hardware:WeightIndicator:Decoding:HexValue"] = IndicatorHexValue,
                ["Hardware:WeightIndicator:Decoding:EssaeMode"] = IndicatorEssaeMode,
                ["Hardware:WeightIndicator:Decoding:RtsCts"] = IndicatorRtsCts,
                ["Hardware:WeightIndicator:Decoding:BufferData"] = IndicatorBufferData,
                ["Hardware:WeightIndicator:Decoding:DummyZero"] = IndicatorDummyZero,
                ["Hardware:WeightIndicator:Decoding:StableWaitTime"] = IndicatorStableWaitTime,
                ["Hardware:PortSettings:ReceivePort1:Enabled"] = ReceivePort1Enabled,
                ["Hardware:PortSettings:ReceivePort1:PortName"] = ReceivePort1Name,
                ["Hardware:PortSettings:ReceivePort1:BaudRate"] = ReceivePort1Baud,
                ["Hardware:PortSettings:ReceivePort2:Enabled"] = ReceivePort2Enabled,
                ["Hardware:PortSettings:ReceivePort2:PortName"] = ReceivePort2Name,
                ["Hardware:PortSettings:ReceivePort2:BaudRate"] = ReceivePort2Baud,
                ["Hardware:PortSettings:SendDataPort:Enabled"] = SendDataPortEnabled,
                ["Hardware:PortSettings:SendDataPort:PortName"] = SendDataPortName,
                ["Hardware:PortSettings:SendDataPort:BaudRate"] = SendDataPortBaud,
                ["Printer:Enabled"] = PrintingEnabled,
                ["Printer:PrinterType"] = PrinterType,
                ["Printer:SideWisePrinting"] = SideWisePrinting,
                ["Printer:DefaultPrinterName"] = DefaultPrinterName,
                ["Printer:CopyCount"] = CopyCount,
                ["Printer:PaperSize"] = PaperSize,
                ["Weighment:UnitBagsWeightColumn"] = UnitBagsWeightColumn,
                ["Weighment:ManualTareEntry"] = ManualTareEntry,
                ["Weighment:AutoTareWeight"] = AutoTareWeight,
                ["Weighment:SecondEntryCharges"] = SecondEntryCharges,
                ["Weighment:GstOnCharges"] = GstOnCharges,
                ["Weighment:OnlySingleEntry"] = OnlySingleEntry,
                ["Weighment:PriceComputing"] = PriceComputing,
                ["Weighment:DisconnectTimeSeconds"] = DisconnectTimeSeconds,
                ["Weighment:AllowZeroNetWeight"] = AllowZeroNetWeight,
                ["Weighment:AutoApplicationShortcut"] = AutoApplicationShortcut,
                ["Weighment:AutoUpdateTareWeight"] = AutoUpdateTareWeight,
                ["Weighment:WeightHold"] = WeightHold,
                ["Weighment:TimeFormat"] = TimeFormat,
                ["Weighment:PrintQrCode"] = PrintQrCode,
                ["Weighment:ChargesMandatory"] = ChargesMandatory,
                ["Weighment:MinimumCharges"] = MinimumCharges,
                ["Company:CompanyName"] = WeighbridgeName,
                ["Company:AddressLine1"] = WeighbridgeAddress1,
                ["Company:AddressLine2"] = WeighbridgeAddress2,
                ["Company:Phone"] = CompanyPhone,
                ["Company:Email"] = CompanyEmail,
                ["Company:TaxId"] = CompanyTaxId,
                ["Sms:Enabled"] = !SmsService.Equals("Disable", StringComparison.OrdinalIgnoreCase),
                ["Sms:Provider"] = SmsService.Equals("Modem", StringComparison.OrdinalIgnoreCase)
                    ? SmsProviderType.GsmModem
                    : SmsProviderType.HttpGateway,
                ["Sms:MessageFrequency"] = SmsFrequency,
                ["Sms:DefaultRecipient"] = SmsNumbers,
                ["Sms:WhatsAppToken"] = WhatsAppToken,
                ["Email:Enabled"] = EmailEnabled,
                ["Email:Frequency"] = EmailFrequency,
                ["Email:PdfEnabled"] = EmailPdf,
                ["Email:SenderName"] = EmailSenderName,
                ["Email:SenderEmail"] = EmailSenderId,
                ["Email:SmtpServer"] = EmailSmtpServer,
                ["Email:SmtpPort"] = EmailSmtpPort,
                ["Email:UseSsl"] = EmailUseSsl,
                ["Email:Recipients"] = RecipientEmails.ToArray(),
                ["Reporting:OutputDirectory"] = ReportOutputDirectory,
                ["Reporting:MaxRowsPerReport"] = MaxRowsPerReport,
            };

            if (!string.IsNullOrWhiteSpace(EmailPassword))
            {
                values["Email:Password"] = EmailPassword;
            }

            ApplyToOptions();

            await _configurationWriter.SaveAsync(values).ConfigureAwait(true);

            _logger.LogInformation("Configuration saved by {Operator}", _permissions.CurrentOperator.UserName);

            var reconnected = await TryReconnectIndicatorAsync().ConfigureAwait(true);

            if (_eventPublisher != null)
            {
                _eventPublisher.Publish(new SettingsChangedEvent("Hardware", null, "SettingsViewModel"));
                _eventPublisher.Publish(new SettingsChangedEvent("Weighment", null, "SettingsViewModel"));
                _eventPublisher.Publish(new SettingsChangedEvent("Printer", null, "SettingsViewModel"));
                _eventPublisher.Publish(new SettingsChangedEvent("Company", null, "SettingsViewModel"));
                _eventPublisher.Publish(new SettingsChangedEvent("Reporting", null, "SettingsViewModel"));
                _eventPublisher.Publish(new SettingsChangedEvent("Sms", null, "SettingsViewModel"));
                _eventPublisher.Publish(new SettingsChangedEvent("Email", null, "SettingsViewModel"));
                _eventPublisher.Publish(new SettingsChangedEvent("All", null, "SettingsViewModel"));
            }

            ShortcutService.EnsureDesktopShortcut(AutoApplicationShortcut, _logger);

            await _dialogService.ShowInformationAsync(
                "Settings Saved",
                reconnected
                    ? $"Settings saved successfully. Indicator reconnected on {PortName} at {BaudRate} baud."
                    : "Settings saved successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration");
            await _dialogService.ShowErrorAsync("Save Error", "Could not save settings: " + ex.Message);
        }
    }

    private string? Validate()
    {
        if (IsSerialDriver && string.IsNullOrWhiteSpace(PortName))
            return "Serial weight indicator requires a valid COM port.";

        if (BaudRate <= 0)
            return "Baud rate must be positive.";

        if (DataBits is < 5 or > 8)
            return "Data bits must be between 5 and 8.";

        if (CopyCount is < 1 or > 10)
            return "Print copy count must be between 1 and 10.";

        if (!DriverTypeChoices.Contains(DriverType))
            return "Driver type must be Serial, Simulator, or Disabled.";

        if (!ParityChoices.Contains(Parity))
            return "Parity must be one of the supported serial parity values.";

        if (!StopBitsChoices.Contains(StopBits))
            return "Stop bits must be One, OnePointFive, or Two.";

        if (!PrinterTypeChoices.Contains(PrinterType))
            return "Printer type is not supported.";

        if (!PaperSizeChoices.Contains(PaperSize))
            return "Paper size is not supported.";

        if (WeightDigits <= 0)
            return "Number of weight digits must be greater than zero.";

        if (DecimalPlaces < 0)
            return "Decimal places must be zero or greater.";

        if (TrailingDigitsRemoved < 0)
            return "Digits to remove from end must be zero or greater.";

        if (DisconnectTimeSeconds <= 0)
            return "Disconnect time must be greater than zero seconds.";

        if (MinimumCharges < 0)
            return "Minimum charges cannot be negative.";

        if (IndicatorBufferData < 0)
            return "Buffer data cannot be negative.";

        if (IndicatorDummyZero < 0)
            return "Dummy zero cannot be negative.";

        if (IndicatorStableWaitTime < 0)
            return "Stable wait time cannot be negative.";

        if (!EmailFrequencyChoices.Contains(EmailFrequency))
            return "Email frequency is not supported.";

        if (EmailEnabled)
        {
            if (string.IsNullOrWhiteSpace(EmailSenderId) || !EmailPattern.IsMatch(EmailSenderId.Trim()))
                return "A valid sender email address is required when email is enabled.";

            if (string.IsNullOrWhiteSpace(EmailSmtpServer))
                return "SMTP server is required when email is enabled.";
        }

        if (RecipientEmails.Any(email => !EmailPattern.IsMatch(email)))
            return "Recipient list contains an invalid email address.";

        if (!SmsServiceChoices.Contains(SmsService))
            return "SMS service is not supported.";

        if (!SmsFrequencyChoices.Contains(SmsFrequency))
            return "SMS frequency is not supported.";

        if (ReceivePort1Baud <= 0 || ReceivePort2Baud <= 0 || SendDataPortBaud <= 0)
            return "Configured serial port baud rates must be positive.";

        return null;
    }

    private T ReadConfiguration<T>(string key, T fallback)
    {
        if (_configuration is null)
        {
            return fallback;
        }

        return _configuration.GetValue<T?>(key) ?? fallback;
    }

    private static string NormalizeSmsService(string? value)
    {
        if (string.Equals(value, "Enable", StringComparison.OrdinalIgnoreCase))
        {
            return "Modem";
        }

        return string.IsNullOrWhiteSpace(value) ? "Disable" : value.Trim();
    }

    private IReadOnlyList<string> ReadConfigurationList(string key)
    {
        if (_configuration is null)
        {
            return [];
        }

        return _configuration.GetSection(key)
            .GetChildren()
            .Select(child => child.Value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void ApplyToOptions()
    {
        var ind = Hardware.WeightIndicator;
        ind.Enabled = IndicatorEnabled;
        ind.DriverType = DriverType;
        ind.PortName = PortName;
        ind.BaudRate = BaudRate;
        ind.DataBits = DataBits;
        ind.Parity = Parity;
        ind.StopBits = StopBits;
        ind.StabilitySampleCount = StabilitySampleCount;
        ind.StabilityToleranceKg = StabilityToleranceKg;
        ind.StabilityDurationMs = StabilityDurationMs;
        ind.DtrEnable = DtrEnable;
        ind.RtsEnable = RtsEnable;
        ind.Handshake = Handshake;
        ind.AutoReconnect = AutoReconnect;
        ind.ReconnectIntervalMs = ReconnectIntervalMs;
        ind.Unit = TargetUnit;

        ind.Decoding.WeightDigits = WeightDigits;
        ind.Decoding.DecimalPlaces = DecimalPlaces;
        ind.Decoding.ReversePayload = ReversePayload;
        ind.Decoding.DigitsToRemoveFromEnd = TrailingDigitsRemoved;
        ind.Decoding.ScaleFactor = ScaleFactor;
        ind.Decoding.EndingString = IndicatorEndingString;
        ind.Decoding.HexValue = IndicatorHexValue;
        ind.Decoding.EssaeMode = IndicatorEssaeMode;
        ind.Decoding.RtsCts = IndicatorRtsCts;
        ind.Decoding.BufferData = IndicatorBufferData;
        ind.Decoding.DummyZero = IndicatorDummyZero;
        ind.Decoding.StableWaitTime = IndicatorStableWaitTime;

        Printer.Enabled = PrintingEnabled;
        Printer.PrinterType = PrinterType;
        Printer.SideWisePrinting = SideWisePrinting;
        Printer.DefaultPrinterName = DefaultPrinterName;
        Printer.CopyCount = CopyCount;
        Printer.PaperSize = PaperSize;

        Weighment.UnitBagsWeightColumn = UnitBagsWeightColumn;
        Weighment.ManualTareEntry = ManualTareEntry;
        Weighment.AutoTareWeight = AutoTareWeight;
        Weighment.SecondEntryCharges = SecondEntryCharges;
        Weighment.GstOnCharges = GstOnCharges;
        Weighment.OnlySingleEntry = OnlySingleEntry;
        Weighment.PriceComputing = PriceComputing;
        Weighment.DisconnectTimeSeconds = DisconnectTimeSeconds;
        Weighment.AllowZeroNetWeight = AllowZeroNetWeight;
        Weighment.AutoApplicationShortcut = AutoApplicationShortcut;
        Weighment.AutoUpdateTareWeight = AutoUpdateTareWeight;
        Weighment.WeightHold = WeightHold;
        Weighment.TimeFormat = TimeFormat;
        Weighment.PrintQrCode = PrintQrCode;
        Weighment.ChargesMandatory = ChargesMandatory;
        Weighment.MinimumCharges = MinimumCharges;

        Company.CompanyName = WeighbridgeName;
        Company.AddressLine1 = WeighbridgeAddress1;
        Company.AddressLine2 = WeighbridgeAddress2;
        Company.Phone = CompanyPhone;
        Company.Email = CompanyEmail;
        Company.TaxId = CompanyTaxId;

        var ports = Hardware.PortSettings;
        ports.ReceivePort1.Enabled = ReceivePort1Enabled;
        ports.ReceivePort1.PortName = ReceivePort1Name;
        ports.ReceivePort1.BaudRate = ReceivePort1Baud;

        ports.ReceivePort2.Enabled = ReceivePort2Enabled;
        ports.ReceivePort2.PortName = ReceivePort2Name;
        ports.ReceivePort2.BaudRate = ReceivePort2Baud;

        ports.SendDataPort.Enabled = SendDataPortEnabled;
        ports.SendDataPort.PortName = SendDataPortName;
        ports.SendDataPort.BaudRate = SendDataPortBaud;

        Reporting.OutputDirectory = ReportOutputDirectory;
        Reporting.MaxRowsPerReport = MaxRowsPerReport;
    }

    private async Task<bool> TryReconnectIndicatorAsync()
    {
        if (!IndicatorEnabled || !IsSerialDriver) return false;
        try
        {
            await _indicator.DisconnectAsync().ConfigureAwait(true);
            return await _indicator.ConnectAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Live reconnect after settings change was not successful");
            return false;
        }
    }

    private async Task TestConnectionAsync()
    {
        if (IsTestingConnection) return;
        try
        {
            IsTestingConnection = true;
            ConnectionTestStatus = $"Testing connection on {PortName} at {BaudRate} baud...";

            if (string.IsNullOrWhiteSpace(PortName))
            {
                ConnectionTestStatus = "Please select or enter a valid COM port name.";
                return;
            }

            ApplyToOptions();

            // Test strictly against the configured single port using the coordinated production indicator service
            await _indicator.DisconnectAsync().ConfigureAwait(true);
            var connected = await _indicator.ConnectAsync().ConfigureAwait(true);
            if (connected)
            {
                var reading = _indicator.CurrentReading;
                ConnectionTestStatus = $"Connection successful on {PortName} ({BaudRate} baud). Live reading: {reading.Value:F1} {reading.Unit}";
            }
            else
            {
                ConnectionTestStatus = $"Unable to connect to {PortName}. Check physical cable and port permissions.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection test failed for {PortName}", PortName);
            ConnectionTestStatus = "Connection test error: " + ex.Message;
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    private async Task TestEmailAsync()
    {
        if (IsTestingEmail) return;
        if (string.IsNullOrWhiteSpace(EmailSmtpServer))
        {
            await _dialogService.ShowWarningAsync("Email Test", "Please enter an SMTP Server address before testing.");
            return;
        }

        try
        {
            IsTestingEmail = true;
            bool success = false;
            if (_emailService != null)
            {
                success = await _emailService.TestConnectionAsync().ConfigureAwait(true);
            }
            else
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                var port = EmailSmtpPort > 0 ? EmailSmtpPort : 25;
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await tcp.ConnectAsync(EmailSmtpServer, port, cts.Token).ConfigureAwait(true);
                success = tcp.Connected;
            }

            if (success)
            {
                await _dialogService.ShowInformationAsync("Email Test Successful", $"Successfully reached SMTP server {EmailSmtpServer}:{EmailSmtpPort}.");
            }
            else
            {
                await _dialogService.ShowErrorAsync("Email Test Failed", $"Unable to connect to SMTP server {EmailSmtpServer}:{EmailSmtpPort}. Please check host and port.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP test failed for {Server}:{Port}", EmailSmtpServer, EmailSmtpPort);
            await _dialogService.ShowErrorAsync("Email Test Error", $"Connection failed: {ex.Message}");
        }
        finally
        {
            IsTestingEmail = false;
        }
    }

    private async Task ImportLegacyDataAsync()
    {
        if (IsImportingLegacyData) return;

        if (string.IsNullOrWhiteSpace(LegacyImportFilePath))
        {
            await _dialogService.ShowWarningAsync("Legacy Import", "Please specify the full path to the .mdb, .accdb, or .csv database file.");
            return;
        }

        if (!File.Exists(LegacyImportFilePath))
        {
            await _dialogService.ShowErrorAsync("File Not Found", $"The file could not be found:\n{LegacyImportFilePath}");
            return;
        }

        if (_legacyImporter == null)
        {
            await _dialogService.ShowErrorAsync("Service Unavailable", "Legacy data importer service is not registered.");
            return;
        }

        try
        {
            IsImportingLegacyData = true;
            LegacyImportStatus = "Importing legacy data in background...";

            var progress = new Progress<double>(p =>
            {
                LegacyImportStatus = $"Importing... {(int)(p * 100)}%";
            });

            var result = await _legacyImporter.ImportAsync(LegacyImportFilePath, progress).ConfigureAwait(true);

            var summary = $"Import completed successfully!\n\n" +
                          $"• Parties: {result.PartiesImported}\n" +
                          $"• Materials: {result.MaterialsImported}\n" +
                          $"• Vehicles: {result.VehiclesImported}\n" +
                          $"• Weighments: {result.WeighmentsImported}\n";

            if (result.HasErrors)
            {
                summary += $"\nWarnings/Errors encountered: {result.ErrorsEncountered}\n" +
                           string.Join("\n", result.ErrorMessages.Take(5));
            }

            LegacyImportStatus = $"Finished: {result.WeighmentsImported} weighments, {result.PartiesImported} parties, {result.MaterialsImported} materials, {result.VehiclesImported} vehicles imported.";
            if (_eventPublisher != null && (result.VehiclesImported > 0 || result.PartiesImported > 0 || result.MaterialsImported > 0 || result.WeighmentsImported > 0))
            {
                _eventPublisher.Publish(new SettingsChangedEvent("Catalog", null, "SettingsViewModel"));
            }
            await _dialogService.ShowInformationAsync("Legacy Migration Complete", summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import legacy database from {Path}", LegacyImportFilePath);
            LegacyImportStatus = "Import failed: " + ex.Message;
            await _dialogService.ShowErrorAsync("Import Failed", ex.Message);
        }
        finally
        {
            IsImportingLegacyData = false;
        }
    }

    #region Signal Diagnostic & Terminal Control

    public async Task StartTerminalAsync()
    {
        await LaunchBraysTerminalAsync();
    }

    public async Task LaunchBraysTerminalAsync()
    {
        try
        {
            if (IsDiagnosticMonitoring)
            {
                await StopDiagnosticMonitoringAsync();
            }

            TerminalStatusMessage = $"Releasing serial port {PortName} for Bray's Terminal...";
            await _indicator.DisconnectAsync();

            var exePath = ResolveTerminalExecutablePath();
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                TerminalStatusMessage = "Bray's Terminal.exe could not be found on disk.";
                await _indicator.ConnectAsync();
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory,
                UseShellExecute = true
            };

            _braysTerminalProcess = Process.Start(startInfo);
            if (_braysTerminalProcess == null)
            {
                TerminalStatusMessage = "Failed to launch Terminal.exe.";
                await _indicator.ConnectAsync();
                return;
            }

            _braysTerminalProcess.EnableRaisingEvents = true;
            _braysTerminalProcess.Exited += (s, e) =>
            {
                void HandleExit()
                {
                    IsTerminalActive = false;
                    IsBraysTerminalRunning = false;
                    TerminalStatusMessage = $"Bray's Terminal closed. Restoring live scale indicator on {PortName}...";
                    Task.Run(async () =>
                    {
                        try
                        {
                            await _indicator.ConnectAsync();
                            TerminalStatusMessage = $"Port {PortName} restored. Normal scale indicator streaming resumed.";
                        }
                        catch (Exception ex)
                        {
                            TerminalStatusMessage = $"Port reconnect failed: {ex.Message}";
                        }
                    });
                }

                if (System.Windows.Application.Current?.Dispatcher != null)
                {
                    System.Windows.Application.Current.Dispatcher.InvokeAsync(HandleExit);
                }
                else
                {
                    HandleExit();
                }
            };

            IsTerminalActive = true;
            IsBraysTerminalRunning = true;
            TerminalStatusMessage = $"Serial port {PortName} released. Bray's Terminal is active in standalone window.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch Bray's Terminal");
            TerminalStatusMessage = $"Failed to launch terminal: {ex.Message}";
            try { await _indicator.ConnectAsync(); } catch { }
        }
    }

    private void ToggleTerminalPopOut()
    {
        // Standalone mode is always a clean native window
    }

    public async Task StopTerminalAsync()
    {
        await StopBraysTerminalAsync();
    }

    public async Task StopBraysTerminalAsync()
    {
        try
        {
            if (_braysTerminalProcess != null && !_braysTerminalProcess.HasExited)
            {
                try
                {
                    _braysTerminalProcess.CloseMainWindow();
                    if (!_braysTerminalProcess.WaitForExit(500))
                    {
                        _braysTerminalProcess.Kill();
                    }
                }
                catch { }
                finally
                {
                    _braysTerminalProcess.Dispose();
                    _braysTerminalProcess = null;
                }
            }

            IsTerminalActive = false;
            IsBraysTerminalRunning = false;
            TerminalStatusMessage = $"Stopping terminal and reconnecting {PortName}...";
            await _indicator.ConnectAsync();
            TerminalStatusMessage = $"Terminal stopped. Normal scale indicator streaming resumed on {PortName}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop terminal or reconnect indicator");
            TerminalStatusMessage = $"Terminal stopped, but indicator reconnect failed: {ex.Message}";
        }
    }

    private async Task StartDiagnosticMonitoringAsync()
    {
        try
        {
            DiagnosticStatus = $"Connecting to {DiagnosticPort}...";

            if (string.Equals(DiagnosticPort, PortName, StringComparison.OrdinalIgnoreCase))
            {
                await _indicator.DisconnectAsync();
            }

            var parity = Enum.TryParse<System.IO.Ports.Parity>(DiagnosticParity, true, out var p) ? p : System.IO.Ports.Parity.None;
            var stopBits = DiagnosticStopBits switch
            {
                "1.5" => System.IO.Ports.StopBits.OnePointFive,
                "OnePointFive" => System.IO.Ports.StopBits.OnePointFive,
                "2" => System.IO.Ports.StopBits.Two,
                "Two" => System.IO.Ports.StopBits.Two,
                _ => System.IO.Ports.StopBits.One
            };

            var ok = await _diagnosticMonitor.StartAsync(
                DiagnosticPort,
                DiagnosticBaudRate,
                DiagnosticDataBits,
                parity,
                stopBits);

            if (ok)
            {
                IsDiagnosticMonitoring = true;
                DiagnosticStatus = $"Monitoring active on {DiagnosticPort} @ {DiagnosticBaudRate} bps";
            }
            else
            {
                DiagnosticStatus = $"Could not open {DiagnosticPort}. Make sure it is not in use.";
                if (string.Equals(DiagnosticPort, PortName, StringComparison.OrdinalIgnoreCase))
                {
                    await _indicator.ConnectAsync();
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticStatus = $"Error: {ex.Message}";
        }
    }

    private async Task StopDiagnosticMonitoringAsync()
    {
        await _diagnosticMonitor.StopAsync();
        IsDiagnosticMonitoring = false;
        DiagnosticStatus = "Monitoring stopped.";

        if (string.Equals(DiagnosticPort, PortName, StringComparison.OrdinalIgnoreCase))
        {
            try { await _indicator.ConnectAsync(); } catch { }
        }
    }

    private void ClearDiagnosticLog()
    {
        _asciiLogBuilder.Clear();
        _hexLogBuilder.Clear();
        DiagnosticAsciiLog = string.Empty;
        DiagnosticHexLog = string.Empty;
        DiagnosticBytesReceivedCount = 0;
    }

    private void CopyDiagnosticLog()
    {
        try
        {
            var text = IsHexDisplayMode ? DiagnosticHexLog : DiagnosticAsciiLog;
            if (!string.IsNullOrEmpty(text))
            {
                System.Windows.Clipboard.SetText(text);
            }
        }
        catch { }
    }

    private void SendDiagnosticText()
    {
        if (string.IsNullOrWhiteSpace(DiagnosticTransmitText)) return;
        _diagnosticMonitor.Send(DiagnosticTransmitText, appendCrLf: true);
    }

    private void SendDiagnosticHex()
    {
        if (string.IsNullOrWhiteSpace(DiagnosticTransmitText)) return;
        _diagnosticMonitor.SendHex(DiagnosticTransmitText);
    }

    private static string? ResolveTerminalExecutablePath()
    {
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "Terminal.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Terminal.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WeighBridge Modern", "Tools", "Terminal.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WeighBridge Modern", "Terminal.exe"),
            Path.Combine(Environment.CurrentDirectory, "src", "WeighBridge.App", "Tools", "Terminal.exe"),
            Path.Combine(Environment.CurrentDirectory, "tools", "Terminal.exe"),
            @"E:\Projects\Weighbridge Entry Dongle 2025\Weighbridge Entry Dongle\Terminal.exe"
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public override async Task OnNavigatedFromAsync()
    {
        if (IsDiagnosticMonitoring)
        {
            await StopDiagnosticMonitoringAsync();
        }

        if (IsTerminalActive)
        {
            await StopTerminalAsync();
        }

        await base.OnNavigatedFromAsync();
    }

    #endregion
}
