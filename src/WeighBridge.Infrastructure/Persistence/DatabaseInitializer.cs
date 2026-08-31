using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Infrastructure.Persistence;

/// <summary>
/// Creates the database file and brings the schema up to date during startup with robust diagnostic containment.
/// </summary>
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

            WriteMigrationDiagnosticFile(ex);

            return DatabaseInitializationResult.Failure(
                $"The database could not be opened: {_connectionStrings.DatabaseFilePath}{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                ex);
        }
    }

    private void WriteMigrationDiagnosticFile(Exception ex)
    {
        try
        {
            var dbPath = _connectionStrings.DatabaseFilePath;
            var directory = Path.GetDirectoryName(dbPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

            var diagFile = Path.Combine(directory, $"migration_failure_{DateTime.UtcNow:yyyyMMdd_HHmmss}.diag");
            var content = $"Timestamp (UTC): {DateTime.UtcNow:O}{Environment.NewLine}" +
                          $"Database Path: {dbPath}{Environment.NewLine}" +
                          $"Exception: {ex.GetType().FullName}{Environment.NewLine}" +
                          $"Message: {ex.Message}{Environment.NewLine}" +
                          $"Stack Trace:{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";

            File.WriteAllText(diagFile, content);
            _logger.LogInformation("Wrote migration diagnostic file to {Path}", diagFile);
        }
        catch
        {
            // Best-effort diagnostic write; must not mask original migration exception
        }
    }
}
