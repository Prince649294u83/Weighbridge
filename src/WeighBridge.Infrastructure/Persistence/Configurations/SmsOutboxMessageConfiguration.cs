using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WeighBridge.Domain.Messaging;

namespace WeighBridge.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="SmsOutboxMessage"/> to the <c>SmsOutboxMessages</c> table.
/// </summary>
public sealed class SmsOutboxMessageConfiguration : IEntityTypeConfiguration<SmsOutboxMessage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SmsOutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("SmsOutboxMessages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedOnAdd();

        builder.Property(m => m.MessageKey)
            .IsRequired()
            .HasMaxLength(SmsOutboxMessage.MaxMessageKeyLength);

        builder.Property(m => m.RecipientPhoneNumber)
            .IsRequired()
            .HasMaxLength(SmsOutboxMessage.MaxRecipientLength);

        builder.Property(m => m.MessageContent)
            .IsRequired()
            .HasMaxLength(SmsOutboxMessage.MaxContentLength);

        builder.Property(m => m.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(m => m.Attempts).IsRequired();
        builder.Property(m => m.MaxAttempts).IsRequired();
        builder.Property(m => m.CreatedAtUtc).IsRequired();

        builder.Property(m => m.LastError).HasMaxLength(SmsOutboxMessage.MaxLastErrorLength);
        builder.Property(m => m.ProviderUsed).HasMaxLength(SmsOutboxMessage.MaxProviderLength);
        builder.Property(m => m.CorrelationId).HasMaxLength(SmsOutboxMessage.MaxCorrelationIdLength);

        // Deterministic idempotency constraint
        builder.HasIndex(m => m.MessageKey).IsUnique();

        // Polling and lease recovery indexes
        builder.HasIndex(m => new { m.Status, m.NextAttemptUtc });
        builder.HasIndex(m => m.SendingSinceUtc);
        builder.HasIndex(m => m.WeighmentId);
        builder.HasIndex(m => m.CorrelationId);
    }
}
