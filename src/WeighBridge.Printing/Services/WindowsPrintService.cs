using System.Drawing.Printing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Printing;
using WeighBridge.Core.Reporting;
using WeighBridge.Core.Security;
using WeighBridge.Printing.Outputs;
using WeighBridge.Printing.Template;

namespace WeighBridge.Printing.Services;

/// <summary>
/// Authoritative Windows print service implementing both GDI graphical printing and Win32 raw spooling
/// using AST-parsed templates and calculation-free <see cref="WeighmentPrintData"/>.
/// </summary>
public sealed class WindowsPrintService : IPrintService
{
    private readonly PrinterOptions _options;
    private readonly CompanyOptions _companyOptions;
    private readonly IOptionsMonitor<PrinterOptions>? _optionsMonitor;
    private readonly IOptionsMonitor<CompanyOptions>? _companyOptionsMonitor;
    private readonly IDateTimeFormatter? _dateTimeFormatter;
    private readonly IPermissionService _permissions;
    private readonly ITemplateEngine _templateEngine;
    private readonly WindowsGdiPrintOutput _gdiOutput;
    private readonly RawSpoolPrintOutput _rawSpoolOutput;
    private readonly ILogger<WindowsPrintService> _logger;
    private int _reportPrintInProgress;

    public WindowsPrintService(
        IOptions<PrinterOptions> options,
        IOptions<CompanyOptions> companyOptions,
        IPermissionService permissions,
        ITemplateEngine templateEngine,
        WindowsGdiPrintOutput gdiOutput,
        RawSpoolPrintOutput rawSpoolOutput,
        ILogger<WindowsPrintService> logger,
        IOptionsMonitor<PrinterOptions>? optionsMonitor = null,
        IOptionsMonitor<CompanyOptions>? companyOptionsMonitor = null,
        IDateTimeFormatter? dateTimeFormatter = null)
    {
        _options = options.Value;
        _companyOptions = companyOptions.Value;
        _permissions = permissions;
        _templateEngine = templateEngine;
        _gdiOutput = gdiOutput;
        _rawSpoolOutput = rawSpoolOutput;
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _companyOptionsMonitor = companyOptionsMonitor;
        _dateTimeFormatter = dateTimeFormatter;
    }

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
        return Task.FromResult<IReadOnlyList<string>>(
            printers.OrderBy(printer => printer, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    /// <inheritdoc />
    public Task<string?> GetDefaultPrinterAsync(CancellationToken cancellationToken = default)
    {
        var settings = new PrinterSettings();
        var defaultPrinter = settings.IsDefaultPrinter ? settings.PrinterName : null;
        var options = CurrentPrinterOptions;
        return Task.FromResult(string.IsNullOrWhiteSpace(options.DefaultPrinterName)
            ? defaultPrinter
            : options.DefaultPrinterName);
    }

    /// <summary>
    /// Prints a weighment slip using canonical print data, resolving the template and printer profile.
    /// </summary>
    public async Task<PrintResult> PrintSlipAsync(
        WeighmentPrintData data,
        string? templateName = null,
        PrinterProfile? profile = null,
        int copies = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        // Security check for duplicate slip reprinting
        if (data.IsDuplicate)
        {
            var authorization = _permissions.Authorize(Permissions.WeighmentReprint);
            if (!authorization.IsAuthorized)
            {
                _logger.LogWarning(
                    "Reprint of slip {SlipNumber} refused for {Operator} as {Role}: {Reason}",
                    data.SlipNumber,
                    _permissions.CurrentOperator.UserName,
                    _permissions.CurrentOperator.Role.Name,
                    authorization.Reason);

                return PrintResult.Failure(authorization.Reason ?? "You are not permitted to reprint a slip.");
            }
        }

        var options = CurrentPrinterOptions;
        if (!options.Enabled)
        {
            _logger.LogWarning("Printing is disabled in configuration.");
            return PrintResult.Failure("Printing is disabled.");
        }

        var effectiveTemplateName = !string.IsNullOrWhiteSpace(templateName)
            ? templateName
            : options.SlipTemplate;

        var targetPrinter = profile?.PrinterName ?? await GetDefaultPrinterAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(targetPrinter))
        {
            _logger.LogWarning("No target printer configured or available.");
            return PrintResult.Failure("No printer specified and no default printer could be found.");
        }

        if (!IsInstalledPrinter(targetPrinter))
        {
            return PrintResult.Failure($"Printer '{targetPrinter}' is not installed.");
        }

        // Determine profile
        var effectiveProfile = profile ?? ResolveDefaultProfile(effectiveTemplateName, targetPrinter);
        if (options.SideWisePrinting && !effectiveProfile.SideWisePrinting)
        {
            effectiveProfile = effectiveProfile with { SideWisePrinting = true };
        }

        try
        {
            string templateContent = BuiltInTemplates.GetByName(effectiveTemplateName);
            var document = _templateEngine.Parse(templateContent, strictValidation: false);

            IPrintOutput outputDriver = effectiveProfile.OutputMode == PrinterOutputMode.RawSpool
                ? _rawSpoolOutput
                : _gdiOutput;

            string docTitle = $"Weighment Slip - {data.SlipNumber}";
            int effectiveCopies = copies > 0 ? copies : Math.Max(1, options.CopyCount);

            _logger.LogInformation("Executing print job for {SlipNumber} on '{PrinterName}' via {OutputMode} ({Copies} copies)",
                data.SlipNumber, effectiveProfile.PrinterName, effectiveProfile.OutputMode, effectiveCopies);

            return await outputDriver.OutputAsync(docTitle, document, data, effectiveProfile, effectiveCopies, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to print weighment slip {SlipNumber} to '{PrinterName}'", data.SlipNumber, targetPrinter);
            return PrintResult.Failure($"Printing failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<PrintResult> PrintAsync(
        string documentKey,
        IReadOnlyDictionary<string, object?> data,
        string? printerName = null,
        int copies = 1,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(documentKey, "ReportDocument", StringComparison.OrdinalIgnoreCase))
        {
            return data.TryGetValue("ReportDocument", out var document) && document is ReportDocument report
                ? await PrintReportAsync(report, printerName, copies, cancellationToken)
                : PrintResult.Failure("Report printing requires a canonical ReportDocument.");
        }

        bool isDuplicate = data.TryGetValue("IsDuplicate", out var val) && val is bool b && b;

        var printData = new WeighmentPrintData(
            WeighmentId: 0,
            SlipNumber: GetString(data, "SlipNumber"),
            VehicleNumber: GetString(data, "VehicleNumber"),
            VehicleTypeName: GetString(data, "VehicleTypeName"),
            PartyName: GetString(data, "PartyName"),
            MaterialName: GetString(data, "MaterialName"),
            DriverName: GetString(data, "DriverName"),
            TransporterName: GetString(data, "TransporterName"),
            GatePassNumber: GetString(data, "GatePassNumber"),
            CustomField1: GetString(data, "CustomField1"),
            CustomField2: GetString(data, "CustomField2"),
            CustomField3: GetString(data, "CustomField3"),
            CustomField4: GetString(data, "CustomField4"),
            GrossWeightKg: GetDecimal(data, "GrossWeightKg"),
            GrossCapturedAtLocal: null,
            TareWeightKg: GetDecimal(data, "TareWeightKg"),
            TareCapturedAtLocal: null,
            NetWeightKg: GetDecimal(data, "NetWeightKg"),
            NumberOfBags: GetNullableInt(data, "NumberOfBags"),
            BagWeightKg: GetNullableDecimal(data, "BagWeightKg"),
            TotalBagWeightKg: GetNullableDecimal(data, "TotalBagWeightKg"),
            ActualWeightKg: GetNullableDecimal(data, "ActualWeightKg"),
            FirstCharges: GetDecimal(data, "FirstCharges"),
            SecondCharges: GetDecimal(data, "SecondCharges"),
            TotalCharges: GetDecimal(data, "Charges"),
            OpenedAtLocal: DateTime.Now,
            CompletedAtLocal: DateTime.Now,
            OperatorUsername: _permissions.CurrentOperator.UserName,
            OperatorDisplayName: _permissions.CurrentOperator.DisplayName,
            Remarks: GetString(data, "Remarks"),
            IsDuplicate: isDuplicate,
            DuplicateWatermarkText: isDuplicate ? "WEIGHMENT SLIP (DUPLICATE)" : null,
            CompanyName: CurrentCompanyOptions.CompanyName,
            AddressLine1: CurrentCompanyOptions.AddressLine1,
            AddressLine2: CurrentCompanyOptions.AddressLine2
        );

        string targetPrinter = printerName ?? await GetDefaultPrinterAsync(cancellationToken) ?? string.Empty;
        var profile = PrinterProfile.DefaultGdi(targetPrinter);

        return await PrintSlipAsync(printData, documentKey, profile, copies, cancellationToken);
    }

    private async Task<PrintResult> PrintReportAsync(
        ReportDocument document,
        string? printerName,
        int copies,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (Interlocked.Exchange(ref _reportPrintInProgress, 1) == 1)
        {
            return PrintResult.Failure("A report print job is already being submitted. Please wait for it to finish.");
        }

        try
        {
            var options = CurrentPrinterOptions;
            if (!options.Enabled)
            {
                _logger.LogWarning("Report printing refused because printing is disabled in configuration.");
                return PrintResult.Failure("Printing is disabled.");
            }

            var targetPrinter = !string.IsNullOrWhiteSpace(printerName)
                ? printerName
                : await GetDefaultPrinterAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(targetPrinter))
            {
                _logger.LogWarning("Report printing refused because no target printer is configured or available.");
                return PrintResult.Failure("No printer specified and no default printer could be found.");
            }

            if (!IsInstalledPrinter(targetPrinter))
            {
                return PrintResult.Failure($"Printer '{targetPrinter}' is not installed.");
            }

            var profile = ResolveReportProfile(targetPrinter);
            int effectiveCopies = copies > 0 ? copies : Math.Max(1, options.CopyCount);
            string renderedText = ReportPrintTextRenderer.Render(document, options.PaperSize, options.SideWisePrinting, _dateTimeFormatter);
            string title = $"{document.Title} - {document.StartDateLocal:yyyyMMdd}-{document.EndDateLocal:yyyyMMdd}";

            _logger.LogInformation(
                "Submitting report print job '{Title}' to '{PrinterName}' via {OutputMode} ({Copies} copies, PaperSize={PaperSize}, SideWise={SideWise})",
                title,
                profile.PrinterName,
                profile.OutputMode,
                effectiveCopies,
                options.PaperSize,
                options.SideWisePrinting);

            return profile.OutputMode == PrinterOutputMode.RawSpool
                ? await _rawSpoolOutput.OutputTextAsync(title, renderedText, profile, effectiveCopies, cancellationToken)
                : await _gdiOutput.OutputTextAsync(title, renderedText, profile, effectiveCopies, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to print canonical report document.");
            return PrintResult.Failure($"Report printing failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _reportPrintInProgress, 0);
        }
    }

    private static PrinterProfile ResolveDefaultProfile(string templateName, string printerName)
    {
        if (string.Equals(templateName, "thermal", StringComparison.OrdinalIgnoreCase))
        {
            return PrinterProfile.Thermal80mm(printerName);
        }
        if (string.Equals(templateName, "dot", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(templateName, "dotmatrix", StringComparison.OrdinalIgnoreCase))
        {
            return PrinterProfile.DotMatrix(printerName);
        }
        return PrinterProfile.DefaultGdi(printerName);
    }

    private PrinterProfile ResolveReportProfile(string printerName)
    {
        var options = CurrentPrinterOptions;
        PrinterProfile profile;
        if (options.PrinterType.Contains("Dot Matrix", StringComparison.OrdinalIgnoreCase))
        {
            profile = PrinterProfile.DotMatrix(printerName);
        }
        else if (options.PrinterType.Contains("Label", StringComparison.OrdinalIgnoreCase) ||
            options.PrinterType.Contains("Sticker", StringComparison.OrdinalIgnoreCase))
        {
            profile = PrinterProfile.Thermal80mm(printerName);
        }
        else
        {
            profile = PrinterProfile.DefaultGdi(printerName);
        }

        if (options.SideWisePrinting)
        {
            profile = profile with { SideWisePrinting = true };
        }

        return profile;
    }

    private static string GetString(IReadOnlyDictionary<string, object?> data, string key)
        => data.TryGetValue(key, out var v) && v != null ? v.ToString() ?? "-" : "-";

    private static decimal GetDecimal(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (data.TryGetValue(key, out var v) && v != null)
        {
            if (v is decimal d) return d;
            if (decimal.TryParse(v.ToString(), out var parsed)) return parsed;
        }
        return 0m;
    }

    private static int? GetNullableInt(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (data.TryGetValue(key, out var v) && v != null)
        {
            if (v is int i) return i;
            if (int.TryParse(v.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static decimal? GetNullableDecimal(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (data.TryGetValue(key, out var v) && v != null)
        {
            if (v is decimal d) return d;
            if (decimal.TryParse(v.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    /// <inheritdoc />
    public Task<HealthResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var options = CurrentPrinterOptions;
        if (!options.Enabled)
        {
            return Task.FromResult(HealthResult.Disabled("Printing disabled in configuration"));
        }

        try
        {
            var settings = new PrinterSettings();
            if (string.IsNullOrWhiteSpace(options.DefaultPrinterName) && !settings.IsDefaultPrinter)
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

    private PrinterOptions CurrentPrinterOptions => _optionsMonitor?.CurrentValue ?? _options;

    private CompanyOptions CurrentCompanyOptions => _companyOptionsMonitor?.CurrentValue ?? _companyOptions;

    private static bool IsInstalledPrinter(string printerName)
        => PrinterSettings.InstalledPrinters.Cast<string>().Contains(printerName, StringComparer.OrdinalIgnoreCase);
}
