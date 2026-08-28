namespace WeighBridge.Core.Logging;

/// <summary>
/// One row of the persistent audit trail, ready for storage.
/// </summary>
/// <param name="OccurredAtUtc">When the audited thing happened.</param>
/// <param name="OperatorName">The authenticated operator responsible, when known.</param>
/// <param name="Module">The application module the action came from, when ambient scope knows it.</param>
/// <param name="Action">What was done — <c>SignedIn</c>, <c>WeighmentCompleted</c>, <c>UserCreated</c>.</param>
/// <param name="Outcome"><c>Succeeded</c>, <c>Denied</c> or <c>Failed</c>.</param>
/// <param name="Entity">What it was done to — <c>User</c>, <c>Weighment</c>, <c>Vehicle</c>.</param>
/// <param name="EntityId">Which one, as text — slip number, username, row id.</param>
/// <param name="Details">Anything else worth keeping. Never secrets or passwords.</param>
/// <param name="CorrelationId">Ties the row to every log line written for the same operation.</param>
public sealed record AuditRecord(
    DateTime OccurredAtUtc,
    string? OperatorName,
    string? Module,
    string Action,
    string Outcome,
    string? Entity,
    string? EntityId,
    string? Details,
    string? CorrelationId);

/// <summary>
/// Persists audit records durably — today the database, via <c>AuditEntries</c>.
/// </summary>
/// <remarks>
/// <para>
/// The file log is evidence too, but a file an operator can delete between shifts is not
/// the same guarantee as a row next to the weighments it describes. This store receives
/// every audit record the logger emits, on a background writer, so a slow or briefly
/// unavailable database never stalls an operator mid-transaction.
/// </para>
/// <para>
/// Implementations must not throw: an audit write failing is reported through logging,
/// never surfaced into the business operation being audited.
/// </para>
/// </remarks>
public interface IAuditStore
{
    /// <summary>Persists one audit record. Must not throw.</summary>
    ValueTask WriteAsync(AuditRecord record, CancellationToken cancellationToken = default);
}
