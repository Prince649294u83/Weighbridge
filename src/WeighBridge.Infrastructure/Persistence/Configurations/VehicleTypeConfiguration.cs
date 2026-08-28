using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WeighBridge.Domain.Common;
using WeighBridge.Domain.Masters;

namespace WeighBridge.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="VehicleType"/> to the <c>VehicleTypes</c> table.
/// </summary>
public sealed class VehicleTypeConfiguration : IEntityTypeConfiguration<VehicleType>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VehicleType> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("VehicleTypes");

        builder.HasKey(type => type.Id);
        builder.Property(type => type.Id).ValueGeneratedOnAdd();

        builder.Property(type => type.TypeName)
            .IsRequired()
            .HasMaxLength(VehicleType.TypeNameMaxLength)
            // Case-insensitive uniqueness.
            .UseCollation("NOCASE");

        builder.Property(type => type.Description)
            .HasMaxLength(VehicleType.DescriptionMaxLength);

        builder.Property(type => type.IsActive).IsRequired();

        // Audit columns
        builder.Property(type => type.CreatedBy).HasMaxLength(128);
        builder.Property(type => type.ModifiedBy).HasMaxLength(128);
        builder.Property(type => type.DeletedBy).HasMaxLength(128);

        builder.Ignore(nameof(EntityBase.IsTransient));

        // Filtered unique index on active records
        builder.HasIndex(type => type.TypeName)
            .IsUnique()
            .HasFilter("IsDeleted = 0 AND IsActive = 1");

        builder.HasIndex(type => type.IsActive);
        builder.HasIndex(type => type.CreatedAtUtc);

        builder.HasQueryFilter(type => !type.IsDeleted);
    }
}
