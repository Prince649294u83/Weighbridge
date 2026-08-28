using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WeighBridge.Domain.Common;
using WeighBridge.Domain.Masters;

namespace WeighBridge.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Party"/> to the <c>Parties</c> table.
/// </summary>
public sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Party> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Parties");

        builder.HasKey(party => party.Id);
        builder.Property(party => party.Id).ValueGeneratedOnAdd();

        builder.Property(party => party.Name)
            .IsRequired()
            .HasMaxLength(Party.NameMaxLength)
            // Case-insensitive uniqueness: "Acme" and "ACME" must not be two parties.
            .UseCollation("NOCASE");

        builder.Property(party => party.Code)
            .HasMaxLength(Party.CodeMaxLength);

        builder.Property(party => party.Address)
            .HasMaxLength(Party.AddressMaxLength);

        builder.Property(party => party.ContactNumber)
            .HasMaxLength(Party.ContactNumberMaxLength);

        builder.Property(party => party.Email)
            .HasMaxLength(Party.EmailMaxLength);

        builder.Property(party => party.Remarks)
            .HasMaxLength(Party.RemarksMaxLength);

        builder.Property(party => party.IsActive).IsRequired();

        // Audit columns
        builder.Property(party => party.CreatedBy).HasMaxLength(128);
        builder.Property(party => party.ModifiedBy).HasMaxLength(128);
        builder.Property(party => party.DeletedBy).HasMaxLength(128);

        builder.Ignore(nameof(EntityBase.IsTransient));

        // Filtered unique index on active parties
        builder.HasIndex(party => party.Name)
            .IsUnique()
            .HasFilter("IsDeleted = 0 AND IsActive = 1");

        builder.HasIndex(party => party.IsActive);
        builder.HasIndex(party => party.Code);
        builder.HasIndex(party => party.CreatedAtUtc);

        builder.HasQueryFilter(party => !party.IsDeleted);
    }
}
