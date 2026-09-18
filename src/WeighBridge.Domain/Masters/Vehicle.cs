using WeighBridge.Domain.Common;

namespace WeighBridge.Domain.Masters;

/// <summary>
/// A registered vehicle master record.
/// </summary>
public sealed class Vehicle : EntityBase, IAggregateRoot, ISoftDeletable, IDeactivatable
{
    public const int VehicleNumberMaxLength = 24;
    public const int RemarksMaxLength = 512;
    public const decimal MaximumTareKg = 100_000m;

    /// <summary>For EF Core materialisation only.</summary>
    private Vehicle()
    {
    }

    /// <summary>Creates a new vehicle record.</summary>
    public static Vehicle Create(
        string vehicleNumber,
        long? vehicleTypeId = null,
        decimal? tareWeightKg = null,
        string? remarks = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicleNumber);

        var normalisedNumber = NormaliseVehicleNumber(vehicleNumber);
        if (normalisedNumber.Length == 0 || !normalisedNumber.Any(char.IsLetterOrDigit))
        {
            throw new ArgumentException(
                "A vehicle number needs at least one letter or digit.",
                nameof(vehicleNumber));
        }

        if (normalisedNumber.Length > VehicleNumberMaxLength)
        {
            throw new ArgumentException(
                $"Vehicle registration must be {VehicleNumberMaxLength} characters or fewer.",
                nameof(vehicleNumber));
        }

        if (tareWeightKg.HasValue && (tareWeightKg.Value < 0m || tareWeightKg.Value > MaximumTareKg))
        {
            throw new ArgumentOutOfRangeException(
                nameof(tareWeightKg),
                tareWeightKg.Value,
                $"Tare weight must be between 0 and {MaximumTareKg:0} kg.");
        }

        var trimmedRemarks = Trim(remarks);
        if (trimmedRemarks?.Length > RemarksMaxLength)
        {
            throw new ArgumentException(
                $"Remarks must be {RemarksMaxLength} characters or fewer.",
                nameof(remarks));
        }

        return new Vehicle
        {
            VehicleNumber = normalisedNumber,
            VehicleTypeId = vehicleTypeId,
            TareWeightKg = tareWeightKg,
            Remarks = trimmedRemarks,
            IsActive = true,
        };
    }

    /// <summary>Normalised vehicle registration number.</summary>
    public string VehicleNumber { get; private set; } = string.Empty;

    /// <summary>Optional foreign key link to <see cref="VehicleType"/>.</summary>
    public long? VehicleTypeId { get; private set; }

    /// <summary>Navigation property to <see cref="VehicleType"/>.</summary>
    public VehicleType? VehicleType { get; private set; }

    /// <summary>Optional standard tare (empty) weight in kilograms.</summary>
    public decimal? TareWeightKg { get; private set; }

    /// <summary>Whether this vehicle is active.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Optional notes.</summary>
    public string? Remarks { get; private set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTime? DeletedAtUtc { get; set; }

    /// <inheritdoc />
    public string? DeletedBy { get; set; }

    /// <summary>Updates vehicle details.</summary>
    public void Update(
        string vehicleNumber,
        long? vehicleTypeId,
        decimal? tareWeightKg,
        string? remarks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicleNumber);

        var normalisedNumber = NormaliseVehicleNumber(vehicleNumber);
        if (normalisedNumber.Length == 0 || !normalisedNumber.Any(char.IsLetterOrDigit))
        {
            throw new ArgumentException(
                "A vehicle number needs at least one letter or digit.",
                nameof(vehicleNumber));
        }

        if (normalisedNumber.Length > VehicleNumberMaxLength)
        {
            throw new ArgumentException(
                $"Vehicle registration must be {VehicleNumberMaxLength} characters or fewer.",
                nameof(vehicleNumber));
        }

        if (tareWeightKg.HasValue && (tareWeightKg.Value < 0m || tareWeightKg.Value > MaximumTareKg))
        {
            throw new ArgumentOutOfRangeException(
                nameof(tareWeightKg),
                tareWeightKg.Value,
                $"Tare weight must be between 0 and {MaximumTareKg:0} kg.");
        }

        var trimmedRemarks = Trim(remarks);
        if (trimmedRemarks?.Length > RemarksMaxLength)
        {
            throw new ArgumentException(
                $"Remarks must be {RemarksMaxLength} characters or fewer.",
                nameof(remarks));
        }

        VehicleNumber = normalisedNumber;
        VehicleTypeId = vehicleTypeId;
        TareWeightKg = tareWeightKg;
        Remarks = trimmedRemarks;
    }

    /// <summary>Updates the vehicle master tare weight (e.g. from AutoUpdateTareWeight option).</summary>
    public void UpdateTareWeight(decimal tareWeightKg)
    {
        if (tareWeightKg < 0m || tareWeightKg > MaximumTareKg)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tareWeightKg),
                tareWeightKg,
                $"Tare weight must be between 0 and {MaximumTareKg:0} kg.");
        }
        TareWeightKg = tareWeightKg;
    }

    /// <summary>Deactivates this vehicle so it cannot be selected for new weighments.</summary>
    public void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>Reactivates this vehicle.</summary>
    public void Reactivate()
    {
        IsActive = true;
    }

    public static string NormaliseVehicleNumber(string vehicleNumber)
    {
        ArgumentNullException.ThrowIfNull(vehicleNumber);

        return string.Concat(
            vehicleNumber.Where(character => !char.IsWhiteSpace(character) && character is not ('-' or '.')))
            .ToUpperInvariant();
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public override string ToString() => $"{VehicleNumber} {(IsActive ? string.Empty : "[Inactive]")}".Trim();
}
