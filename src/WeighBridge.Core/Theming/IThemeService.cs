namespace WeighBridge.Core.Theming;

/// <summary>
/// Applies and persists the active visual theme.
/// </summary>
public interface IThemeService
{
    /// <summary>The theme selected by the operator (may be <see cref="AppTheme.System"/>).</summary>
    AppTheme CurrentTheme { get; }

    /// <summary>
    /// The theme actually rendered — <see cref="AppTheme.System"/> resolved against the
    /// current Windows personalisation setting.
    /// </summary>
    AppTheme EffectiveTheme { get; }

    /// <summary>Raised after the effective theme changed.</summary>
    event EventHandler<AppTheme>? ThemeChanged;

    /// <summary>Loads the persisted theme and applies it. Called once during startup.</summary>
    void Initialize();

    /// <summary>Applies <paramref name="theme"/> and persists the choice.</summary>
    void ApplyTheme(AppTheme theme);

    /// <summary>Toggles between the light and dark themes.</summary>
    void ToggleTheme();
}
