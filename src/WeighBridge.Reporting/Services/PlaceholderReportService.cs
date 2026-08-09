using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Reporting.Services;

/// <summary>
/// Placeholder report service that produces no output.
/// </summary>
/// <remarks>
/// Advertises the formats the Reporting module will support so consuming UI can be
/// built against a stable contract.
/// </remarks>
public sealed class PlaceholderReportService(
    IOptions<ReportingOptions> options,
    ILogger<PlaceholderReportService> logger) : IReportService
{
    private readonly ReportingOptions _options = options.Value;
    private readonly ILogger<PlaceholderReportService> _logger = logger;

    /// <inheritdoc />
    public IReadOnlyList<string> AvailableReports => [];

    /// <inheritdoc />
    public IReadOnlyList<ReportFormat> SupportedFormats => [ReportFormat.Pdf, ReportFormat.Excel, ReportFormat.Csv];

    /// <inheritdoc />
    public Task<ReportResult> GenerateAsync(
        string reportKey,
        IReadOnlyDictionary<string, object?> parameters,
        ReportFormat format,
        string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Report {ReportKey} ({Format}) was requested but reporting is not implemented in this build; default output directory is {OutputDirectory}",
            reportKey,
            format,
            _options.OutputDirectory);

        return Task.FromResult(ReportResult.Failure("Reporting is not available in this build."));
    }
}
