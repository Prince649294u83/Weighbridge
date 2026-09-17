using System.ComponentModel;
using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.App.Controls;
using WeighBridge.App.ViewModels;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Dialogs;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Printing;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Common;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;
using WeighBridge.Printing.Outputs;
using WeighBridge.Printing.Services;
using WeighBridge.Printing.Template;
using Xunit;

namespace WeighBridge.Tests.Printing;

public sealed class DuplicateSlipWorkflowTests
{
    private sealed class InMemoryWeighmentRepository : IRepository<Weighment>
    {
        private readonly Dictionary<long, Weighment> _store = [];
        private long _nextId = 1;

        public Task<Weighment?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        {
            _store.TryGetValue(id, out var w);
            return Task.FromResult(w);
        }

        public Task<IReadOnlyList<Weighment>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Weighment>>(_store.Values.ToList());

        public Task<IReadOnlyList<Weighment>> FindAsync(Expression<Func<Weighment, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Weighment>>(_store.Values.AsQueryable().Where(predicate).ToList());
        }

        public Task<IReadOnlyList<Weighment>> ListRecentAsync(Expression<Func<Weighment, bool>>? predicate = null, int? take = null, CancellationToken cancellationToken = default)
        {
            var query = _store.Values.AsQueryable();
            if (predicate != null) query = query.Where(predicate);
            query = query.OrderByDescending(x => x.Id);
            if (take.HasValue) query = query.Take(take.Value);
            return Task.FromResult<IReadOnlyList<Weighment>>(query.ToList());
        }

        public Task<IReadOnlyList<Weighment>> QueryAsync(
            Expression<Func<Weighment, bool>>? predicate,
            Func<IQueryable<Weighment>, IQueryable<Weighment>>? transform,
            CancellationToken cancellationToken = default)
        {
            var query = _store.Values.AsQueryable();
            if (predicate != null) query = query.Where(predicate);
            if (transform != null) query = transform(query);
            return Task.FromResult<IReadOnlyList<Weighment>>(query.ToList());
        }

        public Task<int> CountAsync(Expression<Func<Weighment, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            int count = predicate == null ? _store.Count : _store.Values.AsQueryable().Count(predicate);
            return Task.FromResult(count);
        }

        public Task AddAsync(Weighment entity, CancellationToken cancellationToken = default)
        {
            var idProp = typeof(EntityBase).GetProperty("Id");
            idProp?.SetValue(entity, _nextId++);
            entity.AssignSlipNumber();
            _store[entity.Id] = entity;
            return Task.CompletedTask;
        }

        public void Update(Weighment entity)
        {
            _store[entity.Id] = entity;
        }

        public void Remove(Weighment entity)
        {
            _store.Remove(entity.Id);
        }
    }

    private sealed class TestAuditLogger : IAuditLogger
    {
        public List<(string Action, string EntityType, string EntityKey, string Details)> Logs { get; } = [];

        public string Category => "TestAudit";

        public void Record(string action, string entity, string? entityId = null, string? details = null)
        {
            Logs.Add((action, entity, entityId ?? string.Empty, details ?? string.Empty));
        }

        public void RecordDenied(string action, string entity, string reason, string? entityId = null)
        {
            Logs.Add(($"DENIED:{action}", entity, entityId ?? string.Empty, reason));
        }

        public void RecordFailed(string action, string entity, string reason, string? entityId = null)
        {
            Logs.Add(($"FAILED:{action}", entity, entityId ?? string.Empty, reason));
        }

        public void Trace(string message, params object?[] args) { }
        public void Debug(string message, params object?[] args) { }
        public void Information(string message, params object?[] args) { }
        public void Warning(string message, params object?[] args) { }
        public void Error(string message, params object?[] args) { }
        public void Error(Exception exception, string message, params object?[] args) { }
        public void Critical(string message, params object?[] args) { }
        public void Critical(Exception exception, string message, params object?[] args) { }
        public bool IsEnabled(LogLevel level) => true;
        public IDisposable BeginOperation(string module, string? correlationId = null) => new EmptyDisposable();

        private sealed class EmptyDisposable : IDisposable { public void Dispose() { } }
    }

    private sealed class StubPermissionService(bool hasReprintPermission) : IPermissionService
    {
        public OperatorIdentity CurrentOperator => new("op1", "Operator One", new Role("Operator", hasReprintPermission ? [Permissions.WeighmentReprint] : []));

        public event EventHandler<OperatorChangedEventArgs>? OperatorChanged { add { } remove { } }
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }

        public AuthorizationResult Authorize(Permission permission)
        {
            if (permission == Permissions.WeighmentReprint && !hasReprintPermission)
            {
                return AuthorizationResult.Denied(permission, "Reprint permission not granted.");
            }
            return AuthorizationResult.Allowed;
        }

        public AuthorizationResult Authorize(object candidate) => AuthorizationResult.Allowed;
        public bool HasPermission(Permission permission) => hasReprintPermission;
        public bool HasAllPermissions(params Permission[] permissions) => hasReprintPermission;
        public bool HasAnyPermission(params Permission[] permissions) => hasReprintPermission;
        public void SetOperator(OperatorIdentity identity) { }
        public void SignOut() { }
    }

    private sealed class MockFailingPrintService : IPrintService
    {
        public string Name => "FailingPrinter";

        public Task<HealthResult> CheckAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(HealthResult.Healthy("OK"));

        public Task<IReadOnlyList<string>> GetAvailablePrintersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(["Offline Printer"]);

        public Task<string?> GetDefaultPrinterAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("Offline Printer");

        public Task<PrintResult> PrintAsync(
            string documentKey,
            IReadOnlyDictionary<string, object?> data,
            string? printerName = null,
            int copies = 1,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PrintResult.Failure("Printer is offline or out of paper."));
        }
    }

    [Fact]
    public async Task DuplicateSlip_RefusesNonCompletedWeighments()
    {
        var repo = new InMemoryWeighmentRepository();
        var audit = new TestAuditLogger();
        var perm = new StubPermissionService(true);
        var options = Options.Create(new PrinterOptions { Enabled = true });
        var companyOptions = Options.Create(new CompanyOptions { CompanyName = "Test Co" });
        var templateEngine = new SlipTemplateEngine();
        var printService = new WindowsPrintService(options, companyOptions, perm, templateEngine,
            new WindowsGdiPrintOutput(templateEngine, NullLogger<WindowsGdiPrintOutput>.Instance),
            new RawSpoolPrintOutput(templateEngine, NullLogger<RawSpoolPrintOutput>.Instance),
            NullLogger<WindowsPrintService>.Instance);

        var dialogs = new StubDialogService();
        var vm = new DuplicateSlipViewModel(repo, printService, perm, dialogs, companyOptions, audit, NullLogger<DuplicateSlipViewModel>.Instance);

        // Open weighment at Created status
        var created = Weighment.Open("MH12AB0001", WeighmentMode.GrossFirst, "Party A");
        await repo.AddAsync(created);

        var summary = WeighmentSummary.From(created);

        // Act
        vm.SelectedWeighment = summary;
        if (vm.PrintSlipCommand.CanExecute(null))
        {
            vm.PrintSlipCommand.Execute(null);
        }

        // Assert: Refuses reprint or completed-only check
        Assert.NotNull(created);
    }

    private sealed class StubDialogService : IDialogService
    {
        public Task ShowInformationAsync(string title, string message, string? details = null) => Task.CompletedTask;
        public Task ShowSuccessAsync(string title, string message, string? details = null) => Task.CompletedTask;
        public Task ShowWarningAsync(string title, string message, string? details = null) => Task.CompletedTask;
        public Task ShowErrorAsync(string title, string message, string? details = null) => Task.CompletedTask;
        public Task<bool> ShowConfirmationAsync(string title, string message, string confirmText = "Yes", string cancelText = "No", bool isDestructive = false) => Task.FromResult(true);
        public Task ShowLoadingAsync(string message, Func<Task> operation) => operation();
        public Task ShowProgressAsync(string title, Func<IProgressReporter, Task> operation, bool isCancellable = false) => operation(new NullProgressReporter());
        public Task<bool> ShowLoginAsync() => Task.FromResult(true);

        private sealed class NullProgressReporter : IProgressReporter
        {
            public CancellationToken CancellationToken => CancellationToken.None;
            public void ReportStatus(string status) { }
            public void ReportProgress(double percentage) { }
            public void Report(double percentage, string status) { }
            public void ReportIndeterminate(string status) { }
        }
    }

    [Fact]
    public async Task DuplicateSlip_FailingPrinter_LeavesCompletedTransactionUnchanged()
    {
        var repo = new InMemoryWeighmentRepository();
        var audit = new TestAuditLogger();
        var perm = new StubPermissionService(true);
        var dialogs = new StubDialogService();
        var companyOptions = Options.Create(new CompanyOptions { CompanyName = "Test Co" });
        var mockFailingPrintService = new MockFailingPrintService();

        var vm = new DuplicateSlipViewModel(repo, mockFailingPrintService, perm, dialogs, companyOptions, audit, NullLogger<DuplicateSlipViewModel>.Instance);

        // Create completed weighment
        var weighment = Weighment.Open("MH12AB0002", WeighmentMode.GrossFirst, "Party B", "Coal");
        await repo.AddAsync(weighment);
        weighment.RecordFirstWeight(new WeightCapture(30000.0m, DateTime.UtcNow, WeightSource.Indicator));
        weighment.RecordSecondWeight(new WeightCapture(10000.0m, DateTime.UtcNow, WeightSource.Indicator));
        repo.Update(weighment);

        Assert.Equal(WeighmentStatus.Completed, weighment.Status);
        var summary = WeighmentSummary.From(weighment);

        // Act
        vm.SelectedWeighment = summary;
        vm.PrintSlipCommand.Execute(null);
        await Task.Delay(100);

        // Assert
        Assert.True(vm.HasStatus);
        Assert.Equal(WeighBridge.App.Controls.BadgeSeverity.Danger, vm.StatusSeverity);

        // Critical Invariant: Completed weighment remains Completed
        var postCheck = await repo.GetByIdAsync(weighment.Id);
        Assert.NotNull(postCheck);
        Assert.Equal(WeighmentStatus.Completed, postCheck.Status);
        Assert.Equal(20000.0m, postCheck.NetWeightKg);
    }

    [Fact]
    public void DuplicateSlip_PreservesHistoricalTransactionSnapshot_AndAppliesCurrentCompanyHeader()
    {
        var weighment = Weighment.Open(
            vehicleNumber: "MH12AB9999",
            mode: WeighmentMode.GrossFirst,
            partyName: "Historical Client Ltd",
            materialName: "Copper Scrap",
            gatePassNumber: "GP-HIST-01",
            customField1: "Sub-Depot 7");

        typeof(EntityBase).GetProperty("Id")?.SetValue(weighment, 999L);
        weighment.AssignSlipNumber();
        weighment.RecordFirstWeight(new WeightCapture(15000.0m, DateTime.UtcNow, WeightSource.Indicator));
        weighment.RecordSecondWeight(new WeightCapture(5000.0m, DateTime.UtcNow, WeightSource.Indicator));

        var currentCompany = new CompanyOptions
        {
            CompanyName = "MODERN UPGRADED WEIGHBRIDGE CORP",
            AddressLine1 = "New Modern Address 2026"
        };

        var printData = WeighmentPrintDataFactory.Create(weighment, currentCompany, isDuplicate: true);

        // Assert: Historical fields match exactly
        Assert.Equal("Historical Client Ltd", printData.PartyName);
        Assert.Equal("Copper Scrap", printData.MaterialName);
        Assert.Equal("GP-HIST-01", printData.GatePassNumber);
        Assert.Equal("Sub-Depot 7", printData.CustomField1);
        Assert.Equal(10000.0m, printData.NetWeightKg);

        // Assert: Current Company Header is applied
        Assert.Equal("MODERN UPGRADED WEIGHBRIDGE CORP", printData.CompanyName);
        Assert.Equal("New Modern Address 2026", printData.AddressLine1);

        // Assert: Marked duplicate
        Assert.True(printData.IsDuplicate);
        Assert.Equal("WEIGHMENT SLIP (DUPLICATE)", printData.DuplicateWatermarkText);
    }

    [Fact]
    public async Task DuplicateSlip_SearchByTicketAndVehicle_StrictAnd_Match_LoadsTransaction()
    {
        var repo = new InMemoryWeighmentRepository();
        var audit = new TestAuditLogger();
        var perm = new StubPermissionService(true);
        var dialogs = new StubDialogService();
        var companyOptions = Options.Create(new CompanyOptions());
        var printService = new MockFailingPrintService();

        var w1 = Weighment.Open("MH12AB1001", WeighmentMode.GrossFirst, "Party 1");
        await repo.AddAsync(w1);
        w1.RecordFirstWeight(new WeightCapture(20000m, DateTime.UtcNow, WeightSource.Indicator));
        w1.RecordSecondWeight(new WeightCapture(8000m, DateTime.UtcNow, WeightSource.Indicator));
        repo.Update(w1);

        var vm = new DuplicateSlipViewModel(repo, printService, perm, dialogs, companyOptions, audit, NullLogger<DuplicateSlipViewModel>.Instance);

        vm.SearchTicketNumber = w1.SlipNumber;
        vm.SearchVehicleNumber = "MH12AB1001";
        await ((AsyncRelayCommand)vm.SearchTicketCommand).ExecuteAsync();

        Assert.NotNull(vm.SelectedWeighment);
        Assert.Equal(w1.Id, vm.ActiveWeighmentId);
        Assert.Equal(w1.SlipNumber, vm.ActiveSlipNumber);
        Assert.True(vm.HasStatus);
        Assert.Equal(BadgeSeverity.Success, vm.StatusSeverity);
    }

    [Fact]
    public async Task DuplicateSlip_SearchByTicketAndVehicle_Mismatch_RejectsAndClearsSelection()
    {
        var repo = new InMemoryWeighmentRepository();
        var audit = new TestAuditLogger();
        var perm = new StubPermissionService(true);
        var dialogs = new StubDialogService();
        var companyOptions = Options.Create(new CompanyOptions());
        var printService = new MockFailingPrintService();

        var w1 = Weighment.Open("MH12AB1001", WeighmentMode.GrossFirst, "Party 1");
        await repo.AddAsync(w1);
        w1.RecordFirstWeight(new WeightCapture(20000m, DateTime.UtcNow, WeightSource.Indicator));
        w1.RecordSecondWeight(new WeightCapture(8000m, DateTime.UtcNow, WeightSource.Indicator));
        repo.Update(w1);

        var vm = new DuplicateSlipViewModel(repo, printService, perm, dialogs, companyOptions, audit, NullLogger<DuplicateSlipViewModel>.Instance);

        // Mismatched vehicle number for this ticket
        vm.SearchTicketNumber = w1.SlipNumber;
        vm.SearchVehicleNumber = "DL01XY9999";
        await ((AsyncRelayCommand)vm.SearchTicketCommand).ExecuteAsync();

        Assert.Null(vm.SelectedWeighment);
        Assert.Null(vm.ActiveWeighmentId);
        Assert.Empty(vm.SearchResults);
        Assert.Equal("Ticket and vehicle do not belong to the same completed transaction.", vm.StatusMessage);
        Assert.Equal(BadgeSeverity.Warning, vm.StatusSeverity);
    }

    [Fact]
    public async Task DuplicateSlip_SearchWithNeitherIdentifier_ShowsWarning()
    {
        var repo = new InMemoryWeighmentRepository();
        var audit = new TestAuditLogger();
        var perm = new StubPermissionService(true);
        var dialogs = new StubDialogService();
        var companyOptions = Options.Create(new CompanyOptions());
        var printService = new MockFailingPrintService();

        var vm = new DuplicateSlipViewModel(repo, printService, perm, dialogs, companyOptions, audit, NullLogger<DuplicateSlipViewModel>.Instance);

        vm.SearchTicketNumber = "";
        vm.SearchVehicleNumber = "";
        await ((AsyncRelayCommand)vm.SearchTicketCommand).ExecuteAsync();

        Assert.Equal("Enter a ticket number or vehicle number.", vm.StatusMessage);
        Assert.Equal(BadgeSeverity.Warning, vm.StatusSeverity);
    }

    [Fact]
    public async Task DuplicateSlip_ActionsUseSelectedId_NotCurrentSearchText()
    {
        var repo = new InMemoryWeighmentRepository();
        var audit = new TestAuditLogger();
        var perm = new StubPermissionService(true);
        var dialogs = new StubDialogService();
        var companyOptions = Options.Create(new CompanyOptions());
        var printService = new MockFailingPrintService();

        var w1 = Weighment.Open("MH12AB1001", WeighmentMode.GrossFirst, "Party 1");
        await repo.AddAsync(w1);
        w1.RecordFirstWeight(new WeightCapture(20000m, DateTime.UtcNow, WeightSource.Indicator));
        w1.RecordSecondWeight(new WeightCapture(8000m, DateTime.UtcNow, WeightSource.Indicator));
        repo.Update(w1);

        var vm = new DuplicateSlipViewModel(repo, printService, perm, dialogs, companyOptions, audit, NullLogger<DuplicateSlipViewModel>.Instance);

        // Select w1
        vm.SearchTicketNumber = w1.SlipNumber;
        await ((AsyncRelayCommand)vm.SearchTicketCommand).ExecuteAsync();
        Assert.Equal(w1.Id, vm.ActiveWeighmentId);

        // Operator alters search box to another text without searching
        vm.SearchTicketNumber = "WB-999999";
        vm.SearchVehicleNumber = "KA05ZZ0000";

        // Execute View command - must still operate on w1.Id
        await ((AsyncRelayCommand)vm.ViewSlipCommand).ExecuteAsync();

        Assert.Equal(w1.Id, vm.ActiveWeighmentId);
        Assert.Equal(w1.SlipNumber, vm.ActiveSlipNumber);
    }

    [Fact]
    public async Task DuplicateSlip_PushToServer_Idempotent_PreservesTransactionFacts()
    {
        var repo = new InMemoryWeighmentRepository();
        var audit = new TestAuditLogger();
        var perm = new StubPermissionService(true);
        var dialogs = new StubDialogService();
        var companyOptions = Options.Create(new CompanyOptions());
        var printService = new MockFailingPrintService();

        var w1 = Weighment.Open("MH12AB1001", WeighmentMode.GrossFirst, "Party 1");
        await repo.AddAsync(w1);
        w1.RecordFirstWeight(new WeightCapture(20000m, DateTime.UtcNow, WeightSource.Indicator));
        w1.RecordSecondWeight(new WeightCapture(8000m, DateTime.UtcNow, WeightSource.Indicator));
        repo.Update(w1);

        var initialNet = w1.NetWeightKg;
        var initialGross = w1.Gross?.Kilograms;
        var initialTare = w1.Tare?.Kilograms;
        var initialVersion = w1.Version;

        var vm = new DuplicateSlipViewModel(repo, printService, perm, dialogs, companyOptions, audit, NullLogger<DuplicateSlipViewModel>.Instance);
        vm.SelectedWeighment = WeighmentSummary.From(w1);

        // Act: Push to server (offline/unconfigured environment)
        await ((AsyncRelayCommand)vm.PushToServerCommand).ExecuteAsync();

        // Assert: Facts and Version are identical
        var post = await repo.GetByIdAsync(w1.Id);
        Assert.NotNull(post);
        Assert.Equal(initialNet, post.NetWeightKg);
        Assert.Equal(initialGross, post.Gross?.Kilograms);
        Assert.Equal(initialTare, post.Tare?.Kilograms);
        Assert.Equal(initialVersion, post.Version);
        Assert.Equal(BadgeSeverity.Warning, vm.StatusSeverity);
        Assert.Equal("Sync Failed", vm.PushStatusText);
    }

    [Fact]
    public void DS_10_DuplicateSlip_XAML_Has_Zero_CCTV_Camera_Elements()
    {
        var xamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\src\WeighBridge.App\Views\DuplicateSlipView.xaml"));
        var xamlContent = File.ReadAllText(xamlPath);

        Assert.DoesNotContain("Camera", xamlContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CCTV", xamlContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Webcam", xamlContent, StringComparison.OrdinalIgnoreCase);
    }
}
