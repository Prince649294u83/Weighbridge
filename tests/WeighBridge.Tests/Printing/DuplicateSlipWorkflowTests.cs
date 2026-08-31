using System.ComponentModel;
using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.App.ViewModels;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Logging;
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

        public event EventHandler<OperatorChangedEventArgs>? OperatorChanged;
        public event PropertyChangedEventHandler? PropertyChanged;

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

        var vm = new DuplicateSlipViewModel(repo, printService, companyOptions, audit, NullLogger<DuplicateSlipViewModel>.Instance);

        // Open weighment at Created status
        var created = Weighment.Open("MH12AB0001", WeighmentMode.GrossFirst, "Party A");
        await repo.AddAsync(created);

        var summary = WeighmentSummary.From(created);

        // Act
        vm.ReprintCommand.Execute(summary);
        await Task.Delay(100);

        // Assert
        Assert.True(vm.HasStatus);
        Assert.Contains("Only completed weighments can be reprinted", vm.StatusMessage);
    }

    [Fact]
    public async Task DuplicateSlip_FailingPrinter_LeavesCompletedTransactionUnchanged()
    {
        var repo = new InMemoryWeighmentRepository();
        var audit = new TestAuditLogger();
        var companyOptions = Options.Create(new CompanyOptions { CompanyName = "Test Co" });
        var mockFailingPrintService = new MockFailingPrintService();

        var vm = new DuplicateSlipViewModel(repo, mockFailingPrintService, companyOptions, audit, NullLogger<DuplicateSlipViewModel>.Instance);

        // Create completed weighment
        var weighment = Weighment.Open("MH12AB0002", WeighmentMode.GrossFirst, "Party B", "Coal");
        await repo.AddAsync(weighment);
        weighment.RecordFirstWeight(new WeightCapture(30000.0m, DateTime.UtcNow, WeightSource.Indicator));
        weighment.RecordSecondWeight(new WeightCapture(10000.0m, DateTime.UtcNow, WeightSource.Indicator));
        repo.Update(weighment);

        Assert.Equal(WeighmentStatus.Completed, weighment.Status);
        var summary = WeighmentSummary.From(weighment);

        // Act
        vm.ReprintCommand.Execute(summary);
        await Task.Delay(100);

        // Assert
        Assert.True(vm.HasStatus);
        Assert.Equal(WeighBridge.App.Controls.BadgeSeverity.Danger, vm.StatusSeverity);

        // Critical Invariant: Completed weighment remains Completed
        var postCheck = await repo.GetByIdAsync(weighment.Id);
        Assert.NotNull(postCheck);
        Assert.Equal(WeighmentStatus.Completed, postCheck.Status);
        Assert.Equal(20000.0m, postCheck.NetWeightKg);

        // Audit log records FAILURE outcome
        Assert.Contains(audit.Logs, log => log.Action == "Reprint" && log.Details.Contains("FAILURE"));
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
}
