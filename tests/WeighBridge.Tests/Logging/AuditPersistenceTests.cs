using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Logging;
using WeighBridge.Domain.Common;
using WeighBridge.Infrastructure.Persistence.Auditing;
using Xunit;

namespace WeighBridge.Tests.Logging;

public sealed class AuditPersistenceTests
{
    private sealed class InMemoryAuditRepository : IRepository<AuditEntry>
    {
        public List<AuditEntry> Items { get; } = [];
        private long _nextId = 1;

        public Task<AuditEntry?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(x => x.Id == id));

        public Task<IReadOnlyList<AuditEntry>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AuditEntry>>(Items.ToList());

        public Task<IReadOnlyList<AuditEntry>> FindAsync(Expression<Func<AuditEntry, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AuditEntry>>(Items.AsQueryable().Where(predicate).ToList());

        public Task<IReadOnlyList<AuditEntry>> ListRecentAsync(Expression<Func<AuditEntry, bool>>? predicate = null, int? take = null, CancellationToken cancellationToken = default)
        {
            var query = Items.AsQueryable();
            if (predicate != null) query = query.Where(predicate);
            query = query.OrderByDescending(x => x.Id);
            if (take.HasValue) query = query.Take(take.Value);
            return Task.FromResult<IReadOnlyList<AuditEntry>>(query.ToList());
        }

        public Task<IReadOnlyList<AuditEntry>> QueryAsync(
            Expression<Func<AuditEntry, bool>>? predicate,
            Func<IQueryable<AuditEntry>, IQueryable<AuditEntry>>? transform,
            CancellationToken cancellationToken = default)
        {
            var query = Items.AsQueryable();
            if (predicate != null) query = query.Where(predicate);
            if (transform != null) query = transform(query);
            return Task.FromResult<IReadOnlyList<AuditEntry>>(query.ToList());
        }

        public Task<int> CountAsync(Expression<Func<AuditEntry, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            int count = predicate == null ? Items.Count : Items.AsQueryable().Count(predicate);
            return Task.FromResult(count);
        }

        public Task AddAsync(AuditEntry entity, CancellationToken cancellationToken = default)
        {
            var idProp = typeof(EntityBase).GetProperty("Id");
            idProp?.SetValue(entity, _nextId++);
            Items.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(AuditEntry entity) { }
        public void Remove(AuditEntry entity) { Items.Remove(entity); }
    }

    private sealed class TestUnitOfWork(InMemoryAuditRepository repo) : IUnitOfWork
    {
        public IRepository<TEntity> Repository<TEntity>() where TEntity : EntityBase, IAggregateRoot
        {
            if (typeof(TEntity) == typeof(AuditEntry))
            {
                return (IRepository<TEntity>)(object)repo;
            }
            throw new NotSupportedException();
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task DatabaseAuditStore_WritesAuditRecord_WithSanitizedCredentials()
    {
        var repo = new InMemoryAuditRepository();
        var store = new DatabaseAuditStore(() => new TestUnitOfWork(repo), NullLogger<DatabaseAuditStore>.Instance);

        var record = new AuditRecord(
            DateTime.UtcNow,
            "super_operator",
            "Settings",
            AuditActions.SettingsChanged,
            AuditOutcomes.Success,
            "Security",
            "PasswordKey",
            "Changed Password: Password=MyPlainPassword123, ApiKey=secret_token_abc",
            Guid.NewGuid().ToString("N"));

        // Act
        await store.WriteAsync(record);

        // Assert
        Assert.Single(repo.Items);
        var entry = repo.Items[0];
        Assert.Equal(AuditActions.SettingsChanged, entry.Action);
        Assert.Equal(AuditOutcomes.Success, entry.Outcome);
        Assert.Equal("super_operator", entry.OperatorName);

        // Verify deterministic redaction
        Assert.DoesNotContain("MyPlainPassword123", entry.Details);
        Assert.DoesNotContain("secret_token_abc", entry.Details);
        Assert.Contains("Password=***REDACTED***", entry.Details);
        Assert.Contains("ApiKey=***REDACTED***", entry.Details);
    }

    [Fact]
    public async Task DatabaseAuditStore_Records_DeniedAndFailureOutcomes()
    {
        var repo = new InMemoryAuditRepository();
        var store = new DatabaseAuditStore(() => new TestUnitOfWork(repo), NullLogger<DatabaseAuditStore>.Instance);

        var denied = new AuditRecord(
            DateTime.UtcNow, "unauthorized_user", "Security", AuditActions.Login, AuditOutcomes.Denied, "User", "admin", "Account locked", Guid.NewGuid().ToString("N"));
        var failed = new AuditRecord(
            DateTime.UtcNow, "operator1", "Printing", AuditActions.Reprint, AuditOutcomes.Failure, "Weighment", "SLIP-01", "Printer offline", Guid.NewGuid().ToString("N"));

        await store.WriteAsync(denied);
        await store.WriteAsync(failed);

        Assert.Equal(2, repo.Items.Count);
        Assert.Contains(repo.Items, x => x.Outcome == AuditOutcomes.Denied && x.Action == AuditActions.Login);
        Assert.Contains(repo.Items, x => x.Outcome == AuditOutcomes.Failure && x.Action == AuditActions.Reprint);
    }
}
