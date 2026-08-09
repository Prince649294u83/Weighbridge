using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Windows;
using WeighBridge.Core.Settings;
using WeighBridge.Core.Theming;

namespace WeighBridge.App.Services;

/// <summary>
/// Applies a theme by swapping the semantic brush dictionary merged into
/// <see cref="Application.Resources"/>.
/// </summary>
/// <remarks>
/// The light and dark dictionaries declare an identical set of keys, so replacing one
/// with the other re-colours every control that references those keys through
/// <c>DynamicResource</c> — no restart, no reload of any view.
/// </remarks>
public sealed class ThemeService : IThemeService
{
    private const string LightThemeSource = "pack://application:,,,/WeighBridge.App;component/Themes/Theme.Light.xaml";
    private const string DarkThemeSource = "pack://application:,,,/WeighBridge.App;component/Themes/Theme.Dark.xaml";

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    private readonly ISettingsService _settingsService;
    private readonly ILogger<ThemeService> _logger;

    public ThemeService(ISettingsService settingsService, ILogger<ThemeService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <inheritdoc />
    public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

    /// <inheritdoc />
    public AppTheme EffectiveTheme { get; private set; } = AppTheme.Light;

    /// <inheritdoc />
    public event EventHandler<AppTheme>? ThemeChanged;

    /// <inheritdoc />
    public void Initialize() => ApplyInternal(_settingsService.Preferences.Theme, persist: false);

    /// <inheritdoc />
    public void ApplyTheme(AppTheme theme) => ApplyInternal(theme, persist: true);

    /// <inheritdoc />
    public void ToggleTheme() => ApplyTheme(EffectiveTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);

    private void ApplyInternal(AppTheme theme, bool persist)
    {
        var effective = theme == AppTheme.System ? ResolveSystemTheme() : theme;
        var source = new Uri(effective == AppTheme.Dark ? DarkThemeSource : LightThemeSource, UriKind.Absolute);

        var application = Application.Current;

        if (application is null)
        {
            // Unit tests and design-time hosts have no Application; record the choice
            // so the state stays consistent and skip the visual swap.
            CurrentTheme = theme;
            EffectiveTheme = effective;
            return;
        }

        var dictionaries = application.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d =>
            d.Source is not null &&
            d.Source.OriginalString.Contains("Themes/Theme.", StringComparison.OrdinalIgnoreCase));

        var replacement = new ResourceDictionary { Source = source };

        if (existing is null)
        {
            // Insert ahead of the style dictionaries so their DynamicResource lookups
            // find the brushes; if there are none yet, appending is equivalent.
            dictionaries.Add(replacement);
        }
        else
        {
            dictionaries[dictionaries.IndexOf(existing)] = replacement;
        }

        CurrentTheme = theme;
        EffectiveTheme = effective;

        _logger.LogInformation("Applied theme {Theme} (effective {EffectiveTheme}).", theme, effective);

        if (persist)
        {
            _settingsService.Preferences.Theme = theme;
            _ = _settingsService.SaveAsync();
        }

        ThemeChanged?.Invoke(this, effective);
    }

    /// <summary>
    /// Reads the Windows app theme preference. Any failure resolves to light, which is
    /// the safe default for a daylit weighbridge cabin.
    /// </summary>
    private AppTheme ResolveSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            var value = key?.GetValue(AppsUseLightThemeValue);

            return value is int useLight && useLight == 0 ? AppTheme.Dark : AppTheme.Light;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the Windows theme preference; defaulting to light.");
            return AppTheme.Light;
        }
    }
}
