namespace WeighBridge.Domain.Common;

/// <summary>
/// An entity whose rows are retired rather than deleted, with an on/off flag the screens
/// filter by.
/// </summary>
/// <remarks>
/// Soft delete (<see cref="ISoftDeletable"/>) hides history from queries while keeping the
/// rows. Active/inactive is a different axis: a deactivated vehicle stays visible to
/// history but disappears from pick lists. When a row is soft-deleted, leaving it flagged
/// active contradicts itself — the repository retires <em>and</em> deactivates anything
/// implementing this, so the two flags never disagree.
/// </remarks>
public interface IDeactivatable
{
    /// <summary>Whether the record is available for new work.</summary>
    bool IsActive { get; }

    /// <summary>Marks the record unavailable for new work.</summary>
    void Deactivate();

    /// <summary>Marks the record available again.</summary>
    void Reactivate();
}
