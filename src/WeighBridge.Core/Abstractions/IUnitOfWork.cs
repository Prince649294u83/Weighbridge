using WeighBridge.Domain.Common;

namespace WeighBridge.Core.Abstractions;

/// <summary>
/// Transactional boundary around one or more repositories.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>Returns the repository for <typeparamref name="TEntity"/>.</summary>
    IRepository<TEntity> Repository<TEntity>()
        where TEntity : EntityBase, IAggregateRoot;

    /// <summary>Persists all staged changes and returns the affected row count.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Begins an explicit transaction.</summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Commits the active transaction.</summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Rolls the active transaction back.</summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
