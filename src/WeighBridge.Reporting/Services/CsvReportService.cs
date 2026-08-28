using System.Globalization;
using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Weighments;

namespace WeighBridge.Reporting.Services;

/// <summary>
/// Writes weighment reports as CSV files.
/// </summary>
/// <remarks>
/// <para>
/// Stateless on purpose. An earlier revision was a singleton holding an injected
/// repository — which pinned one <see cref="WeighBridgeDbContext"/>, and its change
/// tracker, for the life of the process: memory crept with every export and two
/// concurrent exports raced the same context. Each generation now resolves a fresh
/// unit of work per report and pages through the database, so nothing accumulates.
/// </para>
/// </remarks>
public sealed class CsvReportService(
    Func<IUnitOfWork> unitOfWork,
    IPermissionService permissions,
    IOptions<ReportingOptions> options,
    ILogger<CsvReportService> logger) : IReportService
{
    /// <summary>Hard ceiling even when configuration is misconfigured to something huge.</summary>
    private const int AbsoluteMaxRows = 100_000;

    private const string DailyReportKey = "DailyWeighments";
    private const string PartyReportKey = "PartyWeighments";
    private const string MaterialReportKey = "MaterialWeighments";

    private readonly Func<IUnitOfWork> _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    private readonly IPermissionService _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
    private readonly ReportingOptions _options = options.Value;
    private readonly ILogger<CsvReportService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public IReadOnlyList<string> AvailableReports => [DailyReportKey, PartyReportKey, MaterialReportKey];

    public IReadOnlyList<ReportFormat> SupportedFormats => [ReportFormat.Csv];

    public async Task<ReportResult> GenerateAsync(
        string reportKey,
        IReadOnlyDictionary<string, object?> parameters,
        ReportFormat format,
        string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        // Reports.Export is declared, granted to Administrator and Supervisor and withheld
        // from Operator and ReadOnly - and was enforced nowhere, so a ReadOnly gate terminal
        // could write the site's whole weighment history to a CSV file and carry it away.
        // Checked here rather than in the view model because this method is what leaves the
        // file on disk, and a check in front of one caller only guards that caller.
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

                // The daily report ignores name filters; the party and material variants
                // fall back to the date window alone when no name was supplied.
                _ =>
                    w => w.CompletedAtUtc != null &&
                         w.CompletedAtUtc >= startDate.ToUniversalTime() &&
                         w.CompletedAtUtc < endDate.AddDays(1).ToUniversalTime(),
            };

            // Paged reads: the database sorts and windows; only one page is ever in memory.
            int pageSize = 500;
            int skip = 0;
            long totalWritten = 0;
            bool truncated = false;

            var dir = string.IsNullOrEmpty(outputPath) ? _options.OutputDirectory : Path.GetDirectoryName(outputPath) ?? _options.OutputDirectory;
            Directory.CreateDirectory(dir);

            var fileName = string.IsNullOrEmpty(outputPath)
                ? $"{reportKey}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                : Path.GetFileName(outputPath);

            var finalPath = Path.Combine(dir, fileName);

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

                    if (page.Count == 0)
                    {
                        break;
                    }

                    foreach (var w in page)
                    {
                        var line = string.Join(',',
                            EscapeCsv(w.SlipNumber),
                            EscapeCsv(w.VehicleNumber),
                            EscapeCsv(w.PartyName),
                            EscapeCsv(w.MaterialName),
                            w.Gross?.Kilograms.ToString(CultureInfo.InvariantCulture) ?? "",
                            w.Tare?.Kilograms.ToString(CultureInfo.InvariantCulture) ?? "",
                            w.NetWeightKg?.ToString(CultureInfo.InvariantCulture) ?? "",
                            w.CreatedAtUtc.ToLocalTime().ToString("g", CultureInfo.InvariantCulture),
                            w.CompletedAtUtc?.ToLocalTime().ToString("g", CultureInfo.InvariantCulture) ?? "",
                            w.Status.ToString()
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
                "Report {ReportKey} generated at {Path} with {Rows} row(s){Truncated}",
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

    /// <summary>
    /// Quotes what CSV requires and neutralises what spreadsheets execute: a field whose
    /// first character is <c>= + - @</c> or tab would be evaluated as a formula the moment
    /// the file is opened in Excel — a party literally named "=SUM(A1)" must arrive in a
    /// spreadsheet as text, not run.
    /// </summary>
    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        value = value.Replace("\r", " ");

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            value = $"\"{value.Replace("\"", "\"\"")}\"";
        }

        if (value.Length > 0 && (value[0] is '=' or '+' or '-' or '@' or '\t'))
        {
            return $"'{value}";
        }

        return value;
    }
}
