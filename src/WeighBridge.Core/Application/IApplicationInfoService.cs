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
    /// The Windows account the application is running under.
    /// </summary>
    /// <remarks>
    /// Not the operator. The title bar and the log enrichment both used to read this, which
    /// is how every audit entry came to name the terminal's Windows account rather than
    /// whoever had signed in; both now take the operator from
    /// <c>IPermissionService.CurrentOperator</c>. What remains here is the terminal's own
    /// identity, which is the right answer for diagnostics and for an entry written before
    /// anyone signed in.
    /// </remarks>
    string CurrentUserName { get; }
}
