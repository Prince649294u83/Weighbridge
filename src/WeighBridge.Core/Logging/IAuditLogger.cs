namespace WeighBridge.Core.Logging;

/// <summary>
/// Records who changed what. The audit trail.
/// </summary>
/// <remarks>
/// Separate from <see cref="IApplicationLogger"/> because the two have different rules.
/// Application logging is diagnostic and may be turned down to reduce noise; an audit
/// entry is evidence, is always written, and answers a question asked months later when
/// a weighment is disputed.
/// </remarks>
public interface IAuditLogger : IApplicationLogger
{
    /// <summary>
    /// Records an action against a record.
    /// </summary>
    /// <param name="action">What was done — <c>Created</c>, <c>Updated</c>, <c>Deleted</c>, <c>Printed</c>.</param>
    /// <param name="entity">What it was done to — <c>Ticket</c>, <c>Vehicle</c>, <c>Rate</c>.</param>
    /// <param name="entityId">Which one, when it has an identity.</param>
    /// <param name="details">Anything else worth keeping — old and new values.</param>
    void Record(string action, string entity, string? entityId = null, string? details = null);

    /// <summary>
    /// Records an action that was refused, and why.
    /// </summary>
    /// <remarks>
    /// Written at warning level: a refused deletion is a normal outcome, but a run of them
    /// is worth a supervisor's attention.
    /// </remarks>
    void RecordDenied(string action, string entity, string reason, string? entityId = null);

    /// <summary>
    /// Records an action that attempted real work and failed — not an operator typo, but
    /// something that broke partway through.
    /// </summary>
    /// <remarks>
    /// A failure is exactly when the trail matters most: data may have moved halfway and
    /// nobody was told. Recorded alongside the success entries so a disputed day shows
    /// what did and did not complete.
    /// </remarks>
    void RecordFailed(string action, string entity, string reason, string? entityId = null);
}
