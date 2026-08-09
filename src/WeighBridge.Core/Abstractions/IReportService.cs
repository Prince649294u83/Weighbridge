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
/// Generates and exports reports.
/// </summary>
/// <remarks>
/// Module 0.1 registers a placeholder. The Reporting module supplies the real
/// renderers behind this interface.
/// </remarks>
public interface IReportService
{
    /// <summary>Keys of the reports this build knows how to render.</summary>
    IReadOnlyList<string> AvailableReports { get; }

    /// <summary>Formats this build can export to.</summary>
    IReadOnlyList<ReportFormat> SupportedFormats { get; }

    /// <summary>Renders <paramref name="reportKey"/> and writes it to disk.</summary>
    Task<ReportResult> GenerateAsync(
        string reportKey,
        IReadOnlyDictionary<string, object?> parameters,
        ReportFormat format,
        string? outputPath = null,
        CancellationToken cancellationToken = default);
}
