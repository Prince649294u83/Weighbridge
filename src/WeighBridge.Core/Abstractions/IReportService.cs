namespace WeighBridge.Core.Abstractions;

/// <summary>Export formats supported by the reporting layer.</summary>
public enum ReportFormat
{
    Pdf = 0,
    Excel = 1,
    Csv = 2,
}

/// <summary>
/// Outcome of a report generation request.
/// </summary>
/// <param name="Succeeded">True when a file was produced.</param>
/// <param name="Message">Human-readable outcome for logging and dialogs.</param>
/// <param name="OutputPath">Full path of the generated file, when one exists.</param>
public sealed record ReportResult(bool Succeeded, string Message, string? OutputPath = null)
{
    /// <summary>Creates a successful result.</summary>
    public static ReportResult Success(string message, string outputPath) => new(true, message, outputPath);

    /// <summary>Creates a failed result.</summary>
    public static ReportResult Failure(string message) => new(false, message);
}

/// <summary>
/// Generates, previews, and exports canonical report documents.
/// </summary>
public interface IReportService
{
    /// <summary>Keys of the reports this build knows how to render.</summary>
    IReadOnlyList<string> AvailableReports { get; }

    /// <summary>Formats this build can export to.</summary>
    IReadOnlyList<ReportFormat> SupportedFormats { get; }

    /// <summary>
    /// Builds a canonical in-memory ReportDocument from the given filter parameters.
    /// </summary>
    Task<WeighBridge.Core.Reporting.ReportDocument> BuildDocumentAsync(
        WeighBridge.Core.Reporting.ReportFilterParameters filters,
        CancellationToken cancellationToken = default);

    /// <summary>Renders <paramref name="reportKey"/> and writes it to disk.</summary>
    Task<ReportResult> GenerateAsync(
        string reportKey,
        IReadOnlyDictionary<string, object?> parameters,
        ReportFormat format,
        string? outputPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>Exports a canonical <paramref name="document"/> to the requested format.</summary>
    Task<ReportResult> ExportAsync(
        WeighBridge.Core.Reporting.ReportDocument document,
        ReportFormat format,
        string? outputPath = null,
        CancellationToken cancellationToken = default);
}
