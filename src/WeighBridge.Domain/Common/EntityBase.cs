namespace WeighBridge.Domain.Common;

/// <summary>
/// Base class for every persistable domain entity.
/// </summary>
/// <remarks>
/// Module 0.1 defines the contract only; no concrete entities exist yet. Audit
/// columns live here so that every future table (vehicles, tickets, materials)
/// records provenance without repeating the fields.
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
