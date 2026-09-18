using System.Text;

namespace WeighBridge.Core.Printing;

/// <summary>
/// Output mode defining how the print engine interacts with the printer.
/// </summary>
public enum PrinterOutputMode
{
    /// <summary>Windows GDI graphical rendering via System.Drawing.Printing.</summary>
    Gdi,

    /// <summary>Raw byte stream sent directly to Windows Print Spooler (ESC/POS or plain ASCII).</summary>
    RawSpool
}

/// <summary>
/// Capability and transport profile for a physical or virtual printer.
/// Separates hardware capabilities from template document content.
/// </summary>
public sealed record PrinterProfile(
    string PrinterName,
    PrinterOutputMode OutputMode,
    Encoding Encoding,
    int PageWidthColumns = 80,
    int PageHeightLines = 66,
    bool SupportsCut = false,
    bool SupportsFormFeed = false,
    bool SupportsBold = true,
    string PhysicalPaperProfile = "Standard",
    bool SideWisePrinting = false
)
{
    /// <summary>Default profile for standard 80-column GDI laser/inkjet printers.</summary>
    public static PrinterProfile DefaultGdi(string printerName = "") => new(
        PrinterName: printerName,
        OutputMode: PrinterOutputMode.Gdi,
        Encoding: Encoding.UTF8,
        PageWidthColumns: 80,
        PageHeightLines: 66,
        SupportsCut: false,
        SupportsFormFeed: true,
        SupportsBold: true,
        PhysicalPaperProfile: "A4"
    );

    /// <summary>Default profile for 3-inch (80mm) Thermal POS receipt printers using ESC/POS.</summary>
    public static PrinterProfile Thermal80mm(string printerName = "", Encoding? encoding = null) => new(
        PrinterName: printerName,
        OutputMode: PrinterOutputMode.RawSpool,
        Encoding: encoding ?? Encoding.ASCII,
        PageWidthColumns: 48,
        PageHeightLines: 0,
        SupportsCut: true,
        SupportsFormFeed: false,
        SupportsBold: true,
        PhysicalPaperProfile: "80mm Thermal"
    );

    /// <summary>Default profile for continuous tractor-feed Dot Matrix printers.</summary>
    public static PrinterProfile DotMatrix(string printerName = "", Encoding? encoding = null) => new(
        PrinterName: printerName,
        OutputMode: PrinterOutputMode.RawSpool,
        Encoding: encoding ?? Encoding.ASCII,
        PageWidthColumns: 80,
        PageHeightLines: 66,
        SupportsCut: false,
        SupportsFormFeed: true,
        SupportsBold: true,
        PhysicalPaperProfile: "Continuous Tractor"
    );
}
