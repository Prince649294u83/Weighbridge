using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using WeighBridge.Core.Application;

namespace WeighBridge.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add …</c> construct the context without starting the
/// WPF application.
/// </summary>
/// <remarks>
/// Design-time tooling runs outside the DI container, so the factory resolves the
/// same data-root location the running application uses.
/// </remarks>
public sealed class WeighBridgeDbContextFactory : IDesignTimeDbContextFactory<WeighBridgeDbContext>
{
    /// <inheritdoc />
    public WeighBridgeDbContext CreateDbContext(string[] args)
    {
        var paths = new ApplicationPaths();
        paths.EnsureCreated();

        var databasePath = Path.Combine(paths.DatabaseDirectory, "weighbridge.db");

        var options = new DbContextOptionsBuilder<WeighBridgeDbContext>()
            .UseSqlite(
                $"Data Source={databasePath}",
                sqlite => sqlite.MigrationsAssembly(typeof(WeighBridgeDbContext).Assembly.FullName))
            .Options;

        return new WeighBridgeDbContext(options);
    }
}
