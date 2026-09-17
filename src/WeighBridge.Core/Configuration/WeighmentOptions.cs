namespace WeighBridge.Core.Configuration;

/// <summary>
/// Bound to the <c>Weighment</c> section of <c>appsettings.json</c>.
/// Controls operational workflow rules, charges, and auxiliary behavior.
/// </summary>
public sealed class WeighmentOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Weighment";

    /// <summary>Whether zero net weight (Gross == Tare) is permitted upon second weight capture.</summary>
    public bool AllowZeroNetWeight { get; set; } = false;

    /// <summary>Show unit bags weight deduction column.</summary>
    public bool UnitBagsWeightColumn { get; set; } = false;

    /// <summary>Allow manual tare weight input.</summary>
    public bool ManualTareEntry { get; set; } = true;

    /// <summary>Automatically populate standard vehicle tare weight.</summary>
    public bool AutoTareWeight { get; set; } = true;

    /// <summary>Enable second entry charges field.</summary>
    public bool SecondEntryCharges { get; set; } = false;

    /// <summary>Apply GST calculation to weighing charges.</summary>
    public bool GstOnCharges { get; set; } = false;

    /// <summary>Restrict operator to single-entry weighments.</summary>
    public bool OnlySingleEntry { get; set; } = false;

    /// <summary>Enable price computing calculation.</summary>
    public bool PriceComputing { get; set; } = false;

    /// <summary>Inactivity disconnect timeout in seconds.</summary>
    public int DisconnectTimeSeconds { get; set; } = 300;

    /// <summary>Create desktop application shortcut automatically.</summary>
    public bool AutoApplicationShortcut { get; set; } = true;

    /// <summary>Automatically update vehicle master tare weight from captured tare.</summary>
    public bool AutoUpdateTareWeight { get; set; } = false;

    /// <summary>Enable weight hold mode during fluctuating scale readings.</summary>
    public bool WeightHold { get; set; } = false;

    /// <summary>Clock time format: "12 Hour" or "24 Hour".</summary>
    public string TimeFormat { get; set; } = "12 Hour";

    /// <summary>Print QR code verification watermark on slip.</summary>
    public bool PrintQrCode { get; set; } = false;

    /// <summary>Require non-zero charges before completion.</summary>
    public bool ChargesMandatory { get; set; } = false;

    /// <summary>Minimum weighing charge amount in Rupees.</summary>
    public decimal MinimumCharges { get; set; } = 0m;
}
