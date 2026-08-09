namespace WeighBridge.Domain.Common;

/// <summary>
/// Marks an entity as soft-deletable. Weighbridge records are legally significant,
/// so rows are retired rather than physically removed.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }

    DateTime? DeletedAtUtc { get; set; }

    string? DeletedBy { get; set; }
}
