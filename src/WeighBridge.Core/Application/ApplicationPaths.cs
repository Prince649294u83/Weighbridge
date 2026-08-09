namespace WeighBridge.Core.Application;

/// <summary>
/// Default <see cref="IApplicationPaths"/> implementation, rooted at
/// <c>%LOCALAPPDATA%\WeighBridge Modern</c>.
/// </summary>
/// <remarks>
/// The root is overridable through the constructor, which is what the test suite uses
/// to redirect every file the foundation writes into a temporary folder.
/// </remarks>
public sealed class ApplicationPaths : IApplicationPaths
{
    /// <summary>Folder name used underneath the local application data root.</summary>
    public const string ProductFolderName = "WeighBridge Modern";

    public ApplicationPaths(string? dataRoot = null)
    {
        DataRoot = string.IsNullOrWhiteSpace(dataRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ProductFolderName)
            : Path.GetFullPath(dataRoot);

        InstallDirectory = AppContext.BaseDirectory;
        LogsDirectory = Path.Combine(DataRoot, "Logs");
        DatabaseDirectory = Path.Combine(DataRoot, "Data");
        ReportsDirectory = Path.Combine(DataRoot, "Reports");
        CaptureDirectory = Path.Combine(DataRoot, "Captures");
        ConfigurationFile = Path.Combine(DataRoot, "appsettings.json");
        UserPreferencesFile = Path.Combine(DataRoot, "userpreferences.json");
    }

    /// <inheritdoc />
    public string DataRoot { get; }

    /// <inheritdoc />
    public string ConfigurationFile { get; }

    /// <inheritdoc />
    public string UserPreferencesFile { get; }

    /// <inheritdoc />
    public string LogsDirectory { get; }

    /// <inheritdoc />
    public string DatabaseDirectory { get; }

    /// <inheritdoc />
    public string ReportsDirectory { get; }

    /// <inheritdoc />
    public string CaptureDirectory { get; }

    /// <inheritdoc />
    public string InstallDirectory { get; }

    /// <inheritdoc />
    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(DatabaseDirectory);
        Directory.CreateDirectory(ReportsDirectory);
        Directory.CreateDirectory(CaptureDirectory);
    }
}
