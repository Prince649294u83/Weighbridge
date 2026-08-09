using Microsoft.EntityFrameworkCore;

namespace WeighBridge.Infrastructure.Persistence;

/// <summary>
/// Entity Framework Core context for the WeighBridge database.
/// </summary>
/// <remarks>
/// Module 0.1 intentionally maps no tables. The context exists so the connection,
/// migration pipeline and health check are in place; business modules will add their
/// own <see cref="IEntityTypeConfiguration{TEntity}"/> classes, which are discovered
/// automatically by <see cref="OnModelCreating"/>.
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
