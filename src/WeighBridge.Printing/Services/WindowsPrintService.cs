using System.Drawing;
using System.Drawing.Printing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Security;

namespace WeighBridge.Printing.Services;

/// <summary>
/// A Windows-native print service that sends weighment slips to the configured printer using System.Drawing.Printing.
/// </summary>
public sealed class WindowsPrintService(
    IOptions<PrinterOptions> options,
    IPermissionService permissions,
    ILogger<WindowsPrintService> logger) : IPrintService
{
    private readonly PrinterOptions _options = options.Value;
    private readonly IPermissionService _permissions = permissions;
    private readonly ILogger<WindowsPrintService> _logger = logger;

    /// <inheritdoc />
    public string Name => "Printer";

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetAvailablePrintersAsync(CancellationToken cancellationToken = default)
    {
        var printers = new List<string>();
        foreach (string printer in PrinterSettings.InstalledPrinters)
        {
            printers.Add(printer);
        }
        return Task.FromResult<IReadOnlyList<string>>(printers);
    }

    /// <inheritdoc />
    public Task<string?> GetDefaultPrinterAsync(CancellationToken cancellationToken = default)
    {
        var settings = new PrinterSettings();
        var defaultPrinter = settings.IsDefaultPrinter ? settings.PrinterName : null;
        return Task.FromResult(string.IsNullOrWhiteSpace(_options.DefaultPrinterName)
            ? defaultPrinter
            : _options.DefaultPrinterName);
    }

    /// <inheritdoc />
    public async Task<PrintResult> PrintAsync(
        string documentKey,
        IReadOnlyDictionary<string, object?> data,
        string? printerName = null,
        int copies = 1,
        CancellationToken cancellationToken = default)
    {
        // Weighment.Reprint is declared and withheld from the ReadOnly role, and was enforced
        // nowhere: the Duplicate Slip screen is on the navigation rail for every role, so any
        // signed-in account could reissue any slip. Checked here rather than in that screen
        // because this method is what puts paper in someone's hand, and because "duplicate" is
        // already decided by this flag - the control and the DUPLICATE stamp below now read the
        // same field and cannot disagree. A first print carries no flag and is unaffected.
        if (IsDuplicate(data))
        {
            var authorization = _permissions.Authorize(Permissions.WeighmentReprint);
            if (!authorization.IsAuthorized)
            {
                _logger.LogWarning(
                    "Reprint of {DocumentKey} refused for {Operator} as {Role}: {Reason}",
                    documentKey,
                    _permissions.CurrentOperator.UserName,
                    _permissions.CurrentOperator.Role.Name,
                    authorization.Reason);

                return PrintResult.Failure(authorization.Reason ?? "You are not permitted to reprint a slip.");
            }
        }

        if (!_options.Enabled)
        {
            _logger.LogWarning("Printing is disabled in configuration.");
            return PrintResult.Failure("Printing is disabled.");
        }

        var targetPrinter = printerName ?? await GetDefaultPrinterAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(targetPrinter))
        {
            _logger.LogWarning("No printer specified and no default printer found.");
            return PrintResult.Failure("No printer specified and no default printer could be found.");
        }

        try
        {
            _logger.LogInformation("Printing {DocumentKey} to {PrinterName} ({Copies} copies)", documentKey, targetPrinter, copies);

            using var document = new PrintDocument();
            document.PrinterSettings.PrinterName = targetPrinter;
            document.PrinterSettings.Copies = (short)Math.Max(1, copies);

            if (!document.PrinterSettings.IsValid)
            {
                return PrintResult.Failure($"Printer '{targetPrinter}' is not valid or accessible.");
            }

            document.PrintPage += (s, e) =>
            {
                if (e.Graphics is null) return;
                DrawSlip(e.Graphics, e.MarginBounds, data);
            };

            document.Print();

            return PrintResult.Success($"Document sent to {targetPrinter}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to print {DocumentKey} to {PrinterName}", documentKey, targetPrinter);
            return PrintResult.Failure($"Printing failed: {ex.Message}");
        }
    }

    private static bool IsDuplicate(IReadOnlyDictionary<string, object?> data)
        => data.TryGetValue("IsDuplicate", out var value) && value is bool flag && flag;

    private void DrawSlip(Graphics g, Rectangle bounds, IReadOnlyDictionary<string, object?> data)
    {
        // Simple slip template drawing. Every GDI object is disposed before the page ends:
        // four leaked Font handles per slip used to drain the GDI handle pool on a
        // terminal that prints for weeks without restarting.
        using var titleFont = new Font("Arial", 16, FontStyle.Bold);
        using var headerFont = new Font("Arial", 12, FontStyle.Bold);
        using var normalFont = new Font("Arial", 10, FontStyle.Regular);
        using var labelFont = new Font("Arial", 10, FontStyle.Bold);

        using var brush = new SolidBrush(Color.Black);
        using var pen = new Pen(Color.Black, 1);

        float y = bounds.Top + 10;
        float left = bounds.Left + 10;
        float right = bounds.Right - 10;
        float middle = left + (right - left) / 2;

        // Title
        string title = IsDuplicate(data) ? "WEIGHMENT SLIP (DUPLICATE)" : "WEIGHMENT SLIP";
        var titleSize = g.MeasureString(title, titleFont);
        g.DrawString(title, titleFont, brush, left + (bounds.Width - titleSize.Width) / 2, y);
        y += 40;

        g.DrawLine(pen, left, y, right, y);
        y += 10;

        // Header info
        DrawField(g, left, ref y, "Slip No:", GetString(data, "SlipNumber"), labelFont, normalFont, brush);
        DrawField(g, middle, ref y, "Vehicle No:", GetString(data, "VehicleNumber"), labelFont, normalFont, brush, resetY: true);
        
        DrawField(g, left, ref y, "Date/Time In:", GetString(data, "TimeIn"), labelFont, normalFont, brush);
        DrawField(g, middle, ref y, "Date/Time Out:", GetString(data, "TimeOut"), labelFont, normalFont, brush, resetY: true);

        DrawField(g, left, ref y, "Party:", GetString(data, "PartyName"), labelFont, normalFont, brush);
        DrawField(g, middle, ref y, "Material:", GetString(data, "MaterialName"), labelFont, normalFont, brush, resetY: true);

        DrawField(g, left, ref y, "Driver:", GetString(data, "DriverName"), labelFont, normalFont, brush);
        DrawField(g, middle, ref y, "Transporter:", GetString(data, "TransporterName"), labelFont, normalFont, brush, resetY: true);

        y += 10;
        g.DrawLine(pen, left, y, right, y);
        y += 10;

        // Weights
        DrawField(g, left, ref y, "Gross Weight:", GetString(data, "GrossWeightKg") + " kg", labelFont, normalFont, brush);
        DrawField(g, middle, ref y, "Gross Time:", GetString(data, "GrossTime"), labelFont, normalFont, brush, resetY: true);

        DrawField(g, left, ref y, "Tare Weight:", GetString(data, "TareWeightKg") + " kg", labelFont, normalFont, brush);
        DrawField(g, middle, ref y, "Tare Time:", GetString(data, "TareTime"), labelFont, normalFont, brush, resetY: true);

        y += 10;
        DrawField(g, left, ref y, "Net Weight:", GetString(data, "NetWeightKg") + " kg", headerFont, headerFont, brush);

        y += 20;
        g.DrawLine(pen, left, y, right, y);
        y += 10;

        // Footer
        DrawField(g, left, ref y, "Remarks:", GetString(data, "Remarks"), labelFont, normalFont, brush);
        
        y += 60;
        g.DrawString("Operator Signature", normalFont, brush, left, y);
        g.DrawString("Driver Signature", normalFont, brush, right - 120, y);
    }

    private void DrawField(Graphics g, float x, ref float y, string label, string value, Font labelFont, Font valueFont, Brush brush, bool resetY = false)
    {
        g.DrawString(label, labelFont, brush, x, y);
        g.DrawString(value, valueFont, brush, x + 100, y);
        if (!resetY)
        {
            y += 25;
        }
    }

    private string GetString(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (data.TryGetValue(key, out var value) && value != null)
        {
            return value.ToString() ?? "-";
        }
        return "-";
    }

    /// <inheritdoc />
    public Task<HealthResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(HealthResult.Disabled("Printing disabled in configuration"));
        }

        try
        {
            var settings = new PrinterSettings();
            if (string.IsNullOrWhiteSpace(_options.DefaultPrinterName) && !settings.IsDefaultPrinter)
            {
                return Task.FromResult(HealthResult.Unreachable("No default printer configured or available."));
            }

            return Task.FromResult(HealthResult.Healthy("Printer is ready"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthResult.Unreachable($"Printer check failed: {ex.Message}"));
        }
    }
}
