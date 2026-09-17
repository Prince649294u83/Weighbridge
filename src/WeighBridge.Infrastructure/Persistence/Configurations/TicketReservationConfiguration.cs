using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WeighBridge.Domain.Common;
using WeighBridge.Domain.Weighments;

namespace WeighBridge.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="TicketReservation"/> to the <c>TicketReservations</c> table.
/// </summary>
public sealed class TicketReservationConfiguration : IEntityTypeConfiguration<TicketReservation>
{
    public void Configure(EntityTypeBuilder<TicketReservation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TicketReservations");

        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.Id).ValueGeneratedOnAdd();

        builder.Property(reservation => reservation.SlipNumber)
            .IsRequired()
            .HasMaxLength(SlipNumbers.MaxLength);

        builder.Property(reservation => reservation.Status).IsRequired();

        builder.Property(reservation => reservation.TentativeVehicleNumber)
            .HasMaxLength(Weighment.VehicleNumberMaxLength);

        builder.Property(reservation => reservation.TerminalId)
            .HasMaxLength(TicketReservation.MaxTerminalIdLength);

        builder.Property(reservation => reservation.ConsumedBy)
            .HasMaxLength(Weighment.NameMaxLength);

        builder.Property(reservation => reservation.CancellationReason)
            .HasMaxLength(TicketReservation.MaxReasonLength);

        builder.Property(reservation => reservation.CreatedBy)
            .HasMaxLength(Weighment.NameMaxLength);

        builder.Property(reservation => reservation.ModifiedBy)
            .HasMaxLength(Weighment.NameMaxLength);

        builder.Property(reservation => reservation.Version)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Ignore(nameof(EntityBase.IsTransient));

        builder.HasIndex(reservation => reservation.SlipNumber)
            .IsUnique()
            .HasDatabaseName("IX_TicketReservations_SlipNumber")
            .HasFilter("[SlipNumber] <> ''");

        builder.HasIndex(reservation => reservation.Status)
            .HasDatabaseName("IX_TicketReservations_Status");

        builder.HasIndex(reservation => reservation.CreatedAtUtc)
            .HasDatabaseName("IX_TicketReservations_CreatedAtUtc");

        builder.HasIndex(reservation => reservation.ConsumedByWeighmentId)
            .IsUnique()
            .HasDatabaseName("IX_TicketReservations_ConsumedByWeighmentId")
            .HasFilter("[ConsumedByWeighmentId] IS NOT NULL");

        builder.HasOne<Weighment>()
            .WithMany()
            .HasForeignKey(reservation => reservation.ConsumedByWeighmentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
