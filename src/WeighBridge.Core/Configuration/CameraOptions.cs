namespace WeighBridge.Core.Configuration;

/// <summary>
/// Bound to the <c>Camera</c> section of <c>appsettings.json</c>. Consumed by the
/// vehicle image capture module.
/// </summary>
public sealed class CameraOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Camera";

    /// <summary>Enables image capture.</summary>
    public bool Enabled { get; set; }

    /// <summary>Capture an image automatically when a weight is committed.</summary>
    public bool CaptureOnWeighment { get; set; } = true;

    /// <summary>JPEG quality (1-100) used when storing captures.</summary>
    public int ImageQuality { get; set; } = 80;

    /// <summary>How long captures are retained before cleanup.</summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>Single camera operational mode setting retained for legacy schema parity.</summary>
    public bool SingleCameraMode { get; set; } = false;

    /// <summary>Configured camera devices.</summary>
    public IList<CameraDeviceOptions> Devices { get; set; } = [];
}

/// <summary>A single camera device.</summary>
public sealed class CameraDeviceOptions
{
    /// <summary>Friendly name shown in the UI, e.g. "Front".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Stream source, typically an RTSP or HTTP URL.</summary>
    public string StreamUrl { get; set; } = string.Empty;

    /// <summary>Optional credentials for the stream.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Optional credentials for the stream.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Enables this individual device.</summary>
    public bool Enabled { get; set; } = true;
}
