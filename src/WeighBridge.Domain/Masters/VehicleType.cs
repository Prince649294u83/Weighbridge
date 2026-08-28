using WeighBridge.Domain.Common;

namespace WeighBridge.Domain.Masters;

/// <summary>
/// A category or class of vehicle (e.g., 6-Wheeler, 10-Wheeler, Trailer, Dumper, Tanker).
/// </summary>
public sealed class VehicleType : EntityBase, IAggregateRoot, ISoftDeletable, IDeactivatable
{
    public const int TypeNameMaxLength = 64;
    public const int DescriptionMaxLength = 256;

    /// <summary>For EF Core materialisation only.</summary>
    private VehicleType()
    {
    }

    /// <summary>Creates a new vehicle type.</summary>
    public static VehicleType Create(string typeName, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);

        var trimmedName = NormaliseTypeName(typeName);
        if (trimmedName.Length > TypeNameMaxLength)
        {
            throw new ArgumentException(
                $"Vehicle type name must be {TypeNameMaxLength} characters or fewer.",
                nameof(typeName));
        }

        var trimmedDesc = Trim(description);
        if (trimmedDesc?.Length > DescriptionMaxLength)
        {
            throw new ArgumentException(
                $"Description must be {DescriptionMaxLength} characters or fewer.",
                nameof(description));
        }

        return new VehicleType
        {
            TypeName = trimmedName,
            Description = trimmedDesc,
            IsActive = true,
        };
    }

    /// <summary>Name of the vehicle type (e.g., "10 Wheeler Truck").</summary>
    public string TypeName { get; private set; } = string.Empty;

    /// <summary>Optional description or capacity specification.</summary>
    public string? Description { get; private set; }

    /// <summary>Whether this vehicle type is available for new weighments.</summary>
    public bool IsActive { get; private set; } = true;

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTime? DeletedAtUtc { get; set; }

    /// <inheritdoc />
    public string? DeletedBy { get; set; }

    /// <summary>Updates the vehicle type details.</summary>
    public void Update(string typeName, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);

        var trimmedName = NormaliseTypeName(typeName);
        if (trimmedName.Length > TypeNameMaxLength)
        {
            throw new ArgumentException(
                $"Vehicle type name must be {TypeNameMaxLength} characters or fewer.",
                nameof(typeName));
        }

        var trimmedDesc = Trim(description);
        if (trimmedDesc?.Length > DescriptionMaxLength)
        {
            throw new ArgumentException(
                $"Description must be {DescriptionMaxLength} characters or fewer.",
                nameof(description));
        }

        TypeName = trimmedName;
        Description = trimmedDesc;
    }

    /// <summary>Deactivates this vehicle type so it cannot be selected for new weighments.</summary>
    public void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>Reactivates this vehicle type.</summary>
    public void Reactivate()
    {
        IsActive = true;
    }

    public static string NormaliseTypeName(string typeName)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        return typeName.Trim();
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public override string ToString() => $"{TypeName} {(IsActive ? string.Empty : "[Inactive]")}".Trim();
}
