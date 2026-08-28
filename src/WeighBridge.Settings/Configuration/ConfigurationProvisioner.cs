using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WeighBridge.Core.Application;

namespace WeighBridge.Settings.Configuration;

/// <summary>
/// Guarantees that a usable <c>appsettings.json</c> exists before the configuration
/// system reads it, and that secrets in it are not stored as plaintext.
/// </summary>
/// <remarks>
/// The application must never fail to start because configuration is missing. This
/// class creates the file with documented defaults on first run and, on subsequent
/// runs, repairs it by adding any section or key introduced by a newer build while
/// leaving operator-supplied values untouched.
/// </remarks>
public sealed class ConfigurationProvisioner(IApplicationPaths paths, ILogger<ConfigurationProvisioner>? logger = null)
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IApplicationPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ILogger<ConfigurationProvisioner>? _logger = logger;

    /// <summary>Result of the most recent <see cref="EnsureConfigurationFile"/> call.</summary>
    public ConfigurationProvisioningOutcome LastOutcome { get; private set; } = ConfigurationProvisioningOutcome.Unchanged;

    /// <summary>
    /// Creates or repairs <c>appsettings.json</c>, upgrades plaintext secrets to DPAPI-
    /// protected values, and returns the file's full path.
    /// </summary>
    public string EnsureConfigurationFile()
    {
        _paths.EnsureCreated();

        var path = _paths.ConfigurationFile;

        if (!File.Exists(path))
        {
            WriteAtomically(path, DefaultConfiguration.Create(_paths));
            LastOutcome = ConfigurationProvisioningOutcome.Created;
            _logger?.LogInformation("Created default configuration file at {Path}", path);
            return path;
        }

        try
        {
            var existingText = File.ReadAllText(path);
            var existing = JsonNode.Parse(existingText, documentOptions: ReadOptions) as JsonObject;

            if (existing is null)
            {
                throw new JsonException("Root element is not a JSON object.");
            }

            var defaults = DefaultConfiguration.Create(_paths);

            if (MergeMissingKeys(defaults, existing))
            {
                WriteAtomically(path, existing);
                LastOutcome = ConfigurationProvisioningOutcome.Repaired;
                _logger?.LogInformation("Added missing keys to configuration file at {Path}", path);
            }
            else
            {
                LastOutcome = ConfigurationProvisioningOutcome.Unchanged;
            }

            // A repaired or untouched file may still carry a secret in plaintext from an
            // older build or a hand edit. Encrypt those in place; the host decrypts when
            // configuration loads.
            if (ProtectPlaintextSecrets(existing))
            {
                WriteAtomically(path, existing);

                if (LastOutcome == ConfigurationProvisioningOutcome.Unchanged)
                {
                    LastOutcome = ConfigurationProvisioningOutcome.Secured;
                }

                _logger?.LogInformation("Protected one or more plaintext configuration secrets with DPAPI");
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt file must not stop the application. Preserve it for support
            // and start from defaults.
            LastOutcome = ConfigurationProvisioningOutcome.Recovered;
            _logger?.LogError(ex, "Configuration file at {Path} was unreadable; regenerating defaults", path);

            TryQuarantine(path);
            WriteAtomically(path, DefaultConfiguration.Create(_paths));
        }

        return path;
    }

    /// <summary>
    /// Walks the document and replaces every plaintext value under a known secret key
    /// with its DPAPI-protected form.
    /// </summary>
    /// <returns><c>true</c> when at least one value was encrypted.</returns>
    internal bool ProtectPlaintextSecrets(JsonObject root)
    {
        var changed = false;
        Visit(root, ref changed);
        return changed;

        static void Visit(JsonObject node, ref bool changed)
        {
            foreach (var (key, value) in node)
            {
                switch (value)
                {
                    case JsonObject child:
                        Visit(child, ref changed);
                        break;

                    case JsonArray array:
                        foreach (var item in array)
                        {
                            if (item is JsonObject itemObject)
                            {
                                Visit(itemObject, ref changed);
                            }
                        }
                        break;

                    case JsonValue primitive when
                        primitive.TryGetValue<string>(out var text) &&
                        SecretProtector.ProtectedLeafKeys.Contains(key, StringComparer.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(text) &&
                        !SecretProtector.IsProtected(text):

                        node[key] = SecretProtector.Protect(text);
                        changed = true;
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Copies keys present in <paramref name="defaults"/> but absent from
    /// <paramref name="target"/>, recursing into nested objects.
    /// </summary>
    /// <returns><c>true</c> when at least one key was added.</returns>
    private static bool MergeMissingKeys(JsonObject defaults, JsonObject target)
    {
        var changed = false;

        foreach (var (key, defaultValue) in defaults)
        {
            if (!target.TryGetPropertyValue(key, out var existingValue))
            {
                target[key] = defaultValue?.DeepClone();
                changed = true;
                continue;
            }

            if (defaultValue is JsonObject defaultChild && existingValue is JsonObject existingChild)
            {
                changed |= MergeMissingKeys(defaultChild, existingChild);
            }
        }

        return changed;
    }

    /// <summary>
    /// Writes through a temporary file so an interrupted write can never leave a
    /// half-written configuration behind.
    /// </summary>
    private static void WriteAtomically(string path, JsonNode content)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";

        File.WriteAllText(tempPath, content.ToJsonString(WriteOptions));
        File.Move(tempPath, path, overwrite: true);
    }

    private void TryQuarantine(string path)
    {
        try
        {
            var quarantined = $"{path}.invalid-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(path, quarantined, overwrite: true);
            _logger?.LogWarning("Unreadable configuration preserved as {Path}", quarantined);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort only.
        }
    }
}

/// <summary>What <see cref="ConfigurationProvisioner"/> had to do to the file.</summary>
public enum ConfigurationProvisioningOutcome
{
    /// <summary>The file already existed and was complete.</summary>
    Unchanged = 0,

    /// <summary>The file did not exist and was created from defaults.</summary>
    Created = 1,

    /// <summary>The file existed but was missing keys, which were added.</summary>
    Repaired = 2,

    /// <summary>The file was unreadable; it was quarantined and regenerated.</summary>
    Recovered = 3,

    /// <summary>Plaintext secrets were replaced with DPAPI-protected values.</summary>
    Secured = 4,
}

/// <summary>
/// Configuration helpers around secret decryption for hosts.
/// </summary>
public static class SecretAwareConfiguration
{
    /// <summary>
    /// Returns a configuration view of <paramref name="source"/> in which every leaf whose
    /// value carries the <see cref="SecretProtector.Prefix"/> has been decrypted. A value
    /// that cannot be decrypted — written by another user profile or machine — decrypts
    /// to empty so the terminal starts and reports the subsystem unconfigured instead of
    /// failing on startup.
    /// </summary>
    public static IConfiguration WithDecryptedSecrets(IConfiguration source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var flattened = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Flatten(source, string.Empty, flattened);

        foreach (var key in flattened.Keys.ToList())
        {
            var value = flattened[key];

            if (SecretProtector.IsProtected(value))
            {
                flattened[key] = SecretProtector.TryUnprotect(value) ?? string.Empty;
            }
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(flattened!)
            .Build();
    }

    private static void Flatten(IConfiguration source, string prefix, Dictionary<string, string?> target)
    {
        foreach (var child in source.GetChildren())
        {
            var path = prefix.Length == 0 ? child.Key : $"{prefix}:{child.Key}";

            if (child.Value is not null || !child.GetChildren().Any())
            {
                target[path] = child.Value;
            }

            Flatten(child, path, target);
        }
    }
}
