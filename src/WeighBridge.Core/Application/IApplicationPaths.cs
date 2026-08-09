namespace WeighBridge.Core.Application;

/// <summary>
/// Resolves the writable locations the application uses at runtime.
/// </summary>
/// <remarks>
/// The installation directory is assumed to be read-only (Program Files), so
/// configuration, logs, the SQLite database and user preferences all live under a
/// per-user data root instead.
/// </remarks>
public interface IApplicationPaths
{
    /// <summary>Root folder for all writable application data.</summary>
    string DataRoot { get; }

    /// <summary>Full path of <c>appsettings.json</c>.</summary>
    string ConfigurationFile { get; }

    /// <summary>Full path of the user preferences file.</summary>
    string UserPreferencesFile { get; }

    /// <summary>Folder that receives the daily log files.</summary>
    string LogsDirectory { get; }

    /// <summary>Folder that holds the SQLite database.</summary>
    string DatabaseDirectory { get; }

    /// <summary>Folder for generated reports and exports.</summary>
    string ReportsDirectory { get; }

    /// <summary>Folder for captured vehicle images.</summary>
    string CaptureDirectory { get; }

    /// <summary>Directory the application was installed into (read-only assumption).</summary>
    string InstallDirectory { get; }

    /// <summary>Creates every directory in the layout when it does not already exist.</summary>
    void EnsureCreated();
}
