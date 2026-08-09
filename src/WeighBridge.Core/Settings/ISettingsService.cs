namespace WeighBridge.Core.Settings;

/// <summary>
/// Loads and persists <see cref="UserPreferences"/>.
/// </summary>
public interface ISettingsService
{
    /// <summary>The live preferences instance. Mutate, then call <see cref="SaveAsync"/>.</summary>
    UserPreferences Preferences { get; }

    /// <summary>Raised after preferences were successfully saved.</summary>
    event EventHandler? PreferencesSaved;

    /// <summary>Reads preferences from disk, falling back to defaults on any failure.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the current preferences to disk atomically.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>Resets every preference to its default value and persists the result.</summary>
    Task ResetAsync(CancellationToken cancellationToken = default);
}
