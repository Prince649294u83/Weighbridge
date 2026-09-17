using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using Microsoft.Extensions.Logging;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Printing;
using WeighBridge.Printing.Template;
using WeighBridge.Printing.Template.Ast;

namespace WeighBridge.Printing.Outputs;

/// <summary>
/// Windows GDI graphical print output driver that formats documents onto System.Drawing.Printing
/// off the UI thread with deterministic disposal of all native graphics resources.
/// </summary>
public sealed class WindowsGdiPrintOutput(
    ITemplateEngine templateEngine,
    ILogger<WindowsGdiPrintOutput> logger) : IPrintOutput
{
    private readonly ITemplateEngine _templateEngine = templateEngine;
    private readonly ILogger<WindowsGdiPrintOutput> _logger = logger;

    /// <inheritdoc />
    public PrinterOutputMode OutputMode => PrinterOutputMode.Gdi;

    /// <inheritdoc />
    public async Task<PrintResult> OutputAsync(
        string documentTitle,
        TemplateDocument document,
        WeighmentPrintData data,
        PrinterProfile profile,
        int copies = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(profile.PrinterName))
        {
            return PrintResult.Failure("No target printer specified for GDI printing.");
        }

        try
        {
            string renderedText = _templateEngine.RenderToText(document, data, profile);

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var printDoc = new PrintDocument();
                printDoc.DocumentName = documentTitle;
                printDoc.PrinterSettings.PrinterName = profile.PrinterName;
                printDoc.PrinterSettings.Copies = (short)Math.Max(1, copies);

                if (!printDoc.PrinterSettings.IsValid)
                {
                    _logger.LogWarning("Printer '{PrinterName}' is not valid or accessible.", profile.PrinterName);
                    return PrintResult.Failure($"Printer '{profile.PrinterName}' is not valid or accessible.");
                }

                using var reader = new StringReader(renderedText);

                printDoc.PrintPage += (s, e) =>
                {
                    if (e.Graphics is null) return;

                    // Audit-clean resource allocation: all fonts, brushes and pens scoped in using blocks
                    using var font = new Font("Courier New", 10, FontStyle.Regular);
                    using var boldFont = new Font("Courier New", 10, FontStyle.Bold);
                    using var brush = new SolidBrush(Color.Black);

                    float lineHeight = font.GetHeight(e.Graphics);
                    float y = e.MarginBounds.Top;
                    float x = e.MarginBounds.Left;

                    string? line;
                    while (y + lineHeight <= e.MarginBounds.Bottom && (line = reader.ReadLine()) is not null)
                    {
                        e.Graphics.DrawString(line, font, brush, x, y);
                        y += lineHeight;
                    }

                    e.HasMorePages = reader.Peek() != -1;
                };

                _logger.LogInformation("Sending GDI print job '{DocumentTitle}' to '{PrinterName}' ({Copies} copies)",
                    documentTitle, profile.PrinterName, copies);

                printDoc.Print();

                return PrintResult.Success($"Document sent to '{profile.PrinterName}'.");
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("GDI print job '{DocumentTitle}' was cancelled.", documentTitle);
            return PrintResult.Failure("Print operation was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to print '{DocumentTitle}' via GDI to '{PrinterName}'", documentTitle, profile.PrinterName);
            return PrintResult.Failure($"GDI printing failed: {ex.Message}");
        }
    }

    public async Task<PrintResult> OutputTextAsync(
        string documentTitle,
        string renderedText,
        PrinterProfile profile,
        int copies = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderedText);
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(profile.PrinterName))
        {
            return PrintResult.Failure("No target printer specified for GDI printing.");
        }

        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!PrinterSettings.InstalledPrinters.Cast<string>().Contains(profile.PrinterName, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Printer '{PrinterName}' is not installed.", profile.PrinterName);
                    return PrintResult.Failure($"Printer '{profile.PrinterName}' is not installed.");
                }

                using var printDoc = new PrintDocument();
                printDoc.DocumentName = documentTitle;
                printDoc.PrinterSettings.PrinterName = profile.PrinterName;
                printDoc.PrinterSettings.Copies = (short)Math.Max(1, copies);

                if (!printDoc.PrinterSettings.IsValid)
                {
                    _logger.LogWarning("Printer '{PrinterName}' is not valid or accessible.", profile.PrinterName);
                    return PrintResult.Failure($"Printer '{profile.PrinterName}' is not valid or accessible.");
                }

                using var reader = new StringReader(renderedText);

                printDoc.PrintPage += (s, e) =>
                {
                    if (e.Graphics is null) return;

                    using var font = new Font("Courier New", 8.5f, FontStyle.Regular);
                    using var brush = new SolidBrush(Color.Black);

                    float lineHeight = font.GetHeight(e.Graphics);
                    float y = e.MarginBounds.Top;
                    float x = e.MarginBounds.Left;

                    string? line;
                    while (y + lineHeight <= e.MarginBounds.Bottom && (line = reader.ReadLine()) is not null)
                    {
                        e.Graphics.DrawString(line, font, brush, x, y);
                        y += lineHeight;
                    }

                    e.HasMorePages = reader.Peek() != -1;
                };

                _logger.LogInformation("Sending GDI text print job '{DocumentTitle}' to '{PrinterName}' ({Copies} copies)",
                    documentTitle, profile.PrinterName, copies);

                printDoc.Print();

                return PrintResult.Success($"Document sent to '{profile.PrinterName}'.");
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("GDI text print job '{DocumentTitle}' was cancelled.", documentTitle);
            return PrintResult.Failure("Print operation was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to print text job '{DocumentTitle}' via GDI to '{PrinterName}'", documentTitle, profile.PrinterName);
            return PrintResult.Failure($"GDI printing failed: {ex.Message}");
        }
    }
}
