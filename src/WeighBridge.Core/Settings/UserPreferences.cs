using System.Text.Json.Serialization;
using WeighBridge.Core.Theming;

namespace WeighBridge.Core.Settings;

/// <summary>
/// Operator preferences persisted between sessions as JSON.
/// </summary>
/// <remarks>
/// Distinct from <c>appsettings.json</c>: that file holds deployment configuration
/// (managed by an administrator), whereas this file holds per-user UI state that the
/// application itself rewrites.
/// </remarks>
public sealed class UserPreferences
{
    /// <summary>Theme chosen by the operator.</summary>
    public AppTheme Theme { get; set; } = AppTheme.Light;

    /// <summary>Whether the left navigation panel is collapsed to icons.</summary>
    public bool IsNavigationCollapsed { get; set; }

    /// <summary>Module key the shell should reopen on next launch.</summary>
    public string? LastModule { get; set; }

    /// <summary>Persisted main window placement.</summary>
    public WindowPlacement Window { get; set; } = new();
}

/// <summary>Size, position and maximised state of the main window.</summary>
/// <remarks>
/// "Not recorded yet" is expressed as <c>null</c> rather than <see cref="double.NaN"/>.
/// NaN reads naturally in WPF, where it means "auto", but it has no JSON representation:
/// <c>System.Text.Json</c> throws rather than writing it, which would fail every save
/// until a position happened to be captured.
/// </remarks>
public sealed class WindowPlacement
{
    /// <summary>Distance from the left edge of the virtual desktop in DIPs, or null when unset.</summary>
    public double? Left { get; set; }

    /// <summary>Distance from the top edge of the virtual desktop in DIPs, or null when unset.</summary>
    public double? Top { get; set; }

    /// <summary>Window width in DIPs.</summary>
    public double Width { get; set; } = 1280;

    /// <summary>Window height in DIPs.</summary>
    public double Height { get; set; } = 800;

    /// <summary>Whether the window was maximised when it was last closed.</summary>
    public bool IsMaximized { get; set; }

    /// <summary>True when a usable position was recorded.</summary>
    [JsonIgnore]
    public bool HasPosition => Left.HasValue && Top.HasValue;
}
