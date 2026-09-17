using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WeighBridge.Domain.Common;
using WeighBridge.Domain.Weighments;

namespace WeighBridge.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Weighment"/> to the <c>Weighments</c> table.
/// </summary>
/// <remarks>
/// Discovered automatically — <see cref="WeighBridgeDbContext.OnModelCreating"/> applies
/// every <see cref="IEntityTypeConfiguration{TEntity}"/> in this assembly, so this class
/// needs no registration.
/// </remarks>
public sealed class WeighmentConfiguration : IEntityTypeConfiguration<Weighment>
{
    /// <summary>
    /// Stores weights as a whole number of grams.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SQLite has no decimal type. Left alone, EF Core stores <c>decimal</c> as text, which
    /// round-trips exactly but compares lexicographically — so <c>"9000"</c> sorts after
    /// <c>"12000"</c> and a <c>SUM</c> or a <c>WHERE net &gt; 5000</c> quietly returns the
    /// wrong answer. Mapping to <c>REAL</c> instead would make comparison work and exactness
    /// fail, which on a record an invoice is raised from is the worse of the two.
    /// </para>
    /// <para>
    /// An integer count of grams is exact, orders correctly, sums correctly in SQL, and has
    /// three orders of magnitude more resolution than any weighbridge indicator reports. The
    /// conversion is symmetric and lossless for anything the domain accepts.
    /// </para>
    /// </remarks>
    private static readonly ValueConverter<decimal, long> KilogramsToGrams = new(
        kilograms => (long)decimal.Round(kilograms * 1000m, 0, MidpointRounding.AwayFromZero),
        grams => grams / 1000m);

    /// <summary>
    /// Stores monetary fees and charges as an exact integer count of paise.
    /// </summary>
    private static readonly ValueConverter<decimal, long> RupeesToPaise = new(
        rupees => (long)decimal.Round(rupees * 100m, 0, MidpointRounding.AwayFromZero),
        paise => paise / 100m);

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Weighment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Weighments");

        builder.HasKey(weighment => weighment.Id);

        // The slip number is derived from this, so the database has to allocate it.
        builder.Property(weighment => weighment.Id).ValueGeneratedOnAdd();

        builder.Property(weighment => weighment.SlipNumber)
            .IsRequired()
            .HasMaxLength(SlipNumbers.MaxLength);

        builder.Property(weighment => weighment.VehicleNumber)
            .IsRequired()
            .HasMaxLength(Weighment.VehicleNumberMaxLength);

        builder.Property(weighment => weighment.Mode).IsRequired();
        builder.Property(weighment => weighment.Status).IsRequired();

        builder.Property(weighment => weighment.PartyName).HasMaxLength(Weighment.NameMaxLength);
        builder.Property(weighment => weighment.MaterialName).HasMaxLength(Weighment.NameMaxLength);
        builder.Property(weighment => weighment.DriverName).HasMaxLength(Weighment.NameMaxLength);
        builder.Property(weighment => weighment.TransporterName).HasMaxLength(Weighment.NameMaxLength);
        builder.Property(weighment => weighment.Remarks).HasMaxLength(Weighment.TextMaxLength);
        builder.Property(weighment => weighment.CancellationReason).HasMaxLength(Weighment.TextMaxLength);
        builder.Property(weighment => weighment.VehicleTypeName).HasMaxLength(64);

        builder.Property(weighment => weighment.Charges)
            .HasConversion(RupeesToPaise)
            .HasColumnName("ChargesPaise")
            .HasDefaultValue(0L);

        builder.Property(weighment => weighment.SecondCharges)
            .HasConversion(RupeesToPaise)
            .HasColumnName("SecondChargesPaise")
            .HasDefaultValue(0L);

        builder.Property(weighment => weighment.NumberOfBags);

        builder.Property(weighment => weighment.BagWeightKg)
            .HasConversion(KilogramsToGrams)
            .HasColumnName("BagWeightGrams");

        builder.Property(weighment => weighment.GatePassNumber).HasMaxLength(Weighment.GatePassMaxLength);
        builder.Property(weighment => weighment.CustomField1).HasMaxLength(Weighment.CustomFieldMaxLength);
        builder.Property(weighment => weighment.CustomField2).HasMaxLength(Weighment.CustomFieldMaxLength);
        builder.Property(weighment => weighment.CustomField3).HasMaxLength(Weighment.CustomFieldMaxLength);
        builder.Property(weighment => weighment.CustomField4).HasMaxLength(Weighment.CustomFieldMaxLength);

        builder.Property(weighment => weighment.Version)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(weighment => weighment.NetWeightKg)
            .HasConversion(KilogramsToGrams)
            .HasColumnName("NetWeightGrams");

        ConfigureCapture(builder, weighment => weighment.FirstWeight, "FirstWeight");
        ConfigureCapture(builder, weighment => weighment.SecondWeight, "SecondWeight");

        // Audit columns from EntityBase.
        builder.Property(weighment => weighment.CreatedBy).HasMaxLength(Weighment.NameMaxLength);
        builder.Property(weighment => weighment.ModifiedBy).HasMaxLength(Weighment.NameMaxLength);
        builder.Property(weighment => weighment.DeletedBy).HasMaxLength(Weighment.NameMaxLength);

        // Computed from the mapped columns; nothing to store. Gross and Tare would otherwise
        // be taken for two more owned navigations to the same two captures.
        builder.Ignore(weighment => weighment.Gross);
        builder.Ignore(weighment => weighment.Tare);
        builder.Ignore(weighment => weighment.TotalBagWeightKg);
        builder.Ignore(weighment => weighment.ActualWeightKg);
        builder.Ignore(weighment => weighment.IsOpen);
        builder.Ignore(weighment => weighment.NextAction);
        builder.Ignore(nameof(EntityBase.IsTransient));

        // Unique among real slip numbers only. The number is derived from the identity and
        // written in a second step, so a row sits briefly (or, after a crash, permanently)
        // with an empty value; an unfiltered unique index would let two such rows collide
        // in one SaveChanges batch and would let one crashed row block every future insert.
        builder.HasIndex(weighment => weighment.SlipNumber)
            .IsUnique()
            .HasDatabaseName("IX_Weighments_SlipNumber")
            .HasFilter("[SlipNumber] <> ''");

        // Database enforcement of "one open transaction per vehicle". The service-level
        // duplicate-open guard produces a friendly message; this index is what actually
        // stops two operators racing past it. Cancelled and completed records are excluded,
        // so history is unaffected.
        builder.HasIndex(weighment => weighment.VehicleNumber)
            .IsUnique()
            .HasDatabaseName("IX_Weighments_VehicleNumber_Open")
            .HasFilter("[Status] IN (0, 1) AND [IsDeleted] = 0");

        // The screen's two hot queries: the vehicles currently on site, and this vehicle's
        // history when the operator types a registration that is already open.
        builder.HasIndex(weighment => weighment.Status);
        builder.HasIndex(weighment => weighment.CreatedAtUtc);
        builder.HasIndex(weighment => weighment.VehicleId);
        builder.HasIndex(weighment => weighment.PartyId);
        builder.HasIndex(weighment => weighment.MaterialId);
        builder.HasIndex(weighment => weighment.VehicleTypeId);

        // Referential integrity for the master references, configured without navigations:
        // the aggregate deliberately carries text snapshots rather than object graphs, so
        // the relationships live only in persistence. DeleteBehavior.NoAction: masters are
        // retired (soft-deleted), never physically removed, so nothing should be able to
        // orphan or silently rewrite a slip by deleting the row behind it.
        builder.HasOne<Domain.Masters.Vehicle>()
            .WithMany()
            .HasForeignKey(weighment => weighment.VehicleId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Domain.Masters.Party>()
            .WithMany()
            .HasForeignKey(weighment => weighment.PartyId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Domain.Masters.Material>()
            .WithMany()
            .HasForeignKey(weighment => weighment.MaterialId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Domain.Masters.VehicleType>()
            .WithMany()
            .HasForeignKey(weighment => weighment.VehicleTypeId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(weighment => weighment.ReservationId);

        builder.HasIndex(weighment => weighment.ReservationId)
            .IsUnique()
            .HasDatabaseName("IX_Weighments_ReservationId")
            .HasFilter("[ReservationId] IS NOT NULL");

        builder.HasOne<TicketReservation>()
            .WithMany()
            .HasForeignKey(weighment => weighment.ReservationId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(weighment => weighment.Images)
            .WithOne()
            .HasForeignKey(img => img.WeighmentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Retired weighments stay in the table for the audit trail but are invisible to
        // every query, so no caller has to remember to exclude them. IgnoreQueryFilters()
        // is the deliberate escape hatch for an administrative screen that must see them.
        builder.HasQueryFilter(weighment => !weighment.IsDeleted);
    }

    private static void ConfigureCapture(
        EntityTypeBuilder<Weighment> builder,
        System.Linq.Expressions.Expression<Func<Weighment, WeightCapture?>> capture,
        string columnPrefix)
        => builder.OwnsOne(capture, capturebuilder =>
        {
            capturebuilder.Property(weight => weight.Kilograms)
                .HasConversion(KilogramsToGrams)
                .HasColumnName($"{columnPrefix}Grams");

            capturebuilder.Property(weight => weight.CapturedAtUtc)
                .HasColumnName($"{columnPrefix}AtUtc");

            capturebuilder.Property(weight => weight.Source)
                .HasColumnName($"{columnPrefix}Source");

            capturebuilder.Ignore(weight => weight.IsManual);
        });
}
