using Microsoft.Extensions.Logging;
using WeighBridge.Core.Application;

namespace WeighBridge.Core.Logging;

/// <summary>
/// Writes the audit trail.
/// </summary>
/// <remarks>
/// Entries are written at information level and above, and the audit category should never
/// be filtered below that in configuration — an audit trail with gaps is worse than none,
/// because it looks complete.
/// </remarks>
public sealed class AuditLogger(ILoggerFactory loggerFactory, IApplicationInfoService applicationInfo)
    : CategoryLoggerBase(LogCategory.Audit, loggerFactory, applicationInfo), IAuditLogger
{
    /// <inheritdoc />
    public void Record(string action, string entity, string? entityId = null, string? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);

        Write(
            LogLevel.Information,
            null,
            "{Action} {Entity} {EntityId} {Details}",
            [action, entity, entityId ?? "-", details ?? string.Empty]);
    }

    /// <inheritdoc />
    public void RecordDenied(string action, string entity, string reason, string? entityId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Write(
            LogLevel.Warning,
            null,
            "DENIED {Action} {Entity} {EntityId}: {Reason}",
            [action, entity, entityId ?? "-", reason]);
    }
}
