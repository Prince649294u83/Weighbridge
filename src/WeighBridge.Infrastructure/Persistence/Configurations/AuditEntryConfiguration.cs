using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WeighBridge.Infrastructure.Persistence.Auditing;

namespace WeighBridge.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="AuditEntry"/> to the <c>AuditEntries</c> table.
/// </summary>
public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AuditEntries");

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedOnAdd();

        builder.Property(entry => entry.OccurredAtUtc).IsRequired();
        builder.Property(entry => entry.OperatorName).HasMaxLength(AuditEntry.OperatorMaxLength);
        builder.Property(entry => entry.Module).HasMaxLength(AuditEntry.ModuleMaxLength);
        builder.Property(entry => entry.Action).IsRequired().HasMaxLength(AuditEntry.ActionMaxLength);
        builder.Property(entry => entry.Outcome).IsRequired().HasMaxLength(AuditEntry.OutcomeMaxLength);
        builder.Property(entry => entry.Entity).HasMaxLength(AuditEntry.EntityMaxLength);
        builder.Property(entry => entry.EntityId).HasMaxLength(AuditEntry.EntityIdMaxLength);
        builder.Property(entry => entry.Details).HasMaxLength(AuditEntry.DetailsMaxLength);
        builder.Property(entry => entry.CorrelationId).HasMaxLength(AuditEntry.CorrelationIdMaxLength);

        // The two questions a disputed day asks: what happened, in order; and who did it.
        builder.HasIndex(entry => entry.OccurredAtUtc);
        builder.HasIndex(entry => entry.OperatorName);
    }
}
