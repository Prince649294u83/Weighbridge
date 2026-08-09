namespace WeighBridge.Core.Configuration;

/// <summary>
/// Bound to the <c>Reporting</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class ReportingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Reporting";

    /// <summary>Default export format: Pdf, Excel or Csv.</summary>
    public string DefaultExportFormat { get; set; } = "Pdf";

    /// <summary>
    /// Output folder for generated reports. Empty means the Reports folder inside the
    /// application data root.
    /// </summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Open the generated file with the shell association after export.</summary>
    public bool OpenAfterExport { get; set; } = true;

    /// <summary>Maximum number of rows a single report may contain.</summary>
    public int MaxRowsPerReport { get; set; } = 100_000;
}
