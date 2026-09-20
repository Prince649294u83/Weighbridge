using Microsoft.Extensions.Logging;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Printing;
using WeighBridge.Printing.Spooler;
using WeighBridge.Printing.Template;
using WeighBridge.Printing.Template.Ast;

namespace WeighBridge.Printing.Outputs;

/// <summary>
/// Raw spool printer driver strategy that submits ESC/POS or plain ASCII byte streams
/// directly to the Windows Print Spooler off the UI thread.
/// </summary>
public sealed class RawSpoolPrintOutput(
    ITemplateEngine templateEngine,
    ILogger<RawSpoolPrintOutput> logger) : IPrintOutput
{
    private readonly ITemplateEngine _templateEngine = templateEngine;
    private readonly ILogger<RawSpoolPrintOutput> _logger = logger;

    /// <inheritdoc />
    public PrinterOutputMode OutputMode => PrinterOutputMode.RawSpool;

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
            return PrintResult.Failure("No target printer name specified for raw spool printing.");
        }

        try
        {
            byte[] rawBytes = _templateEngine.RenderToBytes(document, data, profile);

            if (profile.PageHeightLines == 33)
            {
                // ESC C 33 (0x1B, 0x43, 0x21) sets page length to 33 lines for continuous tractor-feed half-A4 paper
                byte[] initCommands = [0x1B, 0x43, 33];
                byte[] trailing = rawBytes.Length > 0 && rawBytes[^1] == 0x0C ? [] : [0x0C];
                rawBytes = [..initCommands, ..rawBytes, ..trailing];
            }

            return await Task.Run(() =>
            {
                int effectiveCopies = Math.Max(1, copies);
                for (int i = 0; i < effectiveCopies; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _logger.LogInformation("Sending raw spool job ({Length} bytes, Copy {Copy}/{Total}) to printer '{PrinterName}'",
                        rawBytes.Length, i + 1, effectiveCopies, profile.PrinterName);

                    Win32PrintSpooler.SendBytesToPrinter(profile.PrinterName, documentTitle, rawBytes);
                }

                return PrintResult.Success($"Raw print job sent to '{profile.PrinterName}' ({effectiveCopies} copies).");
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Raw spool print job for '{PrinterName}' was cancelled.", profile.PrinterName);
            return PrintResult.Failure("Print operation was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit raw spool job to printer '{PrinterName}'", profile.PrinterName);
            return PrintResult.Failure($"Raw spooling failed: {ex.Message}");
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
            return PrintResult.Failure("No target printer name specified for raw spool printing.");
        }

        try
        {
            byte[] rawBytes = profile.Encoding.GetBytes(renderedText);

            if (profile.PageHeightLines == 33)
            {
                // ESC C 33 (0x1B, 0x43, 0x21) sets page length to 33 lines for continuous tractor-feed half-A4 paper
                byte[] initCommands = [0x1B, 0x43, 33];
                byte[] trailing = rawBytes.Length > 0 && rawBytes[^1] == 0x0C ? [] : [0x0C];
                rawBytes = [..initCommands, ..rawBytes, ..trailing];
            }

            return await Task.Run(() =>
            {
                int effectiveCopies = Math.Max(1, copies);
                for (int i = 0; i < effectiveCopies; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _logger.LogInformation("Sending raw text spool job ({Length} bytes, Copy {Copy}/{Total}) to printer '{PrinterName}'",
                        rawBytes.Length, i + 1, effectiveCopies, profile.PrinterName);

                    Win32PrintSpooler.SendBytesToPrinter(profile.PrinterName, documentTitle, rawBytes);
                }

                return PrintResult.Success($"Raw print job sent to '{profile.PrinterName}' ({effectiveCopies} copies).");
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Raw text spool print job for '{PrinterName}' was cancelled.", profile.PrinterName);
            return PrintResult.Failure("Print operation was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit raw text spool job to printer '{PrinterName}'", profile.PrinterName);
            return PrintResult.Failure($"Raw spooling failed: {ex.Message}");
        }
    }
}
