using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WeighBridge.Domain.Common;
using WeighBridge.Domain.Masters;

namespace WeighBridge.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Material"/> to the <c>Materials</c> table.
/// </summary>
public sealed class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Materials");

        builder.HasKey(material => material.Id);
        builder.Property(material => material.Id).ValueGeneratedOnAdd();

        builder.Property(material => material.Name)
            .IsRequired()
            .HasMaxLength(Material.NameMaxLength)
            // Case-insensitive uniqueness: "Coal" and "coal" must not be two materials.
            .UseCollation("NOCASE");

        builder.Property(material => material.Code)
            .HasMaxLength(Material.CodeMaxLength);

        builder.Property(material => material.Description)
            .HasMaxLength(Material.DescriptionMaxLength);

        builder.Property(material => material.IsActive).IsRequired();

        // Audit columns
        builder.Property(material => material.CreatedBy).HasMaxLength(128);
        builder.Property(material => material.ModifiedBy).HasMaxLength(128);
        builder.Property(material => material.DeletedBy).HasMaxLength(128);

        builder.Ignore(nameof(EntityBase.IsTransient));

        // Filtered unique index on active materials
        builder.HasIndex(material => material.Name)
            .IsUnique()
            .HasFilter("IsDeleted = 0 AND IsActive = 1");

        builder.HasIndex(material => material.IsActive);
        builder.HasIndex(material => material.Code);
        builder.HasIndex(material => material.CreatedAtUtc);

        builder.HasQueryFilter(material => !material.IsDeleted);
    }
}
