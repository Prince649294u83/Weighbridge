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
            rawBytes = FrameEscpPayload(rawBytes, profile);

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
            rawBytes = FrameEscpPayload(rawBytes, profile);

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

    private static byte[] FrameEscpPayload(byte[] rawBytes, PrinterProfile profile)
    {
        var list = new List<byte>(rawBytes.Length + 16);

        // ESC @ (0x1B, 0x40): Initialize printer to wake print head and reset state
        list.Add(0x1B);
        list.Add(0x40);

        // ESC C n: Set page length in lines (33 lines for half A4, 66 for full A4)
        if (profile.PageHeightLines is 33 or 66)
        {
            list.Add(0x1B);
            list.Add(0x43);
            list.Add((byte)profile.PageHeightLines);
        }

        // SI (0x0F): Condensed font mode for wide slips/reports on 80-column carriage printers
        if (profile.PageWidthColumns > 80 || profile.SideWisePrinting)
        {
            list.Add(0x0F);
        }

        list.AddRange(rawBytes);

        // Always end with Form Feed (0x0C) so printer flushes internal RAM buffer and advances/ejects paper
        if (list.Count == 0 || list[^1] != 0x0C)
        {
            list.Add(0x0C);
        }

        return list.ToArray();
    }
}
