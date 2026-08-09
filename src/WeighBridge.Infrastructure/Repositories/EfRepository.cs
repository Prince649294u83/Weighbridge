using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using WeighBridge.Core.Abstractions;
using WeighBridge.Domain.Common;
using WeighBridge.Infrastructure.Persistence;

namespace WeighBridge.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IRepository{TEntity}"/>.
/// </summary>
/// <remarks>
/// Registered as an open generic, so a business module gets a working repository for
/// a new aggregate root the moment it maps one — no extra registration required.
/// Audit stamps are applied here so no caller has to remember them.
/// </remarks>
public class EfRepository<TEntity>(WeighBridgeDbContext context) : IRepository<TEntity>
    where TEntity : EntityBase, IAggregateRoot
{
    /// <summary>The shared context instance for the current unit of work.</summary>
    protected WeighBridgeDbContext Context { get; } = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>The tracked set for <typeparamref name="TEntity"/>.</summary>
    protected DbSet<TEntity> Set => Context.Set<TEntity>();

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => await Set.FindAsync([id], cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
        => await Set.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<TEntity>> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        return await Set.AsNoTracking().Where(predicate).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
        => predicate is null
            ? await Set.CountAsync(cancellationToken).ConfigureAwait(false)
            : await Set.CountAsync(predicate, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.CreatedAtUtc = DateTime.UtcNow;
        await Set.AddAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual void Update(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.ModifiedAtUtc = DateTime.UtcNow;
        Set.Update(entity);
    }

    /// <inheritdoc />
    public virtual void Remove(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        // Weighment records are legally significant: soft-delete whenever the entity
        // opts in, and only physically remove rows that do not.
        if (entity is ISoftDeletable soft)
        {
            soft.IsDeleted = true;
            soft.DeletedAtUtc = DateTime.UtcNow;
            Set.Update(entity);
            return;
        }

        Set.Remove(entity);
    }
}
