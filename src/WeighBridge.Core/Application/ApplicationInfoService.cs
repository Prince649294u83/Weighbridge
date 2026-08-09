using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Core.Application;

/// <summary>
/// Reads application identity from assembly metadata and the environment.
/// </summary>
public sealed class ApplicationInfoService : IApplicationInfoService
{
    private readonly Lazy<DateTime> _buildDate;

    public ApplicationInfoService(IOptions<ApplicationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var assembly = Assembly.GetEntryAssembly() ?? typeof(ApplicationInfoService).Assembly;

        ApplicationName = string.IsNullOrWhiteSpace(options.Value.Name)
            ? ApplicationOptions.DefaultName
            : options.Value.Name;

        Version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+')[0] ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        _buildDate = new Lazy<DateTime>(() => ReadBuildDate(assembly));
    }

    /// <inheritdoc />
    public string ApplicationName { get; }

    /// <inheritdoc />
    public string Version { get; }

    /// <inheritdoc />
    public string DisplayVersion => $"v{Version}";

    /// <inheritdoc />
    public DateTime BuildDate => _buildDate.Value;

    /// <inheritdoc />
    public string OperatingSystem => RuntimeInformation.OSDescription;

    /// <inheritdoc />
    public string MachineName => Environment.MachineName;

    /// <inheritdoc />
    public string CurrentUserName => Environment.UserName;

    /// <summary>
    /// Uses the assembly file's last write time as the build date. Deterministic
    /// builds strip the PE timestamp, so the file stamp is the reliable source.
    /// </summary>
    private static DateTime ReadBuildDate(Assembly assembly)
    {
        try
        {
            var location = assembly.Location;

            return string.IsNullOrEmpty(location)
                ? DateTime.Now
                : File.GetLastWriteTime(location);
        }
        catch (IOException)
        {
            return DateTime.Now;
        }
        catch (UnauthorizedAccessException)
        {
            return DateTime.Now;
        }
    }
}
