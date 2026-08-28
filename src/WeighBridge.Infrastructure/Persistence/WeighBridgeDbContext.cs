using Microsoft.EntityFrameworkCore;

namespace WeighBridge.Infrastructure.Persistence;

/// <summary>
/// Entity Framework Core context for the WeighBridge database.
/// </summary>
/// <remarks>
/// No <c>DbSet</c> properties: every mapping arrives as an
/// <see cref="IEntityTypeConfiguration{TEntity}"/> discovered by
/// <see cref="OnModelCreating"/>, and repositories reach their entities through
/// <see cref="DbContext.Set{TEntity}"/>. A module therefore adds a table without
/// touching this file.
/// </remarks>
public class WeighBridgeDbContext(DbContextOptions<WeighBridgeDbContext> options) : DbContext(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        // Picks up every IEntityTypeConfiguration<> defined in this assembly. Today
        // there are none; later modules only need to add their configuration class.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WeighBridgeDbContext).Assembly);
    }
}
