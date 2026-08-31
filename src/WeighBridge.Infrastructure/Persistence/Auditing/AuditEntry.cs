using System.Text.RegularExpressions;
using WeighBridge.Domain.Common;

namespace WeighBridge.Infrastructure.Persistence.Auditing;

/// <summary>
/// Append-only durable audit trail row persisted directly to SQLite.
/// </summary>
public sealed class AuditEntry : EntityBase, IAggregateRoot
{
    public const int ActionMaxLength = 128;
    public const int OutcomeMaxLength = 32;
    public const int EntityMaxLength = 128;
    public const int EntityIdMaxLength = 64;
    public const int DetailsMaxLength = 2048;
    public const int OperatorMaxLength = 64;
    public const int ModuleMaxLength = 64;
    public const int CorrelationIdMaxLength = 36;

    private static readonly Regex CredentialSanitizerPattern = new(
        @"(?i)(password|pin|key|secret|token)\s*[:=]\s*([^\s,;]+)",
        RegexOptions.Compiled);

    private AuditEntry()
    {
        // EF Core materialisation
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

    /// <summary>What was done (e.g. AuditActions.WeighmentCompleted).</summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>Canonical outcome: SUCCESS, FAILURE, or DENIED.</summary>
    public string Outcome { get; private set; } = string.Empty;

    /// <summary>Entity name (e.g. Weighment, User, Configuration).</summary>
    public string? Entity { get; private set; }

    /// <summary>Entity key/identifier (e.g. slip number, username).</summary>
    public string? EntityId { get; private set; }

    /// <summary>Sanitized details with credentials replaced by ***REDACTED***.</summary>
    public string? Details { get; private set; }

    /// <summary>Unified operation correlation GUID tying events across subsystems.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>
    /// Creates an audit entry, applying deterministic credential redaction and length limits.
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

        var sanitizedDetails = SanitizeDetails(details);

        return new AuditEntry(
            occurredAtUtc,
            Truncate(operatorName, OperatorMaxLength),
            Truncate(module, ModuleMaxLength),
            Truncate(action.Trim(), ActionMaxLength)!,
            Truncate(outcome.Trim(), OutcomeMaxLength)!,
            Truncate(entity, EntityMaxLength),
            Truncate(entityId, EntityIdMaxLength),
            Truncate(sanitizedDetails, DetailsMaxLength),
            Truncate(correlationId, CorrelationIdMaxLength));
    }

    public static string? SanitizeDetails(string? details)
    {
        if (string.IsNullOrWhiteSpace(details)) return details;
        return CredentialSanitizerPattern.Replace(details, "$1=***REDACTED***");
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
