using WeighBridge.Domain.Common;

namespace WeighBridge.Infrastructure.Persistence.Auditing;

/// <summary>
/// One row of the durable audit trail.
/// </summary>
/// <remarks>
/// <para>
/// Lives in the same database as the weighments it describes, on purpose: a trail stored
/// beside the evidence it covers cannot be deleted without deleting the evidence. It is
/// append-only by convention — nothing in the application updates or deletes these rows,
/// and the repository's soft-delete path is never used for them because they are never
/// queried back through the generic repository.
/// </para>
/// <para>
/// It carries the aggregate marker only so the generic repository can persist it; it is
/// infrastructure bookkeeping, not a domain concept.
/// </para>
/// </remarks>
public sealed class AuditEntry : EntityBase, IAggregateRoot
{
    public const int ActionMaxLength = 128;
    public const int OutcomeMaxLength = 32;
    public const int EntityMaxLength = 128;
    public const int EntityIdMaxLength = 64;
    public const int DetailsMaxLength = 2048;
    public const int OperatorMaxLength = 64;
    public const int ModuleMaxLength = 64;
    public const int CorrelationIdMaxLength = 32;

    private AuditEntry()
    {
        // EF Core materialisation.
    }

    private AuditEntry(
        DateTime occurredAtUtc,
        string? operatorName,
        string? module,
        string action,
        string outcome,
        string? entity,
        string? entityId,
        string? details,
        string? correlationId)
    {
        OccurredAtUtc = occurredAtUtc;
        OperatorName = operatorName;
        Module = module;
        Action = action;
        Outcome = outcome;
        Entity = entity;
        EntityId = entityId;
        Details = details;
        CorrelationId = correlationId;
    }

    /// <summary>When the audited thing happened.</summary>
    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>The authenticated operator responsible, when known.</summary>
    public string? OperatorName { get; private set; }

    /// <summary>The module the action came from, when ambient scope knew it.</summary>
    public string? Module { get; private set; }

    /// <summary>What was done.</summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>Succeeded / Denied / Failed.</summary>
    public string Outcome { get; private set; } = string.Empty;

    /// <summary>What it was done to.</summary>
    public string? Entity { get; private set; }

    /// <summary>Which one — slip number, username, row id.</summary>
    public string? EntityId { get; private set; }

    /// <summary>Anything else worth keeping. Never secrets or passwords.</summary>
    public string? Details { get; private set; }

    /// <summary>Ties the row to every log line written for the same operation.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>
    /// Creates an entry, truncating over-long fields rather than failing: losing the tail
    /// of a long detail string beats losing the whole record to a constraint violation at
    /// the moment being audited.
    /// </summary>
    public static AuditEntry Create(
        DateTime occurredAtUtc,
        string? operatorName,
        string? module,
        string action,
        string outcome,
        string? entity,
        string? entityId,
        string? details,
        string? correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);

        return new AuditEntry(
            occurredAtUtc,
            Truncate(operatorName, OperatorMaxLength),
            Truncate(module, ModuleMaxLength),
            Truncate(action.Trim(), ActionMaxLength)!,
            Truncate(outcome.Trim(), OutcomeMaxLength)!,
            Truncate(entity, EntityMaxLength),
            Truncate(entityId, EntityIdMaxLength),
            Truncate(details, DetailsMaxLength),
            Truncate(correlationId, CorrelationIdMaxLength));
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
