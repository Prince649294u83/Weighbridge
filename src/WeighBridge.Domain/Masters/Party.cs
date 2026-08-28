using WeighBridge.Domain.Common;

namespace WeighBridge.Domain.Masters;

/// <summary>
/// A customer, supplier, or third-party organisation.
/// </summary>
public sealed class Party : EntityBase, IAggregateRoot, ISoftDeletable, IDeactivatable
{
    public const int NameMaxLength = 128;
    public const int CodeMaxLength = 32;
    public const int AddressMaxLength = 256;
    public const int ContactNumberMaxLength = 32;
    public const int EmailMaxLength = 128;
    public const int RemarksMaxLength = 512;

    /// <summary>For EF Core materialisation only.</summary>
    private Party()
    {
    }

    /// <summary>Creates a new party record.</summary>
    public static Party Create(
        string name,
        string? code = null,
        string? address = null,
        string? contactNumber = null,
        string? email = null,
        string? remarks = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmedName = Trim(name)!;
        if (trimmedName.Length > NameMaxLength)
        {
            throw new ArgumentException(
                $"Party name must be {NameMaxLength} characters or fewer.",
                nameof(name));
        }

        var trimmedCode = Trim(code);
        if (trimmedCode?.Length > CodeMaxLength)
        {
            throw new ArgumentException(
                $"Party code must be {CodeMaxLength} characters or fewer.",
                nameof(code));
        }

        var trimmedAddress = Trim(address);
        if (trimmedAddress?.Length > AddressMaxLength)
        {
            throw new ArgumentException(
                $"Address must be {AddressMaxLength} characters or fewer.",
                nameof(address));
        }

        var trimmedContact = Trim(contactNumber);
        if (trimmedContact?.Length > ContactNumberMaxLength)
        {
            throw new ArgumentException(
                $"Contact number must be {ContactNumberMaxLength} characters or fewer.",
                nameof(contactNumber));
        }

        var trimmedEmail = Trim(email);
        if (trimmedEmail?.Length > EmailMaxLength)
        {
            throw new ArgumentException(
                $"Email must be {EmailMaxLength} characters or fewer.",
                nameof(email));
        }

        var trimmedRemarks = Trim(remarks);
        if (trimmedRemarks?.Length > RemarksMaxLength)
        {
            throw new ArgumentException(
                $"Remarks must be {RemarksMaxLength} characters or fewer.",
                nameof(remarks));
        }

        return new Party
        {
            Name = trimmedName,
            Code = trimmedCode,
            Address = trimmedAddress,
            ContactNumber = trimmedContact,
            Email = trimmedEmail,
            Remarks = trimmedRemarks,
            IsActive = true,
        };
    }

    /// <summary>Party name (customer, vendor, account). Unique among active parties.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Optional account/customer code.</summary>
    public string? Code { get; private set; }

    /// <summary>Optional physical or billing address.</summary>
    public string? Address { get; private set; }

    /// <summary>Optional phone or contact number.</summary>
    public string? ContactNumber { get; private set; }

    /// <summary>Optional email address.</summary>
    public string? Email { get; private set; }

    /// <summary>Optional remarks.</summary>
    public string? Remarks { get; private set; }

    /// <summary>Whether this party is active for new weighments.</summary>
    public bool IsActive { get; private set; } = true;

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTime? DeletedAtUtc { get; set; }

    /// <inheritdoc />
    public string? DeletedBy { get; set; }

    /// <summary>Updates party details.</summary>
    public void Update(
        string name,
        string? code,
        string? address,
        string? contactNumber,
        string? email,
        string? remarks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmedName = Trim(name)!;
        if (trimmedName.Length > NameMaxLength)
        {
            throw new ArgumentException(
                $"Party name must be {NameMaxLength} characters or fewer.",
                nameof(name));
        }

        var trimmedCode = Trim(code);
        if (trimmedCode?.Length > CodeMaxLength)
        {
            throw new ArgumentException(
                $"Party code must be {CodeMaxLength} characters or fewer.",
                nameof(code));
        }

        var trimmedAddress = Trim(address);
        if (trimmedAddress?.Length > AddressMaxLength)
        {
            throw new ArgumentException(
                $"Address must be {AddressMaxLength} characters or fewer.",
                nameof(address));
        }

        var trimmedContact = Trim(contactNumber);
        if (trimmedContact?.Length > ContactNumberMaxLength)
        {
            throw new ArgumentException(
                $"Contact number must be {ContactNumberMaxLength} characters or fewer.",
                nameof(contactNumber));
        }

        var trimmedEmail = Trim(email);
        if (trimmedEmail?.Length > EmailMaxLength)
        {
            throw new ArgumentException(
                $"Email must be {EmailMaxLength} characters or fewer.",
                nameof(email));
        }

        var trimmedRemarks = Trim(remarks);
        if (trimmedRemarks?.Length > RemarksMaxLength)
        {
            throw new ArgumentException(
                $"Remarks must be {RemarksMaxLength} characters or fewer.",
                nameof(remarks));
        }

        Name = trimmedName;
        Code = trimmedCode;
        Address = trimmedAddress;
        ContactNumber = trimmedContact;
        Email = trimmedEmail;
        Remarks = trimmedRemarks;
    }

    /// <summary>Deactivates this party so it cannot be selected for new weighments.</summary>
    public void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>Reactivates this party.</summary>
    public void Reactivate()
    {
        IsActive = true;
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public override string ToString() => $"{Name} {(IsActive ? string.Empty : "[Inactive]")}".Trim();
}
