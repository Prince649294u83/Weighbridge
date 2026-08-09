using WeighBridge.Core.Application;

namespace WeighBridge.Tests.Infrastructure;

/// <summary>
/// A disposable <see cref="IApplicationPaths"/> rooted in a unique temporary folder.
/// </summary>
/// <remarks>
/// Every file the foundation writes (configuration, preferences, logs, the SQLite
/// database) is resolved through <see cref="IApplicationPaths"/>, so redirecting the root
/// is enough to keep a test run away from the developer's real
/// <c>%LOCALAPPDATA%\WeighBridge Modern</c> folder. Each instance gets its own directory
/// so tests can run in parallel without fighting over file locks.
/// </remarks>
internal sealed class TempDataRoot : IDisposable
{
    public TempDataRoot()
    {
        Root = Path.Combine(Path.GetTempPath(), "WeighBridge.Tests", Guid.NewGuid().ToString("N"));
        Paths = new ApplicationPaths(Root);
    }

    /// <summary>The temporary folder standing in for the per-user data root.</summary>
    public string Root { get; }

    /// <summary>Paths resolved beneath <see cref="Root"/>.</summary>
    public IApplicationPaths Paths { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A log writer may still hold a handle. Leaving a folder behind in %TEMP% is
            // preferable to failing an otherwise passing test.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }
}
