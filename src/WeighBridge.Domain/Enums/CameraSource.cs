namespace WeighBridge.Domain.Enums;

/// <summary>
/// Origin of a captured vehicle image.
/// </summary>
/// <remarks>
/// Distinguishes an image captured from a physical on-site camera from a simulator frame or
/// an offline fallback, ensuring transparent auditability.
/// </remarks>
public enum CameraSource
{
    /// <summary>Captured from a physical camera stream.</summary>
    Physical = 0,

    /// <summary>Synthesized by the development/testing camera simulator.</summary>
    Simulator = 1,

    /// <summary>Camera subsystem was disabled or unavailable.</summary>
    Disabled = 2,
}
