using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WeighBridge.Core.Application;
using WeighBridge.Core.Settings;

namespace WeighBridge.Settings.Services;

/// <summary>
/// JSON-file implementation of <see cref="ISettingsService"/>.
/// </summary>
/// <remarks>
/// Writes are serialised through a semaphore and go through a temporary file, so a
/// crash mid-save can never corrupt the preferences. A failure to read or write is
/// logged and swallowed: preferences are a convenience, never a reason to block the
/// operator.
/// </remarks>
public sealed class SettingsService(IApplicationPaths paths, ILogger<SettingsService> logger) : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,

        // Defence in depth: a future NaN or infinity on a preferences property round-trips
        // as a named literal instead of throwing part-way through the write.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private readonly IApplicationPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ILogger<SettingsService> _logger = logger;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    /// <inheritdoc />
    public UserPreferences Preferences { get; private set; } = new();

    /// <inheritdoc />
    public event EventHandler? PreferencesSaved;

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = _paths.UserPreferencesFile;

        if (!File.Exists(path))
        {
            _logger.LogInformation("No preferences file found; starting from defaults");
            Preferences = new UserPreferences();
            return;
        }

        try
        {
            await using var stream = File.OpenRead(path);

            var loaded = await JsonSerializer
                .DeserializeAsync<UserPreferences>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            Preferences = loaded ?? new UserPreferences();
            _logger.LogInformation("Loaded user preferences from {Path}", path);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Every failure mode is equivalent from the caller's point of view - an
            // unreadable file, a denied path, malformed JSON - and none of them is worth
            // stopping the application for. Narrowing this filter is how an unexpected
            // serializer exception escapes and becomes a crash.
            _logger.LogWarning(ex, "Could not read preferences from {Path}; using defaults", path);
            Preferences = new UserPreferences();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var path = _paths.UserPreferencesFile;
        var tempPath = path + ".tmp";

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _paths.EnsureCreated();

            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer
                    .SerializeAsync(stream, Preferences, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);

            _logger.LogDebug("Saved user preferences to {Path}", path);
            PreferencesSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Broad on purpose - see the remarks on this class. A save is best-effort, and
            // an escaping exception here would surface as an unobserved task exception
            // because callers such as ThemeService persist without awaiting.
            _logger.LogWarning(ex, "Could not save preferences to {Path}", path);
            TryDeleteTempFile(tempPath);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// Removes a half-written temporary file so a failed save leaves nothing behind.
    /// </summary>
    private void TryDeleteTempFile(string tempPath)
    {
        try
        {
            File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not remove the temporary preferences file {Path}", tempPath);
        }
    }

    /// <inheritdoc />
    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        Preferences = new UserPreferences();
        _logger.LogInformation("User preferences reset to defaults");
        return SaveAsync(cancellationToken);
    }
}
