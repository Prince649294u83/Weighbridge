using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Infrastructure.Persistence;
using WeighBridge.Infrastructure.Repositories;

namespace WeighBridge.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the persistence layer.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Adds the EF Core context factory, the generic repository, the unit of work and
    /// the database health check.
    /// </summary>
    /// <remarks>
    /// A <see cref="IDbContextFactory{TContext}"/> is used rather than a scoped
    /// context: a desktop application has no per-request scope, so each operation
    /// creates and disposes its own short-lived context.
    /// </remarks>
    public static IServiceCollection AddWeighBridgeInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ConnectionStringProvider>();

        services.AddDbContextFactory<WeighBridgeDbContext>((provider, builder) =>
        {
            var connectionStrings = provider.GetRequiredService<ConnectionStringProvider>();
            var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            builder.UseSqlite(
                connectionStrings.ConnectionString,
                sqlite =>
                {
                    sqlite.MigrationsAssembly(typeof(WeighBridgeDbContext).Assembly.FullName);
                    sqlite.CommandTimeout(options.CommandTimeoutSeconds);
                });

            builder.UseLoggerFactory(provider.GetRequiredService<ILoggerFactory>());

            if (options.EnableSensitiveDataLogging)
            {
                builder.EnableSensitiveDataLogging();
                builder.EnableDetailedErrors();
            }
        });

        // Transient context for the unit of work so each UoW owns its own tracker.
        services.AddTransient(provider =>
            provider.GetRequiredService<IDbContextFactory<WeighBridgeDbContext>>().CreateDbContext());

        services.AddTransient<IUnitOfWork, UnitOfWork>();
        services.AddTransient(typeof(IRepository<>), typeof(EfRepository<>));

        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<DatabaseHealthCheck>();

        return services;
    }
}
