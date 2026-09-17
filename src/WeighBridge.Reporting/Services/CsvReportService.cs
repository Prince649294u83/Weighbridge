using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Reporting;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;

namespace WeighBridge.Reporting.Services;

/// <summary>
/// Writes weighment reports, canonical ReportDocuments, and export formats with injection protection and summary statistics.
/// </summary>
public sealed class CsvReportService(
    Func<IUnitOfWork> unitOfWork,
    IPermissionService permissions,
    IOptions<ReportingOptions> options,
    ILogger<CsvReportService> logger,
    IOptions<CompanyOptions>? companyOptions = null) : IReportService
{
    /// <summary>Hard ceiling even when configuration is misconfigured to something huge.</summary>
    private const int AbsoluteMaxRows = 100_000;

    public const string DailyReportKey = "DailyWeighments";
    public const string PartyReportKey = "PartyWeighments";
    public const string MaterialReportKey = "MaterialWeighments";
    public const string SummaryReportKey = "SummaryReport";

    private readonly Func<IUnitOfWork> _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    private readonly IPermissionService _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
    private readonly ReportingOptions _options = options.Value;
    private readonly CompanyOptions _companyOptions = companyOptions?.Value ?? new CompanyOptions();
    private readonly ILogger<CsvReportService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public IReadOnlyList<string> AvailableReports => [DailyReportKey, PartyReportKey, MaterialReportKey, SummaryReportKey];

    public IReadOnlyList<ReportFormat> SupportedFormats => [ReportFormat.Csv, ReportFormat.Excel, ReportFormat.Pdf];

    public async Task<ReportDocument> BuildDocumentAsync(
        ReportFilterParameters filters,
        CancellationToken cancellationToken = default)
    {
        var authorization = _permissions.Authorize(Permissions.ReportsExport);
        if (!authorization.IsAuthorized)
        {
            _logger.LogWarning(
                "Report document generation refused for {Operator} as {Role}: {Reason}",
                _permissions.CurrentOperator.UserName,
                _permissions.CurrentOperator.Role.Name,
                authorization.Reason);

            throw new UnauthorizedAccessException(authorization.Reason ?? "You are not permitted to access reports.");
        }

        ValidateFilters(filters);

        await using var scope = _unitOfWork();

        DateTime? startUtc = null;
        DateTime? endUtc = null;

        if (filters.StartDateLocal.HasValue)
        {
            startUtc = filters.StartDateLocal.Value.Date.ToUniversalTime();
        }

        if (filters.EndDateLocal.HasValue)
        {
            endUtc = filters.EndDateLocal.Value.Date.AddDays(1).ToUniversalTime();
        }

        var vehicleFilter = NormalizeTextFilter(filters.VehicleNumber);
        var partyFilter = NormalizeTextFilter(filters.PartyName);
        var materialFilter = NormalizeTextFilter(filters.MaterialName);
        var vehicleTypeFilter = NormalizeTextFilter(filters.VehicleTypeName);

        Expression<Func<Weighment, bool>> predicate = w =>
            w.Status == WeighmentStatus.Completed &&
            (!startUtc.HasValue || (w.CompletedAtUtc ?? w.CreatedAtUtc) >= startUtc.Value) &&
            (!endUtc.HasValue || (w.CompletedAtUtc ?? w.CreatedAtUtc) < endUtc.Value) &&
            (filters.StartSlipSequence == null || w.Id >= filters.StartSlipSequence.Value) &&
            (filters.EndSlipSequence == null || w.Id <= filters.EndSlipSequence.Value) &&
            (vehicleFilter == null || w.VehicleNumber.ToUpper().Contains(vehicleFilter)) &&
            (partyFilter == null || (w.PartyName != null && w.PartyName.ToUpper().Contains(partyFilter))) &&
            (materialFilter == null || (w.MaterialName != null && w.MaterialName.ToUpper().Contains(materialFilter))) &&
            (vehicleTypeFilter == null || (w.VehicleTypeName != null && w.VehicleTypeName.ToUpper().Contains(vehicleTypeFilter)));

        var maxRows = Math.Min(_options.MaxRowsPerReport, AbsoluteMaxRows);

        var weighments = await scope.Repository<Weighment>().QueryAsync(
            predicate,
            q => q.OrderBy(w => w.Id).Take(maxRows + 1),
            cancellationToken).ConfigureAwait(false);

        var rows = new List<ReportDocumentRow>(weighments.Count);
        int serial = 1;

        var startLocal = filters.StartDateLocal ?? DateTime.Today;
        var endLocal = filters.EndDateLocal ?? DateTime.Today;

        var isLimited = weighments.Count > maxRows;

        foreach (var w in weighments.Take(maxRows))
        {
            decimal grossKg = w.Gross?.Kilograms ?? 0m;
            decimal tareKg = w.Tare?.Kilograms ?? 0m;
            decimal netKg = w.NetWeightKg ?? 0m;

            rows.Add(new ReportDocumentRow(
                SerialNumber: serial++,
                SlipNumber: w.SlipNumber,
                VehicleNumber: w.VehicleNumber,
                VehicleTypeName: w.VehicleTypeName ?? string.Empty,
                PartyName: w.PartyName ?? string.Empty,
                MaterialName: w.MaterialName ?? string.Empty,
                Charges1: w.Charges,
                Charges2: w.SecondCharges,
                TotalCharges: w.Charges + w.SecondCharges,
                GrossWeightKg: grossKg,
                TareWeightKg: tareKg,
                NetWeightKg: netKg,
                GrossCapturedAtLocal: w.FirstWeight?.CapturedAtUtc.ToLocalTime() ?? w.CreatedAtUtc.ToLocalTime(),
                CompletedAtLocal: w.CompletedAtUtc?.ToLocalTime(),
                Status: w.Status.ToString()
            ));
        }

        return ReportDocument.Create(
            title: "WEIGHBRIDGE REPORT",
            companyName: _companyOptions.CompanyName ?? "WEIGHBRIDGE MODERN",
            startDateLocal: startLocal,
            endDateLocal: endLocal,
            rows: rows,
            isRowLimited: isLimited,
            maxRows: maxRows);
    }

    public async Task<ReportResult> ExportAsync(
        ReportDocument document,
        ReportFormat format,
        string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var authorization = _permissions.Authorize(Permissions.ReportsExport);
        if (!authorization.IsAuthorized)
        {
            return ReportResult.Failure(authorization.Reason ?? "You are not permitted to export reports.");
        }

        try
        {
            var dir = string.IsNullOrWhiteSpace(_options.OutputDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeighBridge", "Reports")
                : _options.OutputDirectory;

            Directory.CreateDirectory(dir);

            var ext = format switch
            {
                ReportFormat.Excel => "xlsx",
                ReportFormat.Pdf => "pdf",
                _ => "csv"
            };

            var fileName = string.IsNullOrEmpty(outputPath)
                ? $"Report_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}"
                : Path.GetFileName(outputPath);

            var finalPath = Path.Combine(dir, fileName);

            switch (format)
            {
                case ReportFormat.Excel:
                    await ExportExcelAsync(document, finalPath, cancellationToken).ConfigureAwait(false);
                    break;
                case ReportFormat.Pdf:
                    await ExportPdfAsync(document, finalPath, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    await ExportCsvAsync(document, finalPath, cancellationToken).ConfigureAwait(false);
                    break;
            }

            var message = document.IsRowLimited && document.MaxRows.HasValue
                ? $"Report exported successfully to {finalPath} (row limit applied: limited to {document.MaxRows.Value} rows; refine the report criteria for a complete result)."
                : $"Report exported successfully to {finalPath}";

            return ReportResult.Success(message, finalPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export report document to {Format}", format);
            return ReportResult.Failure(ex.Message);
        }
    }

    public async Task<ReportResult> GenerateAsync(
        string reportKey,
        IReadOnlyDictionary<string, object?> parameters,
        ReportFormat format,
        string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        var authorization = _permissions.Authorize(Permissions.ReportsExport);
        if (!authorization.IsAuthorized)
        {
            _logger.LogWarning(
                "Report {ReportKey} refused for {Operator} as {Role}: {Reason}",
                reportKey,
                _permissions.CurrentOperator.UserName,
                _permissions.CurrentOperator.Role.Name,
                authorization.Reason);

            return ReportResult.Failure(authorization.Reason ?? "You are not permitted to export reports.");
        }

        if (!AvailableReports.Contains(reportKey, StringComparer.OrdinalIgnoreCase))
        {
            return ReportResult.Failure(
                $"Unknown report '{reportKey}'. Available: {string.Join(", ", AvailableReports)}.");
        }

        try
        {
            var startDate = parameters.TryGetValue("StartDate", out var s) && s is DateTime sd ? sd : DateTime.Today;
            var endDate = parameters.TryGetValue("EndDate", out var e) && e is DateTime ed ? ed : DateTime.Today;

            var filters = new ReportFilterParameters(
                StartDateLocal: startDate,
                EndDateLocal: endDate,
                VehicleNumber: parameters.TryGetValue("VehicleNumber", out var v) && v is string vn ? vn : null,
                PartyName: parameters.TryGetValue("PartyName", out var p) && p is string pn ? pn : null,
                MaterialName: parameters.TryGetValue("MaterialName", out var m) && m is string mn ? mn : null,
                VehicleTypeName: parameters.TryGetValue("VehicleTypeName", out var vt) && vt is string vtn ? vtn : null);

            var doc = await BuildDocumentAsync(filters, cancellationToken).ConfigureAwait(false);

            if (string.Equals(reportKey, SummaryReportKey, StringComparison.OrdinalIgnoreCase) && format == ReportFormat.Csv)
            {
                var dir = string.IsNullOrWhiteSpace(_options.OutputDirectory)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeighBridge", "Reports")
                    : _options.OutputDirectory;
                Directory.CreateDirectory(dir);
                var finalPath = outputPath ?? Path.Combine(dir, $"SummaryReport_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                await ExportSummaryCsvAsync(doc, finalPath).ConfigureAwait(false);
                return ReportResult.Success($"Report exported successfully to {finalPath}", finalPath);
            }

            var exportResult = await ExportAsync(doc, format, outputPath, cancellationToken).ConfigureAwait(false);
            return exportResult;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate report {ReportKey}", reportKey);
            return ReportResult.Failure(ex.Message);
        }
    }

    private static async Task ExportSummaryCsvAsync(ReportDocument doc, string finalPath)
    {
        await using var writer = new StreamWriter(finalPath, false, Encoding.UTF8);
        await writer.WriteLineAsync("Party Name,Material,Trips,Gross (kg),Tare (kg),Net (kg),Total Charges");

        var groups = doc.Rows
            .GroupBy(r => (r.PartyName, r.MaterialName))
            .OrderBy(g => g.Key.PartyName).ThenBy(g => g.Key.MaterialName);

        int totalTrips = 0;
        decimal totalGross = 0;
        decimal totalTare = 0;
        decimal totalNet = 0;
        decimal totalCharges = 0;

        foreach (var g in groups)
        {
            var trips = g.Count();
            var gross = g.Sum(r => r.GrossWeightKg);
            var tare = g.Sum(r => r.TareWeightKg);
            var net = g.Sum(r => r.NetWeightKg);
            var charges = g.Sum(r => r.TotalCharges);

            totalTrips += trips;
            totalGross += gross;
            totalTare += tare;
            totalNet += net;
            totalCharges += charges;

            await writer.WriteLineAsync(
                $"{g.Key.PartyName},{g.Key.MaterialName},{trips},{gross.ToString("F1", CultureInfo.InvariantCulture)},{tare.ToString("F1", CultureInfo.InvariantCulture)},{net.ToString("F1", CultureInfo.InvariantCulture)},{charges.ToString("F2", CultureInfo.InvariantCulture)}");
        }

        await writer.WriteLineAsync($"Total Trips,{totalTrips},{totalGross.ToString("F1", CultureInfo.InvariantCulture)},{totalTare.ToString("F1", CultureInfo.InvariantCulture)},{totalNet.ToString("F1", CultureInfo.InvariantCulture)},{totalCharges.ToString("F2", CultureInfo.InvariantCulture)}");
    }

    private static async Task ExportCsvAsync(ReportDocument doc, string finalPath, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(finalPath, false, Encoding.UTF8);
        await writer.WriteLineAsync("Sr No,Slip No,Vehicle No,Vehicle Type,Party Name,Material,Charges 1st,Charges 2nd,Total Charges,Gross (kg),Tare (kg),Net (kg),Date & Time,Status");

        foreach (var r in doc.Rows)
        {
            var line = string.Join(',',
                r.SerialNumber.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(r.SlipNumber),
                EscapeCsv(r.VehicleNumber),
                EscapeCsv(r.VehicleTypeName),
                EscapeCsv(r.PartyName),
                EscapeCsv(r.MaterialName),
                r.Charges1.ToString(CultureInfo.InvariantCulture),
                r.Charges2.ToString(CultureInfo.InvariantCulture),
                r.TotalCharges.ToString(CultureInfo.InvariantCulture),
                r.GrossWeightKg.ToString(CultureInfo.InvariantCulture),
                r.TareWeightKg.ToString(CultureInfo.InvariantCulture),
                r.NetWeightKg.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(r.GrossCapturedAtLocal?.ToString("g", CultureInfo.InvariantCulture) ?? ""),
                EscapeCsv(r.Status));

            await writer.WriteLineAsync(line);
        }
    }

    private static async Task ExportExcelAsync(ReportDocument doc, string finalPath, CancellationToken cancellationToken)
    {
        await ExcelOpenXmlDocumentWriter.WriteAsync(doc, finalPath, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExportPdfAsync(ReportDocument doc, string finalPath, CancellationToken cancellationToken)
    {
        var pdfBytes = PdfReportDocumentWriter.GeneratePdf(doc);
        await File.WriteAllBytesAsync(finalPath, pdfBytes, cancellationToken).ConfigureAwait(false);
    }

    private static string EscapeXml(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    public static string EscapeCsv(string? field)
    {
        if (string.IsNullOrEmpty(field)) return "\"\"";

        bool needsFormulaProtection = field.StartsWith('=') || field.StartsWith('+') || field.StartsWith('-') || field.StartsWith('@') || field.StartsWith('\t') || field.StartsWith('\r');
        if (needsFormulaProtection)
        {
            field = "'" + field;
        }

        bool needsQuotes = needsFormulaProtection && (field.Contains(',') || field.Contains('"') || field.Contains('@'))
            || field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r');

        if (needsQuotes)
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }

    public static long ExtractSequenceNumber(string slipNumber)
    {
        if (string.IsNullOrWhiteSpace(slipNumber)) return 0;
        var digits = new string(slipNumber.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out var seq) ? seq : 0;
    }

    public static long? TryExtractSequenceNumber(string? slipNumber)
    {
        if (string.IsNullOrWhiteSpace(slipNumber))
        {
            return null;
        }

        var trimmed = slipNumber.Trim();
        var candidate = trimmed.StartsWith("WB-", StringComparison.OrdinalIgnoreCase)
            ? trimmed[3..]
            : trimmed;

        return candidate.Length > 0 && candidate.All(char.IsDigit) && long.TryParse(candidate, out var sequence)
            ? sequence
            : null;
    }

    private static void ValidateFilters(ReportFilterParameters filters)
    {
        if (filters.StartDateLocal.HasValue &&
            filters.EndDateLocal.HasValue &&
            filters.StartDateLocal.Value.Date > filters.EndDateLocal.Value.Date)
        {
            throw new ArgumentException("Start date cannot be after end date.", nameof(filters));
        }

        if (filters.StartSlipSequence is <= 0)
        {
            throw new ArgumentException("Starting slip number must be greater than zero.", nameof(filters));
        }

        if (filters.EndSlipSequence is <= 0)
        {
            throw new ArgumentException("Ending slip number must be greater than zero.", nameof(filters));
        }

        if (filters.StartSlipSequence.HasValue &&
            filters.EndSlipSequence.HasValue &&
            filters.StartSlipSequence.Value > filters.EndSlipSequence.Value)
        {
            throw new ArgumentException("Starting slip number cannot be greater than ending slip number.", nameof(filters));
        }
    }

    private static string? NormalizeTextFilter(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant();
}
