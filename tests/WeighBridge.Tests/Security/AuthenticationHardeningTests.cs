using System.ComponentModel;
using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Application;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Common;
using WeighBridge.Domain.Security;
using WeighBridge.Services.Security;
using Xunit;

namespace WeighBridge.Tests.Security;

public sealed class AuthenticationHardeningTests
{
    private sealed class InMemoryUserRepository : IRepository<User>
    {
        public Dictionary<long, User> Store { get; } = [];
        private long _nextId = 1;

        public Task<User?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        {
            Store.TryGetValue(id, out var u);
            return Task.FromResult(u);
        }

        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<User>>(Store.Values.ToList());

        public Task<IReadOnlyList<User>> FindAsync(Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<User>>(Store.Values.AsQueryable().Where(predicate).ToList());

        public Task<IReadOnlyList<User>> ListRecentAsync(Expression<Func<User, bool>>? predicate = null, int? take = null, CancellationToken cancellationToken = default)
        {
            var query = Store.Values.AsQueryable();
            if (predicate != null) query = query.Where(predicate);
            query = query.OrderByDescending(x => x.Id);
            if (take.HasValue) query = query.Take(take.Value);
            return Task.FromResult<IReadOnlyList<User>>(query.ToList());
        }

        public Task<IReadOnlyList<User>> QueryAsync(
            Expression<Func<User, bool>>? predicate,
            Func<IQueryable<User>, IQueryable<User>>? transform,
            CancellationToken cancellationToken = default)
        {
            var query = Store.Values.AsQueryable();
            if (predicate != null) query = query.Where(predicate);
            if (transform != null) query = transform(query);
            return Task.FromResult<IReadOnlyList<User>>(query.ToList());
        }

        public Task<int> CountAsync(Expression<Func<User, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            int count = predicate == null ? Store.Count : Store.Values.AsQueryable().Count(predicate);
            return Task.FromResult(count);
        }

        public Task AddAsync(User entity, CancellationToken cancellationToken = default)
        {
            var idProp = typeof(EntityBase).GetProperty("Id");
            idProp?.SetValue(entity, _nextId++);
            Store[entity.Id] = entity;
            return Task.CompletedTask;
        }

        public void Update(User entity) { Store[entity.Id] = entity; }
        public void Remove(User entity) { Store.Remove(entity.Id); }
    }

    private sealed class TestUnitOfWork(InMemoryUserRepository repo) : IUnitOfWork
    {
        public IRepository<TEntity> Repository<TEntity>() where TEntity : EntityBase, IAggregateRoot
        {
            if (typeof(TEntity) == typeof(User))
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

    private sealed class TestAppLogger : IApplicationLogger
    {
        public string Category => "Test";
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

    private sealed class TestAuditLogger : IAuditLogger
    {
        public List<(string Action, string Entity, string Key, string Details)> Logs { get; } = [];
        public string Category => "TestAudit";
        public void Record(string action, string entity, string? entityId = null, string? details = null)
            => Logs.Add((action, entity, entityId ?? "", details ?? ""));
        public void RecordDenied(string action, string entity, string reason, string? entityId = null)
            => Logs.Add((action, entity, entityId ?? "", reason));
        public void RecordFailed(string action, string entity, string reason, string? entityId = null)
            => Logs.Add((action, entity, entityId ?? "", reason));
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

    private sealed class StubPermissionService : IPermissionService
    {
        public OperatorIdentity CurrentOperator { get; set; } = new("anon", "Anonymous", Roles.Unauthenticated);
        public event EventHandler<OperatorChangedEventArgs>? OperatorChanged { add { } remove { } }
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public AuthorizationResult Authorize(Permission permission) => AuthorizationResult.Allowed;
        public AuthorizationResult Authorize(object candidate) => AuthorizationResult.Allowed;
        public bool HasPermission(Permission permission) => true;
        public bool HasAllPermissions(params Permission[] permissions) => true;
        public bool HasAnyPermission(params Permission[] permissions) => true;
        public void SetOperator(OperatorIdentity identity) { CurrentOperator = identity; }
        public void SignOut() { CurrentOperator = new("anon", "Anonymous", Roles.Unauthenticated); }
    }

    [Fact]
    public async Task FailedSignIns_PersistInDatabase_AndTriggerLockout()
    {
        var repo = new InMemoryUserRepository();
        var options = Options.Create(new SecurityOptions { MaxFailedSignIns = 3, LockoutSeconds = 60 });
        var perm = new StubPermissionService();
        var audit = new TestAuditLogger();
        var auth = new AuthenticationService(() => new TestUnitOfWork(repo), perm, new TestAppLogger(), audit, options);

        var user = User.Create("operator1", "Operator One", auth.HashPassword("CorrectPass123!"), "Operator");
        await repo.AddAsync(user);

        // Attempt 1: Wrong password
        bool result1 = await auth.AuthenticateAsync("operator1", "Wrong1");
        Assert.False(result1);
        Assert.Equal(1, user.FailedAccessCount);
        Assert.Null(user.LockoutUntilUtc);

        // Attempt 2: Wrong password
        bool result2 = await auth.AuthenticateAsync("operator1", "Wrong2");
        Assert.False(result2);
        Assert.Equal(2, user.FailedAccessCount);
        Assert.Null(user.LockoutUntilUtc);

        // Attempt 3: Wrong password (Threshold 3 reached -> Lockout applied)
        bool result3 = await auth.AuthenticateAsync("operator1", "Wrong3");
        Assert.False(result3);
        Assert.Equal(3, user.FailedAccessCount);
        Assert.NotNull(user.LockoutUntilUtc);
        Assert.True(user.LockoutUntilUtc.Value > DateTime.UtcNow);

        // Attempt 4: Even correct password is now REFUSED during active lockout
        bool result4 = await auth.AuthenticateAsync("operator1", "CorrectPass123!");
        Assert.False(result4);
    }

    [Fact]
    public async Task ExpiredPassword_FlagsMustChangePassword_UponSuccessfulSignIn()
    {
        var repo = new InMemoryUserRepository();
        var options = Options.Create(new SecurityOptions { RequirePasswordChangeDays = 90 });
        var perm = new StubPermissionService();
        var audit = new TestAuditLogger();
        var auth = new AuthenticationService(() => new TestUnitOfWork(repo), perm, new TestAppLogger(), audit, options);

        var user = User.Create("operator2", "Operator Two", auth.HashPassword("OldPass123!"), "Operator");
        // Set password change date to 100 days ago
        var changedProp = typeof(User).GetProperty("PasswordChangedAtUtc");
        changedProp?.SetValue(user, DateTime.UtcNow.AddDays(-100));
        await repo.AddAsync(user);

        // Act
        bool result = await auth.AuthenticateAsync("operator2", "OldPass123!");

        // Assert
        Assert.True(result);
        Assert.True(user.MustChangePassword);
    }
}
