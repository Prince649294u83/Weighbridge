namespace WeighBridge.Core.Application;

/// <summary>
/// Default <see cref="IApplicationPaths"/> implementation, rooted at
/// <c>%LOCALAPPDATA%\WeighBridge Modern</c>.
/// </summary>
/// <remarks>
/// <para>
/// The root is overridable through the constructor, which is what the test suite uses
/// to redirect every file the foundation writes into a temporary folder.
/// </para>
/// <para>
/// It is also overridable out of process through <see cref="DataRootVariable"/>, which is
/// how the runtime smoke scripts get an isolated root. That exists because they previously
/// had no way to ask for one: to test a cold start they deleted the database at the real
/// path, and a run that did so against a live installation destroyed the operator's
/// weighment history. The variable is read only when no explicit root is passed, so an
/// in-process override still wins.
/// </para>
/// </remarks>
public sealed class ApplicationPaths : IApplicationPaths
{
    /// <summary>Folder name used underneath the local application data root.</summary>
    public const string ProductFolderName = "WeighBridge Modern";

    /// <summary>
    /// Environment variable that relocates the whole data root.
    /// </summary>
    /// <remarks>
    /// Named with the same <c>WEIGHBRIDGE_</c> prefix the configuration provider already
    /// binds, so there is one prefix to know about rather than two. It cannot itself be a
    /// configuration key: the root has to be resolved before a configuration file can be
    /// located, because the file lives inside it.
    /// </remarks>
    public const string DataRootVariable = "WEIGHBRIDGE_DATA_ROOT";

    public ApplicationPaths(string? dataRoot = null)
    {
        var resolved = string.IsNullOrWhiteSpace(dataRoot)
            ? Environment.GetEnvironmentVariable(DataRootVariable)
            : dataRoot;

        DataRoot = string.IsNullOrWhiteSpace(resolved)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ProductFolderName)
            : Path.GetFullPath(resolved);

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
