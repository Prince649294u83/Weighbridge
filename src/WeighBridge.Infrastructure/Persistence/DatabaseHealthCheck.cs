using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WeighBridge.Core.Abstractions;

namespace WeighBridge.Infrastructure.Persistence;

/// <summary>
/// Probes database connectivity for the shell status bar.
/// </summary>
public sealed class DatabaseHealthCheck(
    IDbContextFactory<WeighBridgeDbContext> contextFactory,
    ConnectionStringProvider connectionStrings,
    ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    private readonly IDbContextFactory<WeighBridgeDbContext> _contextFactory = contextFactory;
    private readonly ConnectionStringProvider _connectionStrings = connectionStrings;
    private readonly ILogger<DatabaseHealthCheck> _logger = logger;

    /// <inheritdoc />
    public string Name => "Database";

    /// <inheritdoc />
    public async Task<HealthResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using var context = await _contextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            var canConnect = await context.Database
                .CanConnectAsync(cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();

            return canConnect
                ? HealthResult.Healthy(_connectionStrings.Description, stopwatch.Elapsed)
                : HealthResult.Unreachable($"Cannot open {_connectionStrings.Description}");
        }
        catch (OperationCanceledException)
        {
            return HealthResult.Unreachable("Health check cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database health check failed");
            return HealthResult.Unreachable(ex.Message);
        }
    }
}
