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
/// <para>
/// Failure stops startup. This used to be non-fatal, on the reasoning that a shell
/// reporting "Disconnected" is what an operator needs to see when a network share or a
/// file permission is wrong. That outcome became unreachable when login was placed before
/// the shell: authentication queries the database, so a failure here surfaced as an
/// unhandled query exception a moment later instead. The failure is now reported against
/// the database, with the path, by the caller.
/// </para>
/// <para>
/// The work runs once per process however many callers ask for it. Startup begins it
/// without waiting, and anything that needs the schema awaits the same task - a second
/// caller must not start a concurrent migration, because SQLite would then be migrated
/// twice through two connections.
/// </para>
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
    private readonly object _gate = new();

    private Task<DatabaseInitializationResult>? _initialization;

    /// <inheritdoc />
    public Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            // The first caller's cancellation token owns the work; a later caller joins the
            // run in progress rather than starting one of its own.
            return _initialization ??= InitializeCoreAsync(cancellationToken);
        }
    }

    private async Task<DatabaseInitializationResult> InitializeCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(_connectionStrings.DatabaseFilePath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // A zero-byte file is a valid empty SQLite database, so MigrateAsync below would
            // write a fresh schema over it and the login dialog would then offer first-run
            // administrator setup, on a terminal that has been in service for months, with
            // nothing on screen to say a database had been destroyed. The application never
            // leaves a zero-byte file at this path - SQLite writes the header on the first
            // write - so one here is damage, and migrating over it is what makes the damage
            // permanent and invisible.
            //
            // Only when the connection string is the one derived from this path. A site that
            // configures its own connection string points the provider somewhere else, and a
            // stale file left at the default path says nothing about the database in use.
            if (string.IsNullOrWhiteSpace(_options.ConnectionString)
                && File.Exists(_connectionStrings.DatabaseFilePath)
                && new FileInfo(_connectionStrings.DatabaseFilePath).Length == 0)
            {
                _logger.LogError(
                    "Database file {DatabasePath} is empty (zero bytes); refusing to migrate over it",
                    _connectionStrings.DatabaseFilePath);

                return DatabaseInitializationResult.Failure(
                    $"The database file is empty: {_connectionStrings.DatabaseFilePath}{Environment.NewLine}{Environment.NewLine}" +
                    "A zero-byte file is not a database this application wrote. Restore it from " +
                    "a backup, or delete it if this terminal is genuinely new - deleting an " +
                    "empty file loses nothing, and the application will then create a database.");
            }

            await using var context = await _contextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            var known = context.Database.GetMigrations().ToList();

            if (known.Count == 0)
            {
                // Reachable only if every migration is removed from the assembly. Kept so
                // that a build in that state still provisions a connectable database file.
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

            // No user account is seeded here. This used to create "admin" with a hash of a
            // password published in the repository, which is a fixed known credential on
            // every installation that has never had its password changed. The first
            // administrator is now collected by the login dialog's first-run setup mode
            // (IAuthenticationService.RequiresInitialSetupAsync), so the application ships
            // with no password at all.

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

            // The path belongs in the message, not only in the exception detail behind "Show
            // technical details": a site can have its database on a share or at a configured
            // location, and "file is not a database" is not actionable without knowing which
            // file is meant.
            return DatabaseInitializationResult.Failure(
                $"The database could not be opened: {_connectionStrings.DatabaseFilePath}{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                ex);
        }
    }
}
