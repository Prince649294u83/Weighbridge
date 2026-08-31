using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Printing;
using WeighBridge.Printing.Template.Ast;

namespace WeighBridge.Printing.Outputs;

/// <summary>
/// Strategy contract for delivering rendered documents to a physical or virtual printer.
/// </summary>
public interface IPrintOutput
{
    /// <summary>The printer output mode handled by this implementation.</summary>
    PrinterOutputMode OutputMode { get; }

    /// <summary>
    /// Delivers the rendered document to the printer.
    /// </summary>
    Task<PrintResult> OutputAsync(
        string documentTitle,
        TemplateDocument document,
        WeighmentPrintData data,
        PrinterProfile profile,
        int copies = 1,
        CancellationToken cancellationToken = default);
}
