using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WeighBridge.Domain.Common;
using WeighBridge.Domain.Masters;

namespace WeighBridge.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Vehicle"/> to the <c>Vehicles</c> table.
/// </summary>
public sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    private static readonly ValueConverter<decimal, long> KilogramsToGrams = new(
        kilograms => (long)decimal.Round(kilograms * 1000m, 0, MidpointRounding.AwayFromZero),
        grams => grams / 1000m);

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Vehicles");

        builder.HasKey(vehicle => vehicle.Id);
        builder.Property(vehicle => vehicle.Id).ValueGeneratedOnAdd();

        builder.Property(vehicle => vehicle.VehicleNumber)
            .IsRequired()
            .HasMaxLength(Vehicle.VehicleNumberMaxLength)
            // Case-insensitive uniqueness: the domain upper-cases on write, but the index
            // must not depend on every writer remembering to do so.
            .UseCollation("NOCASE");

        builder.Property(vehicle => vehicle.TareWeightKg)
            .HasConversion(KilogramsToGrams!)
            .HasColumnName("TareWeightGrams");

        builder.Property(vehicle => vehicle.Remarks)
            .HasMaxLength(Vehicle.RemarksMaxLength);

        builder.Property(vehicle => vehicle.IsActive).IsRequired();

        // Foreign key to VehicleType
        builder.HasOne(vehicle => vehicle.VehicleType)
            .WithMany()
            .HasForeignKey(vehicle => vehicle.VehicleTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        // Audit columns
        builder.Property(vehicle => vehicle.CreatedBy).HasMaxLength(128);
        builder.Property(vehicle => vehicle.ModifiedBy).HasMaxLength(128);
        builder.Property(vehicle => vehicle.DeletedBy).HasMaxLength(128);

        builder.Ignore(nameof(EntityBase.IsTransient));

        // Filtered unique index on active vehicles
        builder.HasIndex(vehicle => vehicle.VehicleNumber)
            .IsUnique()
            .HasFilter("IsDeleted = 0 AND IsActive = 1");

        builder.HasIndex(vehicle => vehicle.IsActive);
        builder.HasIndex(vehicle => vehicle.VehicleTypeId);
        builder.HasIndex(vehicle => vehicle.CreatedAtUtc);

        builder.HasQueryFilter(vehicle => !vehicle.IsDeleted);
    }
}
