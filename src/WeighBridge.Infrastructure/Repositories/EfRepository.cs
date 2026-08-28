using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Common;
using WeighBridge.Infrastructure.Persistence;

namespace WeighBridge.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IRepository{TEntity}"/>.
/// </summary>
/// <remarks>
/// Registered as an open generic, so a business module gets a working repository for
/// a new aggregate root the moment it maps one â€” no extra registration required.
/// Audit stamps are applied here so no caller has to remember them, attributed to the
/// signed-in operator rather than left blank.
/// </remarks>
public class EfRepository<TEntity>(WeighBridgeDbContext context, SignedInOperator? signedInOperator = null)
    : IRepository<TEntity>
    where TEntity : EntityBase, IAggregateRoot
{
    /// <summary>The shared context instance for the current unit of work.</summary>
    protected WeighBridgeDbContext Context { get; } = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>The tracked set for <typeparamref name="TEntity"/>.</summary>
    protected DbSet<TEntity> Set => Context.Set<TEntity>();

    /// <summary>Who to blame for changes made through this repository.</summary>
    protected string? OperatorName => signedInOperator?.UserName;

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
    public virtual async Task<IReadOnlyList<TEntity>> ListRecentAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        int? take = null,
        CancellationToken cancellationToken = default)
    {
        if (take is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(take), take, "Ask for at least one row, or for all of them.");
        }

        IQueryable<TEntity> query = Set.AsNoTracking();

        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        // Identity descending, not CreatedAtUtc: two rows inserted in the same millisecond
        // would otherwise come back in an arbitrary order, and "the latest weighment" would
        // not be stable between two calls.
        query = query.OrderByDescending(entity => entity.Id);

        if (take is { } limit)
        {
            query = query.Take(limit);
        }

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<TEntity>> QueryAsync(
        Expression<Func<TEntity, bool>>? predicate,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? transform,
        CancellationToken cancellationToken = default)
    {
        IQueryable<TEntity> query = Set.AsNoTracking();

        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        if (transform is not null)
        {
            query = transform(query);
        }

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
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

        // The service layer may already know better who acted; the repository only fills
        // the gap so attribution is never silently blank.
        entity.CreatedBy ??= OperatorName;
        await Set.AddAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual void Update(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.ModifiedAtUtc = DateTime.UtcNow;
        entity.ModifiedBy ??= OperatorName;
        Set.Update(entity);
    }

    /// <inheritdoc />
    public virtual void Remove(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        // Weighment records are legally significant: soft-delete whenever the entity
        // opts in, and only physically remove rows that do not. The soft-deleted row is
        // also deactivated: a record flagged deleted-but-active would contradict itself,
        // and the filtered unique indexes treat those two states differently.
        if (entity is ISoftDeletable soft)
        {
            soft.IsDeleted = true;
            soft.DeletedAtUtc = DateTime.UtcNow;
            soft.DeletedBy ??= OperatorName;

            if (entity is IDeactivatable deactivatable && deactivatable.IsActive)
            {
                deactivatable.Deactivate();
            }

            Set.Update(entity);
            return;
        }

        Set.Remove(entity);
    }
}
