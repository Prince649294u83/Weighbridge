using System.Linq.Expressions;
using WeighBridge.Domain.Common;

namespace WeighBridge.Core.Abstractions;

/// <summary>
/// Generic persistence contract for aggregate roots.
/// </summary>
/// <remarks>
/// Declared in Module 0.1 so ViewModels in later modules depend on this abstraction
/// rather than on EF Core. No SQL and no <c>DbContext</c> ever reaches a ViewModel.
/// </remarks>
public interface IRepository<TEntity>
    where TEntity : EntityBase, IAggregateRoot
{
    /// <summary>Returns the entity with the given key, or <c>null</c>.</summary>
    Task<TEntity?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Returns every entity. Use sparingly — prefer a filtered query.</summary>
    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the entities matching <paramref name="predicate"/>.</summary>
    Task<IReadOnlyList<TEntity>> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>Counts the entities matching an optional predicate.</summary>
    Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a new entity for insertion.</summary>
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>Stages an existing entity for update.</summary>
    void Update(TEntity entity);

    /// <summary>Stages an entity for removal.</summary>
    void Remove(TEntity entity);
}
