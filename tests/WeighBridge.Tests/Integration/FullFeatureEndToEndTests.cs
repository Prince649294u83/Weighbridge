using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.App.ViewModels;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Dialogs;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Navigation;
using WeighBridge.Core.Notifications;
using WeighBridge.Core.Printing;
using WeighBridge.Core.Reporting;
using WeighBridge.Core.Security;
using WeighBridge.Core.Threading;
using WeighBridge.Core.Undo;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Masters;
using WeighBridge.Domain.Weighments;
using WeighBridge.Infrastructure.Persistence;
using WeighBridge.Infrastructure.Repositories;
using WeighBridge.Printing.Services;
using WeighBridge.Reporting.Services;
using WeighBridge.Services.Busy;
using WeighBridge.Services.Commands;
using WeighBridge.Services.Events;
using WeighBridge.Services.Masters;
using WeighBridge.Services.Notifications;
using WeighBridge.Services.Security;
using WeighBridge.Services.Undo;
using WeighBridge.Services.Weighments;
using WeighBridge.Tests.Infrastructure;
using Xunit;

namespace WeighBridge.Tests.Integration;

public sealed class FullFeatureEndToEndTests : IDisposable
{
    private readonly TempDataRoot _root = new();
    private readonly DbContextOptions<WeighBridgeDbContext> _dbOptions;
    private readonly SignedInOperator _operator;
    private readonly Func<IUnitOfWork> _uowFactory;
    private readonly PermissionService _permissions;
    private readonly ICommandExecutor _executor;
    private readonly EventBus _events;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TestUiDispatcher _dispatcher;
    private readonly StubDialogService _dialogs;
    private readonly IPrintService _printService;
    private readonly IWeightIndicatorService _indicator;
    private readonly ICameraService _cameraService;

    // Services
    private readonly IVehicleService _vehicleService;
    private readonly IPartyService _partyService;
    private readonly IMaterialService _materialService;
    private readonly IVehicleTypeService _vehicleTypeService;
    private readonly IWeighmentService _weighmentService;
    private readonly IReportService _reportService;

    public FullFeatureEndToEndTests()
    {
        _root.Paths.EnsureCreated();
        var dbPath = Path.Combine(_root.Paths.DatabaseDirectory, "e2e_test.db");
        _dbOptions = new DbContextOptionsBuilder<WeighBridgeDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        using (var initialContext = new WeighBridgeDbContext(_dbOptions))
        {
            initialContext.Database.Migrate();
        }

        _operator = new SignedInOperator { UserName = "admin" };
        _uowFactory = () => new UnitOfWork(new WeighBridgeDbContext(_dbOptions), _operator);

        var appInfo = new TestApplicationInfoService();
        _loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        _dispatcher = new TestUiDispatcher();
        var appLogger = new ApplicationLogger(_loggerFactory, appInfo);

        _events = new EventBus(_dispatcher, _loggerFactory.CreateLogger<EventBus>());
        _permissions = new PermissionService(appInfo, appLogger, _operator);
        _permissions.SetOperator(new OperatorIdentity("Admin", "admin", Roles.Administrator));

        var undo = new UndoManager(Options.Create(new UndoOptions()), appLogger);
        var notif = new NotificationManager(_events, _dispatcher, Options.Create(new NotificationOptions()), _loggerFactory.CreateLogger<NotificationManager>());
        var audit = new AuditLogger(_loggerFactory, appInfo);
        var uiAudit = new UIInteractionLogger(_loggerFactory, appInfo);

        _executor = new CommandExecutor(
            _permissions,
            new BusyStateService(_dispatcher, appLogger),
            undo,
            _events,
            notif,
            audit,
            uiAudit);

        _dialogs = new StubDialogService();
        _printService = new StubPrintService();
        _indicator = new StubIndicatorService();
        _cameraService = new StubCameraService();

        // Master services
        _vehicleService = new VehicleService(_uowFactory, _permissions, _events, _loggerFactory.CreateLogger<VehicleService>());
        _partyService = new PartyService(_uowFactory, _permissions, _events, _loggerFactory.CreateLogger<PartyService>());
        _materialService = new MaterialService(_uowFactory, _permissions, _events, _loggerFactory.CreateLogger<MaterialService>());
        _vehicleTypeService = new VehicleTypeService(_uowFactory, _permissions, _events, _loggerFactory.CreateLogger<VehicleTypeService>());

        // Weighment service
        var weighmentOpts = Options.Create(new WeighmentOptions
        {
            ManualTareEntry = true,
            SecondEntryCharges = true,
            ChargesMandatory = false
        });

        _weighmentService = new WeighmentService(
            _uowFactory,
            _permissions,
            _events,
            _loggerFactory.CreateLogger<WeighmentService>(),
            weighmentOpts);

        // Report service
        var reportOpts = Options.Create(new ReportingOptions
        {
            OutputDirectory = _root.Root,
            MaxRowsPerReport = 50000
        });
        var companyOpts = Options.Create(new CompanyOptions
        {
            CompanyName = "Test Apex WeighBridge Pvt Ltd",
            AddressLine1 = "Industrial Zone 4",
            Phone = "020-12345678"
        });

        _reportService = new CsvReportService(
            _uowFactory,
            _permissions,
            reportOpts,
            _loggerFactory.CreateLogger<CsvReportService>(),
            companyOpts);
    }

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task CompleteSystem_EndToEnd_Lifecycle_With_Realistic_Dummy_Data()
    {
        // =========================================================================
        // FEATURE 1: MASTERS MANAGEMENT (Create Vehicle, Type, Party, Material)
        // =========================================================================
        const string dummyVehicleNo = "MH14AZ7777";
        const string dummyVehicleType = "16 Wheeler Tipper";
        const string dummyPartyName = "UltraTech Cement Ltd";
        const string dummyMaterialName = "Fly Ash Grade-A";
        const decimal dummyFirstWeightKg = 45200m;
        const decimal dummySecondWeightKg = 12400m;
        const decimal dummyNetWeightKg = 32800m;
        const decimal dummyCharges = 350m;

        // 1.1 Create Vehicle Type
        var createdType = await _vehicleTypeService.CreateAsync(new CreateVehicleTypeRequest(dummyVehicleType, "Heavy 16-wheel tipper"));
        Assert.NotNull(createdType);
        Assert.Equal(dummyVehicleType, createdType.TypeName);

        // 1.2 Create Party
        var createdParty = await _partyService.CreateAsync(new CreatePartyRequest(dummyPartyName, "UTC001", "Industrial Corridor", "9876543210"));
        Assert.NotNull(createdParty);
        Assert.Equal(dummyPartyName, createdParty.Name);

        // 1.3 Create Material
        var createdMaterial = await _materialService.CreateAsync(new CreateMaterialRequest(dummyMaterialName, "FA01", "High grade fly ash"));
        Assert.NotNull(createdMaterial);
        Assert.Equal(dummyMaterialName, createdMaterial.Name);

        // 1.4 Create Vehicle with standard Tare
        var createdVehicle = await _vehicleService.CreateAsync(new CreateVehicleRequest(dummyVehicleNo, createdType.Id, dummySecondWeightKg, "Standard company fleet"));
        Assert.NotNull(createdVehicle);
        Assert.Equal(dummyVehicleNo, createdVehicle.VehicleNumber);

        // Verify Masters queryability
        var parties = await _partyService.GetAllAsync();
        Assert.Contains(parties, p => p.Name == dummyPartyName);

        var materials = await _materialService.GetAllAsync();
        Assert.Contains(materials, m => m.Name == dummyMaterialName);

        var vehicles = await _vehicleService.GetAllAsync();
        Assert.Contains(vehicles, v => v.VehicleNumber == dummyVehicleNo);

        // =========================================================================
        // FEATURE 2: VEHICLE ENTRY F1 (First Entry - Gross Weight Capture)
        // =========================================================================
        var weighmentOpts = Options.Create(new WeighmentOptions { ManualTareEntry = true, SecondEntryCharges = true });
        var optionsMonitor = new TestOptionsMonitor<WeighmentOptions>(weighmentOpts.Value);

        var vehicleEntryVm = new VehicleEntryViewModel(
            _executor,
            _weighmentService,
            _vehicleService,
            _partyService,
            _materialService,
            _vehicleTypeService,
            _indicator,
            _cameraService,
            _printService,
            _permissions,
            _dialogs,
            _dispatcher,
            _loggerFactory.CreateLogger<VehicleEntryViewModel>(),
            navigationService: null,
            optionsMonitor: optionsMonitor);

        // Simulate operator navigating to Vehicle Entry
        await vehicleEntryVm.OnNavigatedToAsync(NavigationContext.Empty);

        // Verify initial state: F1 mode, authoritative reservation allocated
        Assert.True(vehicleEntryVm.IsF1Mode);
        Assert.Equal("F1", vehicleEntryVm.EntryModeText);
        Assert.NotNull(vehicleEntryVm.ActiveSlipNumber);
        Assert.StartsWith("WB-", vehicleEntryVm.ActiveSlipNumber);
        var allocatedTicketNo = vehicleEntryVm.ActiveSlipNumber;

        // Enter dummy values into Vehicle Entry form
        vehicleEntryVm.VehicleNumber = dummyVehicleNo;
        vehicleEntryVm.SelectedVehicleTypeName = dummyVehicleType;
        vehicleEntryVm.PartyName = dummyPartyName;
        vehicleEntryVm.MaterialName = dummyMaterialName;
        vehicleEntryVm.Charges = dummyCharges;
        vehicleEntryVm.GrossTareText = "G"; // Gross First

        // Operator captures first weight
        vehicleEntryVm.WeightInput = dummyFirstWeightKg.ToString("F0", CultureInfo.InvariantCulture);

        // Verify live HUD computations
        Assert.Equal(dummyFirstWeightKg, vehicleEntryVm.DisplayGrossWeightKg);
        Assert.Equal(0m, vehicleEntryVm.DisplayTareWeightKg);
        Assert.Equal(0m, vehicleEntryVm.DisplayNetWeightKg);

        // Operator submits F1 First Entry (F5)
        Assert.True(vehicleEntryVm.SubmitWorkflowCommand.CanExecute(null));
        await vehicleEntryVm.SubmitWorkflowAsync();

        // Verify First Entry transitioned to AwaitingSecondWeight and is in pending queue
        var pendingRecords = await _weighmentService.GetAwaitingSecondWeightAsync();
        Assert.Single(pendingRecords);
        var pendingRecord = pendingRecords[0];
        Assert.Equal(allocatedTicketNo, pendingRecord.SlipNumber);
        Assert.Equal(dummyVehicleNo, pendingRecord.VehicleNumber);
        Assert.Equal(dummyPartyName, pendingRecord.PartyName);
        Assert.Equal(dummyMaterialName, pendingRecord.MaterialName);
        Assert.Equal(dummyFirstWeightKg, pendingRecord.Gross?.Kilograms);
        Assert.Equal(WeighmentStatus.AwaitingSecondWeight, pendingRecord.Status);

        // =========================================================================
        // FEATURE 3: VEHICLE ENTRY F2 (Second Entry - Tare Capture & Completion)
        // =========================================================================
        // Operator switches to F2 mode
        vehicleEntryVm.EntryModeText = "F2";
        Assert.True(vehicleEntryVm.IsF2Mode);
        Assert.True(vehicleEntryVm.IsF2SearchActive);

        // Operator searches for pending ticket using Slip Number or Vehicle Number
        vehicleEntryVm.F2SearchKey = dummyVehicleNo;
        await vehicleEntryVm.SearchPendingSecondEntryAsync();

        // Verify ticket loaded into F2 context and historical fields are locked
        Assert.False(vehicleEntryVm.IsF2SearchActive);
        Assert.Equal(allocatedTicketNo, vehicleEntryVm.ActiveSlipNumber);
        Assert.Equal(dummyVehicleNo, vehicleEntryVm.VehicleNumber);
        Assert.Equal(dummyPartyName, vehicleEntryVm.PartyName);
        Assert.Equal(dummyMaterialName, vehicleEntryVm.MaterialName);
        Assert.Equal(dummyFirstWeightKg, vehicleEntryVm.DisplayGrossWeightKg);

        // Operator enters second weight (Tare)
        vehicleEntryVm.WeightInput = dummySecondWeightKg.ToString("F0", CultureInfo.InvariantCulture);

        // Verify live Net Weight calculation
        Assert.Equal(dummyFirstWeightKg, vehicleEntryVm.DisplayGrossWeightKg);
        Assert.Equal(dummySecondWeightKg, vehicleEntryVm.DisplayTareWeightKg);
        Assert.Equal(dummyNetWeightKg, vehicleEntryVm.DisplayNetWeightKg);

        // Operator submits Second Entry (F5) to complete transaction
        await vehicleEntryVm.SubmitWorkflowAsync();

        // Verify transaction is now Completed in database
        var completed = await _weighmentService.GetBySlipNumberAsync(allocatedTicketNo);
        Assert.NotNull(completed);
        Assert.Equal(WeighmentStatus.Completed, completed.Status);
        Assert.Equal(dummyFirstWeightKg, completed.Gross?.Kilograms);
        Assert.Equal(dummySecondWeightKg, completed.Tare?.Kilograms);
        Assert.Equal(dummyNetWeightKg, completed.NetWeightKg);
        Assert.Equal(dummyCharges, completed.Charges);
        Assert.NotNull(completed.CompletedAtUtc);

        // Verify pending queue is now empty
        var pendingAfter = await _weighmentService.GetAwaitingSecondWeightAsync();
        Assert.Empty(pendingAfter);

        // =========================================================================
        // FEATURE 4: DUPLICATE SLIP (Search & Verification)
        // =========================================================================
        var companyOpts = Options.Create(new CompanyOptions
        {
            CompanyName = "Apex WeighBridge Pvt Ltd",
            AddressLine1 = "Sector 12, Industrial Area"
        });

        var appInfo = new TestApplicationInfoService();
        var audit = new AuditLogger(_loggerFactory, appInfo);

        var duplicateSlipVm = new DuplicateSlipViewModel(
            new EfRepository<Weighment>(new WeighBridgeDbContext(_dbOptions)),
            _printService,
            _permissions,
            _dialogs,
            companyOpts,
            audit,
            _loggerFactory.CreateLogger<DuplicateSlipViewModel>());

        // Search by Ticket Number
        duplicateSlipVm.SearchTicketNumber = allocatedTicketNo;
        duplicateSlipVm.SearchTicketCommand.Execute(null);

        Assert.Single(duplicateSlipVm.SearchResults);
        var slipResult = duplicateSlipVm.SearchResults[0];
        Assert.Equal(allocatedTicketNo, slipResult.SlipNumber);
        Assert.Equal(dummyVehicleNo, slipResult.VehicleNumber);
        Assert.Equal(dummyPartyName, slipResult.PartyName);
        Assert.Equal(dummyMaterialName, slipResult.MaterialName);
        Assert.Equal(dummyFirstWeightKg, slipResult.GrossKg);
        Assert.Equal(dummySecondWeightKg, slipResult.TareKg);
        Assert.Equal(dummyNetWeightKg, slipResult.NetKg);

        // Also test searching by Vehicle Number
        duplicateSlipVm.SearchTicketNumber = string.Empty;
        duplicateSlipVm.SearchVehicleNumber = dummyVehicleNo;
        duplicateSlipVm.SearchVehicleCommand.Execute(null);
        Assert.Single(duplicateSlipVm.SearchResults);

        // =========================================================================
        // FEATURE 5: REPORTS & EXPORTS (Daily / Summary Report + CSV / Excel / PDF)
        // =========================================================================
        var filterParams = new ReportFilterParameters(
            StartDateLocal: DateTime.Today.AddDays(-1),
            EndDateLocal: DateTime.Today.AddDays(1));

        var reportDoc = await _reportService.BuildDocumentAsync(filterParams);
        Assert.NotNull(reportDoc);
        Assert.Single(reportDoc.Rows);
        Assert.Equal(allocatedTicketNo, reportDoc.Rows[0].SlipNumber);
        Assert.Equal(dummyVehicleNo, reportDoc.Rows[0].VehicleNumber);
        Assert.Equal(dummyPartyName, reportDoc.Rows[0].PartyName);
        Assert.Equal(dummyFirstWeightKg, reportDoc.Rows[0].GrossWeightKg);
        Assert.Equal(dummySecondWeightKg, reportDoc.Rows[0].TareWeightKg);
        Assert.Equal(dummyNetWeightKg, reportDoc.Rows[0].NetWeightKg);

        // Test Export to CSV
        var csvPath = Path.Combine(_root.Root, "e2e_report.csv");
        var csvResult = await _reportService.ExportAsync(reportDoc, ReportFormat.Csv, csvPath);
        Assert.True(csvResult.Succeeded);
        Assert.True(File.Exists(csvPath));
        var csvContent = await File.ReadAllTextAsync(csvPath);
        Assert.Contains(allocatedTicketNo, csvContent);
        Assert.Contains(dummyVehicleNo, csvContent);
        Assert.Contains(dummyPartyName, csvContent);

        // Test Export to Excel
        var excelPath = Path.Combine(_root.Root, "e2e_report.xlsx");
        var excelResult = await _reportService.ExportAsync(reportDoc, ReportFormat.Excel, excelPath);
        Assert.True(excelResult.Succeeded);
        Assert.True(File.Exists(excelPath));
        Assert.True(new FileInfo(excelPath).Length > 0);

        // Test Export to PDF
        var pdfPath = Path.Combine(_root.Root, "e2e_report.pdf");
        var pdfResult = await _reportService.ExportAsync(reportDoc, ReportFormat.Pdf, pdfPath);
        Assert.True(pdfResult.Succeeded);
        Assert.True(File.Exists(pdfPath));
        Assert.True(new FileInfo(pdfPath).Length > 0);

        // =========================================================================
        // FEATURE 6: DASHBOARD AGGREGATIONS
        // =========================================================================
        var dashboardVm = new DashboardViewModel(
            new EfRepository<Weighment>(new WeighBridgeDbContext(_dbOptions)),
            _indicator,
            _dispatcher,
            new StubNavigationService(),
            _loggerFactory.CreateLogger<DashboardViewModel>());

        await dashboardVm.OnNavigatedToAsync(NavigationContext.Empty);
        Assert.Equal(1, dashboardVm.TodaysCompleted);
        Assert.Equal(0, dashboardVm.CurrentlyWaiting);
    }

    #region Test Stubs

    private sealed class StubDialogService : IDialogService
    {
        public Task ShowInformationAsync(string title, string message, string? details = null) => Task.CompletedTask;
        public Task ShowSuccessAsync(string title, string message, string? details = null) => Task.CompletedTask;
        public Task ShowWarningAsync(string title, string message, string? details = null) => Task.CompletedTask;
        public Task ShowErrorAsync(string title, string message, string? details = null) => Task.CompletedTask;
        public Task<bool> ShowConfirmationAsync(string title, string message, string confirmText = "Confirm", string cancelText = "Cancel", bool isDestructive = false) => Task.FromResult(true);
        public Task ShowLoadingAsync(string message, Func<Task> operation) => operation();
        public Task ShowProgressAsync(string title, Func<IProgressReporter, Task> operation, bool isCancellable = false) => operation(new NullProgressReporter());
        public Task<bool> ShowLoginAsync() => Task.FromResult(true);
        public Task<string?> ShowSaveFileDialogAsync(string title, string defaultFileName, string filter, string? initialDirectory = null) => Task.FromResult<string?>(null);

        private sealed class NullProgressReporter : IProgressReporter
        {
            public CancellationToken CancellationToken => CancellationToken.None;
            public void ReportStatus(string status) { }
            public void ReportProgress(double percentage) { }
            public void Report(double percentage, string status) { }
            public void ReportIndeterminate(string status) { }
        }
    }

    private sealed class StubPrintService : IPrintService
    {
        public string Name => "StubPrinter";
        public Task<HealthResult> CheckAsync(CancellationToken cancellationToken = default) => Task.FromResult(HealthResult.Healthy("OK"));
        public Task<IReadOnlyList<string>> GetAvailablePrintersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(["Default Printer"]);
        public Task<string?> GetDefaultPrinterAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("Default Printer");
        public Task<PrintResult> PrintAsync(string documentKey, IReadOnlyDictionary<string, object?> data, string? printerName = null, int copies = 1, CancellationToken cancellationToken = default)
            => Task.FromResult(PrintResult.Success("STUB"));
    }

#pragma warning disable CS0067

    private sealed class StubIndicatorService : IWeightIndicatorService
    {
        public string Name => "StubIndicator";
        public ConnectionState State => ConnectionState.Connected;
        public WeightReading CurrentReading => new(0m, "kg", true, DateTime.UtcNow, WeightSource.Indicator);
        public event EventHandler<ConnectionState>? StateChanged;
        public event EventHandler<WeightReading>? ReadingReceived;
        public event EventHandler<DiagnosticDataChunk>? RawTelemetryReceived;
        public Task<HealthResult> CheckAsync(CancellationToken cancellationToken = default) => Task.FromResult(HealthResult.Healthy("OK"));
        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<WeightReading> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(CurrentReading);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubCameraService : ICameraService
    {
        public string Name => "StubCamera";
        public ConnectionState State => ConnectionState.Connected;
        public IReadOnlyList<string> ConfiguredDevices => [];
        public event EventHandler<ConnectionState>? StateChanged;
        public Task<HealthResult> CheckAsync(CancellationToken cancellationToken = default) => Task.FromResult(HealthResult.Healthy("OK"));
        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<string?> CaptureAsync(string deviceName, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<CameraCaptureResult> CaptureSnapshotAsync(string deviceName, string stage, string slipNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(CameraCaptureResult.Failed("Stub camera"));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubNavigationService : INavigationService
    {
        public ViewModelBase? CurrentViewModel => null;
        public bool CanGoBack => false;
        public bool CanGoForward => false;
        public event EventHandler<NavigatedEventArgs>? Navigated;
        public Task<bool> NavigateToAsync<TViewModel>(NavigationContext? context = null) where TViewModel : ViewModelBase => Task.FromResult(true);
        public Task<bool> NavigateToAsync(Type viewModelType, NavigationContext? context = null) => Task.FromResult(true);
        public Task<bool> GoBackAsync() => Task.FromResult(false);
        public Task<bool> GoForwardAsync() => Task.FromResult(false);
        public Task<bool> RefreshAsync() => Task.FromResult(true);
        public void ClearHistory() { }
    }

#pragma warning restore CS0067

    private sealed class TestOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    #endregion
}
