using WeighBridge.Domain.Common;
using WeighBridge.Domain.Enums;

namespace WeighBridge.Domain.Weighments;

/// <summary>
/// Metadata for an image captured during a weighment transaction.
/// </summary>
/// <remarks>
/// The binary JPEG payload is stored as an individual file under the configured
/// <c>Captures</c> directory, while this entity retains the provenance, stage, timestamp,
/// relative path, and SHA-256 integrity checksum for auditability.
/// </remarks>
public sealed class WeighmentImage : EntityBase, IAggregateRoot
{
    public const int CameraNameMaxLength = 64;
    public const int FilePathMaxLength = 260;
    public const int ChecksumMaxLength = 64;

    /// <summary>For EF Core materialisation only.</summary>
    private WeighmentImage()
    {
    }

    /// <summary>Creates a new weighment image metadata record.</summary>
    public static WeighmentImage Create(
        long weighmentId,
        string cameraName,
        string stage,
        CameraSource source,
        string relativeFilePath,
        DateTime capturedAtUtc,
        long fileSizeBytes,
        string? sha256Checksum = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cameraName);
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeFilePath);

        var trimmedCamera = cameraName.Trim();
        if (trimmedCamera.Length > CameraNameMaxLength)
        {
            throw new ArgumentException($"Camera name must be {CameraNameMaxLength} characters or fewer.", nameof(cameraName));
        }

        var trimmedPath = relativeFilePath.Trim();
        if (trimmedPath.Length > FilePathMaxLength)
        {
            throw new ArgumentException($"File path must be {FilePathMaxLength} characters or fewer.", nameof(relativeFilePath));
        }

        var trimmedChecksum = string.IsNullOrWhiteSpace(sha256Checksum) ? null : sha256Checksum.Trim();
        if (trimmedChecksum?.Length > ChecksumMaxLength)
        {
            throw new ArgumentException($"Checksum must be {ChecksumMaxLength} characters or fewer.", nameof(sha256Checksum));
        }

        return new WeighmentImage
        {
            WeighmentId = weighmentId,
            CameraName = trimmedCamera,
            Stage = stage.Trim(),
            Source = source,
            RelativeFilePath = trimmedPath,
            CapturedAtUtc = capturedAtUtc,
            FileSizeBytes = fileSizeBytes >= 0 ? fileSizeBytes : 0,
            Sha256Checksum = trimmedChecksum,
        };
    }

    /// <summary>Foreign key to the parent weighment.</summary>
    public long WeighmentId { get; private set; }

    /// <summary>Name of the camera device (e.g. "Camera 1", "Front", "Overhead").</summary>
    public string CameraName { get; private set; } = string.Empty;

    /// <summary>Transaction stage when captured (e.g. "FirstWeight", "SecondWeight").</summary>
    public string Stage { get; private set; } = string.Empty;

    /// <summary>Origin of the image (Physical, Simulator, Disabled).</summary>
    public CameraSource Source { get; private set; }

    /// <summary>File path relative to the application captures root directory.</summary>
    public string RelativeFilePath { get; private set; } = string.Empty;

    /// <summary>UTC timestamp when the frame was captured.</summary>
    public DateTime CapturedAtUtc { get; private set; }

    /// <summary>File size in bytes.</summary>
    public long FileSizeBytes { get; private set; }

    /// <summary>Optional SHA-256 hash of the image file for integrity verification.</summary>
    public string? Sha256Checksum { get; private set; }
}
