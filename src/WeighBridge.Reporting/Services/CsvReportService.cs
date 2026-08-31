using System.Globalization;
using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Reporting;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Weighments;

namespace WeighBridge.Reporting.Services;

/// <summary>
/// Writes weighment reports as hardened CSV files with injection protection and summary statistics.
/// </summary>
public sealed class CsvReportService(
    Func<IUnitOfWork> unitOfWork,
    IPermissionService permissions,
    IOptions<ReportingOptions> options,
    ILogger<CsvReportService> logger) : IReportService
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
    private readonly ILogger<CsvReportService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public IReadOnlyList<string> AvailableReports => [DailyReportKey, PartyReportKey, MaterialReportKey, SummaryReportKey];

    public IReadOnlyList<ReportFormat> SupportedFormats => [ReportFormat.Csv];

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

        if (format != ReportFormat.Csv)
        {
            return ReportResult.Failure($"Format {format} is not supported by {nameof(CsvReportService)}.");
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

            if (endDate < startDate)
            {
                return ReportResult.Failure("The end date cannot be before the start date.");
            }

            var partyName = parameters.TryGetValue("PartyName", out var p) && p is string pn ? pn : null;
            var materialName = parameters.TryGetValue("MaterialName", out var m) && m is string mn ? mn : null;

            var maxRows = Math.Clamp(_options.MaxRowsPerReport, 1, AbsoluteMaxRows);

            await using var scope = _unitOfWork();

            Expression<Func<Weighment, bool>> filter = reportKey switch
            {
                PartyReportKey when !string.IsNullOrWhiteSpace(partyName) =>
                    w => w.CompletedAtUtc != null &&
                         w.CompletedAtUtc >= startDate.ToUniversalTime() &&
                         w.CompletedAtUtc < endDate.AddDays(1).ToUniversalTime() &&
                         w.PartyName == partyName,

                MaterialReportKey when !string.IsNullOrWhiteSpace(materialName) =>
                    w => w.CompletedAtUtc != null &&
                         w.CompletedAtUtc >= startDate.ToUniversalTime() &&
                         w.CompletedAtUtc < endDate.AddDays(1).ToUniversalTime() &&
                         w.MaterialName == materialName,

                // Daily report and Summary report filter on the date window
                _ =>
                    w => w.CompletedAtUtc != null &&
                         w.CompletedAtUtc >= startDate.ToUniversalTime() &&
                         w.CompletedAtUtc < endDate.AddDays(1).ToUniversalTime(),
            };

            var dir = string.IsNullOrEmpty(outputPath) ? _options.OutputDirectory : Path.GetDirectoryName(outputPath) ?? _options.OutputDirectory;
            Directory.CreateDirectory(dir);

            var fileName = string.IsNullOrEmpty(outputPath)
                ? $"{reportKey}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                : Path.GetFileName(outputPath);

            var finalPath = Path.Combine(dir, fileName);

            if (string.Equals(reportKey, SummaryReportKey, StringComparison.OrdinalIgnoreCase))
            {
                return await GenerateSummaryReportAsync(scope, filter, maxRows, finalPath, cancellationToken);
            }

            return await GenerateDetailedReportAsync(reportKey, scope, filter, maxRows, finalPath, cancellationToken);
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

    private async Task<ReportResult> GenerateDetailedReportAsync(
        string reportKey,
        IUnitOfWork scope,
        Expression<Func<Weighment, bool>> filter,
        int maxRows,
        string finalPath,
        CancellationToken cancellationToken)
    {
        int pageSize = 500;
        int skip = 0;
        long totalWritten = 0;
        bool truncated = false;

        using (var writer = new StreamWriter(finalPath))
        {
            await writer.WriteLineAsync(
                "Slip Number,Vehicle,Party,Material,Gross (kg),Tare (kg),Net (kg),Opened At,Completed At,Status");

            while (!truncated)
            {
                var page = await scope.Repository<Weighment>().QueryAsync(
                    filter,
                    query => query
                        .OrderBy(w => w.CompletedAtUtc)
                        .ThenBy(w => w.Id)
                        .Skip(skip)
                        .Take(pageSize),
                    cancellationToken);

                if (page.Count == 0) break;

                foreach (var w in page)
                {
                    decimal? gross = w.Gross?.Kilograms;
                    decimal? tare = w.Tare?.Kilograms;
                    decimal? net = w.NetWeightKg;

                    var line = string.Join(',',
                        EscapeCsv(w.SlipNumber),
                        EscapeCsv(w.VehicleNumber),
                        EscapeCsv(w.PartyName),
                        EscapeCsv(w.MaterialName),
                        gross?.ToString(CultureInfo.InvariantCulture) ?? "",
                        tare?.ToString(CultureInfo.InvariantCulture) ?? "",
                        net?.ToString(CultureInfo.InvariantCulture) ?? "",
                        EscapeCsv(w.CreatedAtUtc.ToLocalTime().ToString("g", CultureInfo.InvariantCulture)),
                        EscapeCsv(w.CompletedAtUtc?.ToLocalTime().ToString("g", CultureInfo.InvariantCulture) ?? ""),
                        EscapeCsv(w.Status.ToString())
                    );

                    await writer.WriteLineAsync(line);
                    totalWritten++;

                    if (totalWritten >= maxRows)
                    {
                        truncated = true;
                        break;
                    }
                }

                skip += page.Count;
            }
        }

        _logger.LogInformation(
            "Detailed report {ReportKey} generated at {Path} with {Rows} row(s){Truncated}",
            reportKey,
            finalPath,
            totalWritten,
            truncated ? " — truncated by MaxRowsPerReport" : "");

        return ReportResult.Success(
            truncated
                ? $"Report generated with the first {totalWritten} rows (row limit reached)."
                : $"Report generated with {totalWritten} row(s).",
            finalPath);
    }

    private async Task<ReportResult> GenerateSummaryReportAsync(
        IUnitOfWork scope,
        Expression<Func<Weighment, bool>> filter,
        int maxRows,
        string finalPath,
        CancellationToken cancellationToken)
    {
        var weighments = await scope.Repository<Weighment>().QueryAsync(
            filter,
            query => query
                .OrderBy(w => w.PartyName)
                .ThenBy(w => w.MaterialName)
                .Take(maxRows),
            cancellationToken);

        var groups = weighments
            .GroupBy(w => new { Party = w.PartyName ?? "-", Material = w.MaterialName ?? "-" })
            .OrderBy(g => g.Key.Party)
            .ThenBy(g => g.Key.Material)
            .ToList();

        decimal grandGross = 0m;
        decimal grandTare = 0m;
        decimal grandNet = 0m;
        decimal grandCharges = 0m;
        long totalTrips = 0;

        using (var writer = new StreamWriter(finalPath))
        {
            await writer.WriteLineAsync("Party,Material,Trips,Total Gross (kg),Total Tare (kg),Total Net (kg),Total Charges (Rs)");

            foreach (var g in groups)
            {
                int trips = g.Count();
                decimal gross = g.Sum(w => w.Gross?.Kilograms ?? 0m);
                decimal tare = g.Sum(w => w.Tare?.Kilograms ?? 0m);
                decimal net = g.Sum(w => w.NetWeightKg ?? 0m);
                decimal charges = g.Sum(w => w.Charges + w.SecondCharges);

                totalTrips += trips;
                grandGross += gross;
                grandTare += tare;
                grandNet += net;
                grandCharges += charges;

                var line = string.Join(',',
                    EscapeCsv(g.Key.Party),
                    EscapeCsv(g.Key.Material),
                    trips,
                    gross.ToString("F1", CultureInfo.InvariantCulture),
                    tare.ToString("F1", CultureInfo.InvariantCulture),
                    net.ToString("F1", CultureInfo.InvariantCulture),
                    charges.ToString("F2", CultureInfo.InvariantCulture)
                );

                await writer.WriteLineAsync(line);
            }

            await writer.WriteLineAsync();
            await writer.WriteLineAsync("--- GRAND TOTALS ---");
            await writer.WriteLineAsync($"Total Trips,{totalTrips}");
            await writer.WriteLineAsync($"Grand Gross (kg),{grandGross.ToString("F1", CultureInfo.InvariantCulture)}");
            await writer.WriteLineAsync($"Grand Tare (kg),{grandTare.ToString("F1", CultureInfo.InvariantCulture)}");
            await writer.WriteLineAsync($"Grand Net (kg),{grandNet.ToString("F1", CultureInfo.InvariantCulture)}");
            await writer.WriteLineAsync($"Grand Charges (Rs),{grandCharges.ToString("F2", CultureInfo.InvariantCulture)}");
        }

        return ReportResult.Success($"Summary report generated with {groups.Count} aggregated group(s) across {totalTrips} transaction(s).", finalPath);
    }

    /// <summary>
    /// Quotes what CSV requires and neutralises formula injection: a field whose
    /// first character is <c>= + - @</c> or tab would be evaluated as a formula when
    /// opened in spreadsheet applications.
    /// </summary>
    public static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        // Clean out carriage returns
        value = value.Replace("\r", " ");

        // Neutralize formula injection
        bool formulaRisk = value.Length > 0 && (value[0] is '=' or '+' or '-' or '@' or '\t');
        if (formulaRisk)
        {
            value = "'" + value;
        }

        // Quote if containing comma, quote, or newline
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            value = $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}
