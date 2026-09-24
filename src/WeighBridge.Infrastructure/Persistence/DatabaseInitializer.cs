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

            if (context.Database.IsSqlite())
            {
                await context.Database.ExecuteSqlRawAsync(
                    "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 5000;",
                    cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("SQLite initialized with WAL mode, synchronous NORMAL, and busy_timeout 5000ms.");
            }

            var known = context.Database.GetMigrations().ToList();

            if (known.Count == 0)
            {
                await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Database ready at {DatabasePath}; no migrations are defined yet",
                    _connectionStrings.DatabaseFilePath);

                // Seed default accounts and master data even on a no-migration build, so the
                // first login is never stuck on an empty database.
                await SeedDefaultUsersIfEmptyAsync(context, cancellationToken).ConfigureAwait(false);
                await SeedDefaultMasterDataIfEmptyAsync(context, cancellationToken).ConfigureAwait(false);

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

            // User accounts are seeded first and independently — a failure in master data
            // (vehicle types, parties, materials) must never prevent the admin and operator
            // from being created.
            await SeedDefaultUsersIfEmptyAsync(context, cancellationToken).ConfigureAwait(false);
            await SeedDefaultMasterDataIfEmptyAsync(context, cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// Seeds the default administrator and operator accounts when the Users table is empty.
    /// </summary>
    /// <remarks>
    /// Separated from master data seeding so that a failure in vehicle types, parties, or
    /// materials can never silently prevent the admin and operator accounts from being created.
    /// Without these accounts the login screen is unusable on a fresh installation.
    /// </remarks>
    private async Task SeedDefaultUsersIfEmptyAsync(WeighBridgeDbContext context, CancellationToken cancellationToken)
    {
        try
        {
            var userSet = context.Set<Domain.Security.User>();
            if (await userSet.AnyAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            _logger.LogInformation("Seeding default administrator and operator accounts...");
            var defaultUsers = new[]
            {
                Domain.Security.User.Create(
                    "admin",
                    "System Administrator",
                    Core.Security.PasswordHasher.HashPassword("admin123"),
                    Core.Security.Roles.Administrator.Name),
                Domain.Security.User.Create(
                    "operator",
                    "Weighbridge Operator",
                    Core.Security.PasswordHasher.HashPassword("operator123"),
                    Core.Security.Roles.Operator.Name)
            };

            await userSet.AddRangeAsync(defaultUsers, cancellationToken).ConfigureAwait(false);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Seeded default administrator and operator accounts successfully");
        }
        catch (Exception ex)
        {
            // User seeding is critical — log as error, not warning, so it is visible in diagnostics.
            _logger.LogError(ex, "CRITICAL: Failed to seed default user accounts; login with default credentials will not work on this installation");
        }
    }

    /// <summary>
    /// Seeds sample master data (vehicle types, parties, materials, vehicles) when the
    /// respective tables are empty. Non-critical — a failure here leaves the application
    /// usable, just without sample data.
    /// </summary>
    private async Task SeedDefaultMasterDataIfEmptyAsync(WeighBridgeDbContext context, CancellationToken cancellationToken)
    {
        try
        {
            var vehicleTypeSet = context.Set<Domain.Masters.VehicleType>();
            if (!await vehicleTypeSet.AnyAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.LogInformation("Seeding default vehicle types...");
                var defaultVehicleTypes = new[]
                {
                    Domain.Masters.VehicleType.Create("4 Wheeler", "Small commercial or pickup vehicle"),
                    Domain.Masters.VehicleType.Create("6 Wheeler", "Standard medium truck"),
                    Domain.Masters.VehicleType.Create("10 Wheeler", "Heavy goods vehicle"),
                    Domain.Masters.VehicleType.Create("12 Wheeler", "Multi-axle heavy freight truck"),
                    Domain.Masters.VehicleType.Create("14 Wheeler", "Multi-axle heavy goods carrier"),
                    Domain.Masters.VehicleType.Create("Trailer", "Multi-axle articulated trailer / container carrier"),
                    Domain.Masters.VehicleType.Create("Dumper / Tipper", "Mining and aggregate dumper"),
                    Domain.Masters.VehicleType.Create("Tanker", "Liquid or bulk material tanker")
                };

                await vehicleTypeSet.AddRangeAsync(defaultVehicleTypes, cancellationToken).ConfigureAwait(false);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Seeded {Count} default vehicle types successfully", defaultVehicleTypes.Length);
            }

            var partySet = context.Set<Domain.Masters.Party>();
            if (!await partySet.AnyAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.LogInformation("Seeding sample parties...");
                var defaultParties = new[]
                {
                    Domain.Masters.Party.Create("UltraTech Cement Ltd", "CUST-001", "MIDC Industrial Area, Pune", "9876543210"),
                    Domain.Masters.Party.Create("Tata Steel Logistics", "CUST-002", "Jamshedpur Yard, Plot 4", "9876543211"),
                    Domain.Masters.Party.Create("Adani Enterprises", "CUST-003", "Mundra Port Logistics Hub", "9876543212"),
                    Domain.Masters.Party.Create("Northern Aggregates & Mines", "SUPP-001", "Quarry Highway 48", "9876543213")
                };

                await partySet.AddRangeAsync(defaultParties, cancellationToken).ConfigureAwait(false);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Seeded {Count} sample parties successfully", defaultParties.Length);
            }

            var materialSet = context.Set<Domain.Masters.Material>();
            if (!await materialSet.AnyAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.LogInformation("Seeding sample materials...");
                var defaultMaterials = new[]
                {
                    Domain.Masters.Material.Create("Iron Ore (Fine)", "MAT-001", "High grade iron ore fines"),
                    Domain.Masters.Material.Create("Coal (Thermal)", "MAT-002", "Imported Indonesian thermal coal"),
                    Domain.Masters.Material.Create("M-Sand (Manufactured Sand)", "MAT-003", "Crushed aggregate construction sand"),
                    Domain.Masters.Material.Create("Cement (OPC 53)", "MAT-004", "Ordinary Portland Cement Grade 53"),
                    Domain.Masters.Material.Create("Fly Ash", "MAT-005", "Class F thermal fly ash")
                };

                await materialSet.AddRangeAsync(defaultMaterials, cancellationToken).ConfigureAwait(false);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Seeded {Count} sample materials successfully", defaultMaterials.Length);
            }

            var vehicleSet = context.Set<Domain.Masters.Vehicle>();
            if (!await vehicleSet.AnyAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.LogInformation("Seeding sample vehicles with standard tare weights...");
                var vehicleTypeSet2 = context.Set<Domain.Masters.VehicleType>();
                var wheel10 = await vehicleTypeSet2.FirstOrDefaultAsync(t => t.TypeName == "10 Wheeler", cancellationToken).ConfigureAwait(false);
                var wheel12 = await vehicleTypeSet2.FirstOrDefaultAsync(t => t.TypeName == "12 Wheeler", cancellationToken).ConfigureAwait(false);
                var wheel6 = await vehicleTypeSet2.FirstOrDefaultAsync(t => t.TypeName == "6 Wheeler", cancellationToken).ConfigureAwait(false);
                var trailer = await vehicleTypeSet2.FirstOrDefaultAsync(t => t.TypeName == "Trailer", cancellationToken).ConfigureAwait(false);

                var defaultVehicles = new[]
                {
                    Domain.Masters.Vehicle.Create("MH14AZ7777", wheel10?.Id, 12400m, "Tata Signa 2823 Tipper - Standard Tare 12,400 kg"),
                    Domain.Masters.Vehicle.Create("MH12CD5678", wheel12?.Id, 14200m, "Ashok Leyland 3520 Multi-Axle - Standard Tare 14,200 kg"),
                    Domain.Masters.Vehicle.Create("KA04EF1234", wheel6?.Id, 9800m, "Eicher Pro 3019 - Standard Tare 9,800 kg"),
                    Domain.Masters.Vehicle.Create("DL01AB9999", trailer?.Id, 16500m, "BharatBenz 4028T Articulated - Standard Tare 16,500 kg")
                };

                await vehicleSet.AddRangeAsync(defaultVehicles, cancellationToken).ConfigureAwait(false);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Seeded {Count} sample vehicles successfully", defaultVehicles.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to seed default master data; application will continue with user-created masters");
        }
    }
}
