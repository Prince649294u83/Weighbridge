using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Infrastructure.Persistence;

/// <summary>
/// Creates the database file and brings the schema up to date during startup.
/// </summary>
/// <remarks>
/// Failure is never fatal: the shell still opens and simply reports the database as
/// disconnected, which is what an operator needs to see when a network share or file
/// permission is wrong.
/// </remarks>
public sealed class DatabaseInitializer(
    IDbContextFactory<WeighBridgeDbContext> contextFactory,
    ConnectionStringProvider connectionStrings,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    private readonly IDbContextFactory<WeighBridgeDbContext> _contextFactory = contextFactory;
    private readonly ConnectionStringProvider _connectionStrings = connectionStrings;
    private readonly DatabaseOptions _options = options.Value;
    private readonly ILogger<DatabaseInitializer> _logger = logger;

    /// <inheritdoc />
    public async Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(_connectionStrings.DatabaseFilePath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var context = await _contextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            var known = context.Database.GetMigrations().ToList();

            if (known.Count == 0)
            {
                // No migrations exist yet (Module 0.1 maps no tables). EnsureCreated
                // provisions the empty database file so the connection is testable.
                await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Database ready at {DatabasePath}; no migrations are defined yet",
                    _connectionStrings.DatabaseFilePath);

                return DatabaseInitializationResult.Success("Database created; no migrations defined.");
            }

            if (!_options.ApplyMigrationsOnStartup)
            {
                _logger.LogInformation("Startup migrations are disabled by configuration");
                return DatabaseInitializationResult.Success("Migrations skipped by configuration.");
            }

            var pending = (await context.Database
                .GetPendingMigrationsAsync(cancellationToken)
                .ConfigureAwait(false)).ToList();

            if (pending.Count > 0)
            {
                _logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
                await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _logger.LogInformation("Database schema is up to date ({Count} migration(s) applied previously)", known.Count);
            }

            return DatabaseInitializationResult.Success(
                pending.Count > 0
                    ? $"Applied {pending.Count} migration(s)."
                    : "Schema up to date.",
                pending);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database initialisation failed for {DatabasePath}", _connectionStrings.DatabaseFilePath);
            return DatabaseInitializationResult.Failure($"Database initialisation failed: {ex.Message}", ex);
        }
    }
}
