using WeighBridge.Core.Theming;

namespace WeighBridge.Core.Configuration;

/// <summary>
/// Bound to the <c>Application</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class ApplicationOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Application";

    /// <summary>Product name used when configuration does not supply one.</summary>
    public const string DefaultName = "WeighBridge Modern";

    /// <summary>Display name of the application.</summary>
    public string Name { get; set; } = DefaultName;

    /// <summary>Name of the operating company, shown in the title bar and on slips.</summary>
    public string OrganizationName { get; set; } = string.Empty;

    /// <summary>Identifier of the physical site / weighbridge.</summary>
    public string SiteName { get; set; } = string.Empty;

    /// <summary>Startup theme.</summary>
    public AppTheme Theme { get; set; } = AppTheme.Light;

    /// <summary>UI culture, e.g. <c>en-US</c>.</summary>
    public string Language { get; set; } = "en-US";

    /// <summary>Module the shell opens on startup.</summary>
    public string StartupModule { get; set; } = "Dashboard";

    /// <summary>Whether window size and position are restored between sessions.</summary>
    public bool RestoreWindowPlacement { get; set; } = true;
}
