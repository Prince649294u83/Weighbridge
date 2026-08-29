namespace WeighBridge.Domain.Enums;

/// <summary>
/// Policy governing zero net weight validation during second-weight recording.
/// </summary>
public enum NetWeightPolicy
{
    /// <summary>Gross must be strictly greater than Tare (Net > 0).</summary>
    RejectZero = 0,

    /// <summary>Gross must be greater than or equal to Tare (Net >= 0), permitting tare verification.</summary>
    AllowZero = 1,
}
