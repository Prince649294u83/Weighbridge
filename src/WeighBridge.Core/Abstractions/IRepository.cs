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

    /// <summary>
    /// Returns the most recently created entities first, optionally filtered and capped.
    /// </summary>
    /// <remarks>
    /// The ordering is by identity descending, which is the order rows were inserted. Added
    /// because a module showing "the last twenty weighments" has no other way to say so:
    /// <see cref="GetAllAsync"/> would load every row ever recorded and sort it in memory,
    /// which is the same defect on a weighbridge with three years of history whether or not
    /// it is visible on the first day.
    /// </remarks>
    /// <param name="predicate">Optional filter.</param>
    /// <param name="take">Maximum rows to return; <c>null</c> for all of them.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<TEntity>> ListRecentAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        int? take = null,
        CancellationToken cancellationToken = default);

    /// <summary>Counts the entities matching an optional predicate.</summary>
    Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a caller-shaped query — ordering, paging, projection — and returns the rows.
    /// </summary>
    /// <remarks>
    /// Exists because "every report row ever recorded, in memory" is the same defect as
    /// <see cref="GetAllAsync"/> wearing a disguise. The transform is where a caller says
    /// <c>OrderBy(...).Skip(n).Take(limit)</c>, so the database does the work and the cap
    /// is real.
    /// </remarks>
    Task<IReadOnlyList<TEntity>> QueryAsync(
        Expression<Func<TEntity, bool>>? predicate,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? transform,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a new entity for insertion.</summary>
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>Stages an existing entity for update.</summary>
    void Update(TEntity entity);

    /// <summary>Stages an entity for removal.</summary>
    void Remove(TEntity entity);
}
