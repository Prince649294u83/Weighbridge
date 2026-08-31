using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WeighBridge.Domain.Common;
using WeighBridge.Domain.Security;

namespace WeighBridge.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="User"/> to the <c>Users</c> table.
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Users");

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedOnAdd();

        builder.Property(user => user.Username)
            .IsRequired()
            .HasMaxLength(User.UsernameMaxLength)
            .UseCollation("NOCASE");

        builder.Property(user => user.DisplayName)
            .IsRequired()
            .HasMaxLength(User.DisplayNameMaxLength);

        builder.Property(user => user.PasswordHash)
            .IsRequired()
            .HasMaxLength(User.PasswordHashMaxLength);

        builder.Property(user => user.RoleName)
            .IsRequired()
            .HasMaxLength(User.RoleNameMaxLength);

        builder.Property(user => user.IsActive).IsRequired();

        // Persistent Lockout and Password Expiry Columns
        builder.Property(user => user.FailedAccessCount).IsRequired().HasDefaultValue(0);
        builder.Property(user => user.LockoutUntilUtc);
        builder.Property(user => user.PasswordChangedAtUtc).IsRequired();
        builder.Property(user => user.MustChangePassword).IsRequired().HasDefaultValue(false);

        // Audit columns
        builder.Property(user => user.CreatedBy).HasMaxLength(128);
        builder.Property(user => user.ModifiedBy).HasMaxLength(128);
        builder.Property(user => user.DeletedBy).HasMaxLength(128);

        builder.Ignore(nameof(EntityBase.IsTransient));

        // Filtered unique index on username (NOCASE collation above applies to it)
        builder.HasIndex(user => user.Username)
            .IsUnique()
            .HasFilter("IsDeleted = 0");

        builder.HasQueryFilter(user => !user.IsDeleted);
    }
}
