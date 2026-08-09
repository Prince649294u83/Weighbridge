using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore.Storage;
using WeighBridge.Core.Abstractions;
using WeighBridge.Domain.Common;
using WeighBridge.Infrastructure.Persistence;

namespace WeighBridge.Infrastructure.Repositories;

/// <summary>
/// EF Core unit of work. Owns one <see cref="WeighBridgeDbContext"/> and hands out
/// repositories that share its change tracker.
/// </summary>
public sealed class UnitOfWork(WeighBridgeDbContext context) : IUnitOfWork
{
    private readonly WeighBridgeDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly ConcurrentDictionary<Type, object> _repositories = new();

    private IDbContextTransaction? _transaction;
    private bool _disposed;

    /// <inheritdoc />
    public IRepository<TEntity> Repository<TEntity>()
        where TEntity : EntityBase, IAggregateRoot
        => (IRepository<TEntity>)_repositories.GetOrAdd(
            typeof(TEntity),
            _ => new EfRepository<TEntity>(_context));

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already active on this unit of work.");
        }

        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            throw new InvalidOperationException("No transaction is active on this unit of work.");
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await DisposeTransactionAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            return;
        }

        try
        {
            await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await DisposeTransactionAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await DisposeTransactionAsync().ConfigureAwait(false);
        await _context.DisposeAsync().ConfigureAwait(false);
    }

    private async Task DisposeTransactionAsync()
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.DisposeAsync().ConfigureAwait(false);
        _transaction = null;
    }
}
