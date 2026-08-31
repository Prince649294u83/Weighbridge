using System.ComponentModel;
using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Common;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;
using WeighBridge.Reporting.Services;
using Xunit;

namespace WeighBridge.Tests.Reporting;

public sealed class ReportingServiceHardeningTests
{
    private sealed class InMemoryRepository<T> : IRepository<T> where T : EntityBase, IAggregateRoot
    {
        public List<T> Items { get; } = [];

        public Task<T?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(x => x.Id == id));

        public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<T>>(Items.ToList());

        public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<T>>(Items.AsQueryable().Where(predicate).ToList());
        }

        public Task<IReadOnlyList<T>> ListRecentAsync(Expression<Func<T, bool>>? predicate = null, int? take = null, CancellationToken cancellationToken = default)
        {
            var query = Items.AsQueryable();
            if (predicate != null) query = query.Where(predicate);
            query = query.OrderByDescending(x => x.Id);
            if (take.HasValue) query = query.Take(take.Value);
            return Task.FromResult<IReadOnlyList<T>>(query.ToList());
        }

        public Task<IReadOnlyList<T>> QueryAsync(
            Expression<Func<T, bool>>? predicate,
            Func<IQueryable<T>, IQueryable<T>>? transform,
            CancellationToken cancellationToken = default)
        {
            var query = Items.AsQueryable();
            if (predicate != null) query = query.Where(predicate);
            if (transform != null) query = transform(query);
            return Task.FromResult<IReadOnlyList<T>>(query.ToList());
        }

        public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            int count = predicate == null ? Items.Count : Items.AsQueryable().Count(predicate);
            return Task.FromResult(count);
        }

        public Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            Items.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(T entity) { }
        public void Remove(T entity)
        {
            Items.Remove(entity);
        }
    }

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        private readonly InMemoryRepository<Weighment> _weighments;

        public TestUnitOfWork(InMemoryRepository<Weighment> weighments)
        {
            _weighments = weighments;
        }

        public IRepository<TEntity> Repository<TEntity>() where TEntity : EntityBase, IAggregateRoot
        {
            if (typeof(TEntity) == typeof(Weighment))
            {
                return (IRepository<TEntity>)(object)_weighments;
            }
            throw new NotSupportedException();
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubPermissionService(bool allowExport) : IPermissionService
    {
        public OperatorIdentity CurrentOperator => new("op1", "Operator", new Role("Admin", allowExport ? [Permissions.ReportsExport] : []));

        public event EventHandler<OperatorChangedEventArgs>? OperatorChanged;
        public event PropertyChangedEventHandler? PropertyChanged;

        public AuthorizationResult Authorize(Permission permission)
        {
            if (permission == Permissions.ReportsExport && !allowExport)
            {
                return AuthorizationResult.Denied(permission, "Export permission not granted.");
            }
            return AuthorizationResult.Allowed;
        }

        public AuthorizationResult Authorize(object candidate) => AuthorizationResult.Allowed;
        public bool HasPermission(Permission permission) => allowExport;
        public bool HasAllPermissions(params Permission[] permissions) => allowExport;
        public bool HasAnyPermission(params Permission[] permissions) => allowExport;
        public void SetOperator(OperatorIdentity identity) { }
        public void SignOut() { }
    }

    [Theory]
    [InlineData("=cmd|'/C calc'!A0", "'=cmd|'/C calc'!A0")]
    [InlineData("+SUM(A1:A10)", "'+SUM(A1:A10)")]
    [InlineData("-2+3*cmd|' /C calc'!A0", "'-2+3*cmd|' /C calc'!A0")]
    [InlineData("@SUM(1,2)", "\"'@SUM(1,2)\"")]
    [InlineData("\tTabInjection", "'\tTabInjection")]
    [InlineData("Standard, Text", "\"Standard, Text\"")]
    [InlineData("Quote \"Text\"", "\"Quote \"\"Text\"\"\"")]
    public void EscapeCsv_Neutralizes_FormulaInjectionAndSpecialChars(string input, string expected)
    {
        string escaped = CsvReportService.EscapeCsv(input);
        Assert.Equal(expected, escaped);
    }

    [Fact]
    public async Task GenerateAsync_DailyReport_EnforcesMaxRowsLimit()
    {
        var weighmentsRepo = new InMemoryRepository<Weighment>();
        long nextId = 1;

        for (int i = 1; i <= 10; i++)
        {
            var w = Weighment.Open($"MH12AB{i:D4}", WeighmentMode.GrossFirst, $"Party {i}", "Sand");
            var idProp = typeof(EntityBase).GetProperty("Id");
            idProp?.SetValue(w, nextId++);
            w.AssignSlipNumber();
            w.RecordFirstWeight(new WeightCapture(20000m + i * 100, DateTime.UtcNow, WeightSource.Indicator));
            w.RecordSecondWeight(new WeightCapture(10000m, DateTime.UtcNow, WeightSource.Indicator));
            weighmentsRepo.Items.Add(w);
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "WeighBridgeReportsTest_" + Guid.NewGuid());
        var options = Options.Create(new ReportingOptions { MaxRowsPerReport = 5, OutputDirectory = tempDir });
        var perm = new StubPermissionService(true);
        var service = new CsvReportService(() => new TestUnitOfWork(weighmentsRepo), perm, options, NullLogger<CsvReportService>.Instance);

        var outputPath = Path.Combine(tempDir, "DailyReportTest.csv");

        var result = await service.GenerateAsync(
            CsvReportService.DailyReportKey,
            new Dictionary<string, object?> { ["StartDate"] = DateTime.Today.AddDays(-1), ["EndDate"] = DateTime.Today.AddDays(1) },
            ReportFormat.Csv,
            outputPath);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(outputPath));

        var lines = await File.ReadAllLinesAsync(outputPath);
        // Header (1) + 5 data rows = 6 lines
        Assert.Equal(6, lines.Length);

        // Clean up
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task GenerateAsync_SummaryReport_AggregatesByPartyAndMaterial()
    {
        var weighmentsRepo = new InMemoryRepository<Weighment>();
        long nextId = 1;

        // 3 for Party A / Steel
        for (int i = 1; i <= 3; i++)
        {
            var w = Weighment.Open("MH12AB0001", WeighmentMode.GrossFirst, "Party A", "Steel", charges: 100m);
            var idProp = typeof(EntityBase).GetProperty("Id");
            idProp?.SetValue(w, nextId++);
            w.AssignSlipNumber();
            w.RecordFirstWeight(new WeightCapture(30000m, DateTime.UtcNow, WeightSource.Indicator));
            w.RecordSecondWeight(new WeightCapture(10000m, DateTime.UtcNow, WeightSource.Indicator));
            weighmentsRepo.Items.Add(w);
        }

        // 2 for Party B / Coal
        for (int i = 1; i <= 2; i++)
        {
            var w = Weighment.Open("MH12AB0002", WeighmentMode.GrossFirst, "Party B", "Coal", charges: 200m);
            var idProp = typeof(EntityBase).GetProperty("Id");
            idProp?.SetValue(w, nextId++);
            w.AssignSlipNumber();
            w.RecordFirstWeight(new WeightCapture(40000m, DateTime.UtcNow, WeightSource.Indicator));
            w.RecordSecondWeight(new WeightCapture(15000m, DateTime.UtcNow, WeightSource.Indicator));
            weighmentsRepo.Items.Add(w);
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "WeighBridgeSummaryReportTest_" + Guid.NewGuid());
        var options = Options.Create(new ReportingOptions { MaxRowsPerReport = 100, OutputDirectory = tempDir });
        var perm = new StubPermissionService(true);
        var service = new CsvReportService(() => new TestUnitOfWork(weighmentsRepo), perm, options, NullLogger<CsvReportService>.Instance);

        var outputPath = Path.Combine(tempDir, "SummaryReportTest.csv");

        var result = await service.GenerateAsync(
            CsvReportService.SummaryReportKey,
            new Dictionary<string, object?> { ["StartDate"] = DateTime.Today.AddDays(-1), ["EndDate"] = DateTime.Today.AddDays(1) },
            ReportFormat.Csv,
            outputPath);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(outputPath));

        var lines = await File.ReadAllLinesAsync(outputPath);
        Assert.Contains(lines, l => l.StartsWith("Party A,Steel,3,90000.0,30000.0,60000.0,300.00"));
        Assert.Contains(lines, l => l.StartsWith("Party B,Coal,2,80000.0,30000.0,50000.0,400.00"));
        Assert.Contains(lines, l => l.StartsWith("Total Trips,5"));

        // Clean up
        Directory.Delete(tempDir, true);
    }
}
