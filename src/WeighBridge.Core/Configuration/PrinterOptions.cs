namespace WeighBridge.Core.Configuration;

/// <summary>
/// Bound to the <c>Printer</c> section of <c>appsettings.json</c>. Consumed by the
/// Printing module for weighment slips.
/// </summary>
public sealed class PrinterOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Printer";

    /// <summary>Enables printing.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Printer Type: "Dot Matrix Printer", "Graphics Printer", or "Label / Sticker Printer".</summary>
    public string PrinterType { get; set; } = "Dot Matrix Printer";

    /// <summary>Side-wise printing mode.</summary>
    public bool SideWisePrinting { get; set; } = false;

    /// <summary>Windows printer name. Empty means "use the system default".</summary>
    public string DefaultPrinterName { get; set; } = string.Empty;

    /// <summary>Slip template identifier.</summary>
    public string SlipTemplate { get; set; } = "Default";

    /// <summary>Number of copies printed per slip.</summary>
    public int CopyCount { get; set; } = 2;

    /// <summary>Paper size name, e.g. <c>A4</c> or <c>Half A4 / A5</c>.</summary>
    public string PaperSize { get; set; } = "A4";

    /// <summary>Show the Windows print dialog instead of printing silently.</summary>
    public bool ShowPrintDialog { get; set; }

    /// <summary>Preview the slip before sending it to the printer.</summary>
    public bool PreviewBeforePrint { get; set; } = true;
}
