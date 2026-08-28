using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WeighBridge.Domain.Weighments;

namespace WeighBridge.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="WeighmentImage"/>.
/// </summary>
public sealed class WeighmentImageConfiguration : IEntityTypeConfiguration<WeighmentImage>
{
    public void Configure(EntityTypeBuilder<WeighmentImage> builder)
    {
        builder.ToTable("WeighmentImages");

        builder.HasKey(img => img.Id);

        builder.Property(img => img.CameraName)
            .IsRequired()
            .HasMaxLength(WeighmentImage.CameraNameMaxLength);

        builder.Property(img => img.Stage)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(img => img.Source)
            .IsRequired()
            // Integer storage to match every other persisted enum in this schema; the
            // original string form made this one column the odd one out for reporting SQL.
            .HasConversion<int>();

        builder.Property(img => img.RelativeFilePath)
            .IsRequired()
            .HasMaxLength(WeighmentImage.FilePathMaxLength);

        builder.Property(img => img.CapturedAtUtc)
            .IsRequired();

        builder.Property(img => img.FileSizeBytes)
            .IsRequired();

        builder.Property(img => img.Sha256Checksum)
            .HasMaxLength(WeighmentImage.ChecksumMaxLength);

        builder.HasIndex(img => img.WeighmentId);
        builder.HasIndex(img => img.CapturedAtUtc);
    }
}
