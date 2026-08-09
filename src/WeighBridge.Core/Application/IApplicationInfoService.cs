namespace WeighBridge.Core.Application;

/// <summary>
/// Exposes read-only facts about the running application: version, build date and
/// the identity of the signed-in operator (a placeholder until the Login module
/// lands).
/// </summary>
public interface IApplicationInfoService
{
    /// <summary>Display name of the product.</summary>
    string ApplicationName { get; }

    /// <summary>Informational version string, e.g. <c>0.1.0</c>.</summary>
    string Version { get; }

    /// <summary>Version prefixed for display, e.g. <c>v0.1.0</c>.</summary>
    string DisplayVersion { get; }

    /// <summary>Local time the assembly was built.</summary>
    DateTime BuildDate { get; }

    /// <summary>Operating system description.</summary>
    string OperatingSystem { get; }

    /// <summary>Machine the application is running on.</summary>
    string MachineName { get; }

    /// <summary>
    /// Name shown in the title bar. Module 0.1 reports the Windows account; the Login
    /// module will replace this with the authenticated operator.
    /// </summary>
    string CurrentUserName { get; }
}
