using Microsoft.Extensions.Logging;

namespace WeighBridge.Core.Configuration;

/// <summary>
/// Bound to the <c>Logging:File</c> section of <c>appsettings.json</c> and consumed by
/// the daily file logger.
/// </summary>
public sealed class FileLoggingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Logging:File";

    /// <summary>Enables writing log files to disk.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Minimum severity written to file.</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// File name pattern. <c>{Date}</c> is replaced with the current date in
    /// <c>yyyy-MM-dd</c> form, producing one file per day.
    /// </summary>
    public string FileNamePattern { get; set; } = "weighbridge-{Date}.log";

    /// <summary>How many days of log files are kept before being deleted.</summary>
    public int RetainedDays { get; set; } = 30;

    /// <summary>Size at which the current day's file rolls over to a new part.</summary>
    public int MaxFileSizeMegabytes { get; set; } = 20;

    /// <summary>Also emit every entry to the debugger output window.</summary>
    public bool IncludeDebugOutput { get; set; } = true;
}
