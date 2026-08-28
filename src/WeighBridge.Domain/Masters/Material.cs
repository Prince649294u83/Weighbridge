using WeighBridge.Domain.Common;

namespace WeighBridge.Domain.Masters;

/// <summary>
/// A commodity or material master record (e.g. Coal, Iron Ore, Sand, Grain, Cement).
/// </summary>
public sealed class Material : EntityBase, IAggregateRoot, ISoftDeletable, IDeactivatable
{
    public const int NameMaxLength = 128;
    public const int CodeMaxLength = 32;
    public const int DescriptionMaxLength = 256;

    /// <summary>For EF Core materialisation only.</summary>
    private Material()
    {
    }

    /// <summary>Creates a new material record.</summary>
    public static Material Create(
        string name,
        string? code = null,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmedName = Trim(name)!;
        if (trimmedName.Length > NameMaxLength)
        {
            throw new ArgumentException(
                $"Material name must be {NameMaxLength} characters or fewer.",
                nameof(name));
        }

        var trimmedCode = Trim(code);
        if (trimmedCode?.Length > CodeMaxLength)
        {
            throw new ArgumentException(
                $"Material code must be {CodeMaxLength} characters or fewer.",
                nameof(code));
        }

        var trimmedDesc = Trim(description);
        if (trimmedDesc?.Length > DescriptionMaxLength)
        {
            throw new ArgumentException(
                $"Description must be {DescriptionMaxLength} characters or fewer.",
                nameof(description));
        }

        return new Material
        {
            Name = trimmedName,
            Code = trimmedCode,
            Description = trimmedDesc,
            IsActive = true,
        };
    }

    /// <summary>Material commodity name. Unique among active materials.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Optional product/item code.</summary>
    public string? Code { get; private set; }

    /// <summary>Optional material description.</summary>
    public string? Description { get; private set; }

    /// <summary>Whether this material is active for new weighments.</summary>
    public bool IsActive { get; private set; } = true;

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTime? DeletedAtUtc { get; set; }

    /// <inheritdoc />
    public string? DeletedBy { get; set; }

    /// <summary>Updates material details.</summary>
    public void Update(
        string name,
        string? code,
        string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmedName = Trim(name)!;
        if (trimmedName.Length > NameMaxLength)
        {
            throw new ArgumentException(
                $"Material name must be {NameMaxLength} characters or fewer.",
                nameof(name));
        }

        var trimmedCode = Trim(code);
        if (trimmedCode?.Length > CodeMaxLength)
        {
            throw new ArgumentException(
                $"Material code must be {CodeMaxLength} characters or fewer.",
                nameof(code));
        }

        var trimmedDesc = Trim(description);
        if (trimmedDesc?.Length > DescriptionMaxLength)
        {
            throw new ArgumentException(
                $"Description must be {DescriptionMaxLength} characters or fewer.",
                nameof(description));
        }

        Name = trimmedName;
        Code = trimmedCode;
        Description = trimmedDesc;
    }

    /// <summary>Deactivates this material so it cannot be selected for new weighments.</summary>
    public void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>Reactivates this material.</summary>
    public void Reactivate()
    {
        IsActive = true;
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public override string ToString() => $"{Name} {(IsActive ? string.Empty : "[Inactive]")}".Trim();
}
