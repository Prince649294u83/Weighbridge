namespace WeighBridge.Domain.Common;

/// <summary>
/// Base class for every persistable domain entity.
/// </summary>
/// <remarks>
/// Audit columns live here so that every table records provenance without repeating
/// the fields. <see cref="WeighBridge.Domain.Weighments.Weighment"/> is the first
/// entity to derive from it.
/// </remarks>
public abstract class EntityBase
{
    /// <summary>Surrogate primary key. Zero means "not yet persisted".</summary>
    public long Id { get; set; }

    /// <summary>UTC timestamp captured when the row was first written.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Login name of the operator who created the row.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>UTC timestamp of the most recent modification, if any.</summary>
    public DateTime? ModifiedAtUtc { get; set; }

    /// <summary>Login name of the operator who last modified the row.</summary>
    public string? ModifiedBy { get; set; }

    /// <summary>True when the row has never been persisted.</summary>
    public bool IsTransient => Id == 0;
}
