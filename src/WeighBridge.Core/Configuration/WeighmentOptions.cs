namespace WeighBridge.Core.Configuration;

/// <summary>
/// Bound to the <c>Weighment</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class WeighmentOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Weighment";

    /// <summary>
    /// Whether zero net weight (Gross == Tare) is permitted upon second weight capture.
    /// Default is <c>false</c> (RejectZero).
    /// </summary>
    public bool AllowZeroNetWeight { get; set; } = false;
}
