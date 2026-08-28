using Microsoft.Extensions.Logging;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Logging;
using WeighBridge.Infrastructure.Persistence.Auditing;

namespace WeighBridge.Infrastructure.Persistence.Auditing;

/// <summary>
/// Writes audit records into the database, one short-lived unit of work per record.
/// </summary>
/// <remarks>
/// <para>
/// A fresh context per write keeps this singleton free of the change tracker the report
/// service was once caught pinning: nothing accumulates here across the life of the
/// process. Failures are counted and reported through logging — never thrown — because
/// an unavailable database must not fail the operator's command that is being audited.
/// </para>
/// </remarks>
public sealed class DatabaseAuditStore(
    Func<IUnitOfWork> unitOfWork,
    ILogger<DatabaseAuditStore> logger) : IAuditStore
{
    private int _consecutiveFailures;

    /// <inheritdoc />
    public async ValueTask WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = unitOfWork();

            await scope.Repository<AuditEntry>().AddAsync(
                AuditEntry.Create(
                    record.OccurredAtUtc,
                    record.OperatorName,
                    record.Module,
                    record.Action,
                    record.Outcome,
                    record.Entity,
                    record.EntityId,
                    record.Details,
                    record.CorrelationId),
                cancellationToken).ConfigureAwait(false);

            await scope.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (Interlocked.Exchange(ref _consecutiveFailures, 0) > 0)
            {
                logger.LogInformation("Audit store recovered; persisting records again.");
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown cancellation: the file log carries the record either way.
        }
        catch (Exception exception)
        {
            var failures = Interlocked.Increment(ref _consecutiveFailures);

            // Report the first failure and then every fiftieth, so a broken store is loud
            // without a failed write turning into thousands of log lines an hour.
            if (failures == 1 || failures % 50 == 0)
            {
                logger.LogWarning(
                    exception,
                    "Audit write #{Failure} failed for action {Action}; the record remains in the file log.",
                    failures,
                    record.Action);
            }
        }
    }
}
