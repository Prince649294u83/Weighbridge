using System.Drawing.Printing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Printing;
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
    private readonly IPermissionService _permissions;
    private readonly ITemplateEngine _templateEngine;
    private readonly WindowsGdiPrintOutput _gdiOutput;
    private readonly RawSpoolPrintOutput _rawSpoolOutput;
    private readonly ILogger<WindowsPrintService> _logger;

    public WindowsPrintService(
        IOptions<PrinterOptions> options,
        IOptions<CompanyOptions> companyOptions,
        IPermissionService permissions,
        ITemplateEngine templateEngine,
        WindowsGdiPrintOutput gdiOutput,
        RawSpoolPrintOutput rawSpoolOutput,
        ILogger<WindowsPrintService> logger)
    {
        _options = options.Value;
        _companyOptions = companyOptions.Value;
        _permissions = permissions;
        _templateEngine = templateEngine;
        _gdiOutput = gdiOutput;
        _rawSpoolOutput = rawSpoolOutput;
        _logger = logger;
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

        if (!_options.Enabled)
        {
            _logger.LogWarning("Printing is disabled in configuration.");
            return PrintResult.Failure("Printing is disabled.");
        }

        var effectiveTemplateName = !string.IsNullOrWhiteSpace(templateName)
            ? templateName
            : _options.SlipTemplate;

        var targetPrinter = profile?.PrinterName ?? await GetDefaultPrinterAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(targetPrinter))
        {
            _logger.LogWarning("No target printer configured or available.");
            return PrintResult.Failure("No printer specified and no default printer could be found.");
        }

        // Determine profile
        var effectiveProfile = profile ?? ResolveDefaultProfile(effectiveTemplateName, targetPrinter);

        try
        {
            string templateContent = BuiltInTemplates.GetByName(effectiveTemplateName);
            var document = _templateEngine.Parse(templateContent, strictValidation: false);

            IPrintOutput outputDriver = effectiveProfile.OutputMode == PrinterOutputMode.RawSpool
                ? _rawSpoolOutput
                : _gdiOutput;

            string docTitle = $"Weighment Slip - {data.SlipNumber}";
            int effectiveCopies = copies > 0 ? copies : Math.Max(1, _options.CopyCount);

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
            NumberOfBags: null,
            BagWeightKg: null,
            TotalBagWeightKg: null,
            ActualWeightKg: null,
            FirstCharges: 0m,
            SecondCharges: 0m,
            TotalCharges: GetDecimal(data, "Charges"),
            OpenedAtLocal: DateTime.Now,
            CompletedAtLocal: DateTime.Now,
            OperatorUsername: _permissions.CurrentOperator.UserName,
            OperatorDisplayName: _permissions.CurrentOperator.DisplayName,
            Remarks: GetString(data, "Remarks"),
            IsDuplicate: isDuplicate,
            DuplicateWatermarkText: isDuplicate ? "WEIGHMENT SLIP (DUPLICATE)" : null,
            CompanyName: _companyOptions.CompanyName,
            AddressLine1: _companyOptions.AddressLine1,
            AddressLine2: _companyOptions.AddressLine2
        );

        string targetPrinter = printerName ?? await GetDefaultPrinterAsync(cancellationToken) ?? string.Empty;
        var profile = PrinterProfile.DefaultGdi(targetPrinter);

        return await PrintSlipAsync(printData, documentKey, profile, copies, cancellationToken);
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
