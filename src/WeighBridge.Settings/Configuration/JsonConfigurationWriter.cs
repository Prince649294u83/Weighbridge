using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WeighBridge.Core.Application;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Settings.Configuration;

/// <summary>
/// Writes configuration values into <c>appsettings.json</c> in place.
/// </summary>
/// <remarks>
/// <para>
/// Edits the existing document rather than serialising an options object over it. An
/// options class only knows the keys it binds, so writing one out would silently delete
/// every section it does not model — including the DPAPI-protected secrets and the
/// <c>Logging</c> section, which no options class in this application owns.
/// </para>
/// <para>
/// Writes go through a temporary file and a move, the same as
/// <see cref="ConfigurationProvisioner"/>: an interrupted write must never be able to
/// leave the application without a readable configuration, and this one runs while the
/// application is up and the operator is watching.
/// </para>
/// </remarks>
public sealed class JsonConfigurationWriter(
    IApplicationPaths paths,
    ILogger<JsonConfigurationWriter> logger) : IConfigurationWriter
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IApplicationPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ILogger<JsonConfigurationWriter> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // Concurrent saves would each read, edit and write the whole document, so the second
    // write would silently drop the first one's changes.
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    /// <inheritdoc />
    public async Task<string> SaveAsync(
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        var path = _paths.ConfigurationFile;

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = await ReadRootAsync(path, cancellationToken).ConfigureAwait(false);

            foreach (var (key, value) in values)
            {
                Assign(root, key, value);
            }

            var tempPath = path + ".tmp";
            await File.WriteAllTextAsync(tempPath, root.ToJsonString(WriteOptions), cancellationToken)
                .ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);

            _logger.LogInformation(
                "Saved {Count} configuration value(s) to {Path}: {Keys}",
                values.Count,
                path,
                string.Join(", ", values.Keys));

            return path;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// Reads the document, or starts a fresh one when the file is missing or unreadable.
    /// </summary>
    /// <remarks>
    /// A save must not fail because the file on disk is corrupt — the provisioner already
    /// quarantines and regenerates in that case on the next start, and refusing to save
    /// here would leave the operator unable to fix a bad port number through the screen.
    /// </remarks>
    private async Task<JsonObject> ReadRootAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            _logger.LogWarning("No configuration file at {Path}; a new one will be created", path);
            return new JsonObject();
        }

        try
        {
            var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonNode.Parse(text, documentOptions: ReadOptions) as JsonObject ?? new JsonObject();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Configuration file at {Path} was unreadable; writing a fresh document", path);
            return new JsonObject();
        }
    }

    /// <summary>
    /// Sets one colon-delimited path, creating the objects along the way.
    /// </summary>
    private void Assign(JsonObject root, string key, object? value)
    {
        var segments = key.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            throw new ArgumentException($"'{key}' is not a configuration path.", nameof(key));
        }

        var node = root;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];

            if (node[segment] is JsonObject child)
            {
                node = child;
                continue;
            }

            // Either absent, or a scalar where a section is needed — a hand edit that put
            // a string at "Hardware" must not stop the operator saving a port number.
            var created = new JsonObject();
            node[segment] = created;
            node = created;
        }

        var leaf = segments[^1];

        if (value is null)
        {
            node.Remove(leaf);
            return;
        }

        node[leaf] = ToJsonValue(leaf, value);
    }

    /// <summary>
    /// Converts a value to a JSON node, encrypting anything under a secret key name.
    /// </summary>
    /// <remarks>
    /// The check is on the key, not the value, and matches
    /// <see cref="SecretProtector.ProtectedLeafKeys"/> — so a camera password typed into the
    /// screen is protected on the way in, rather than sitting in plaintext until the next
    /// start happens to run the provisioner over it.
    /// </remarks>
    private static JsonNode ToJsonValue(string leafKey, object value)
    {
        if (value is string text &&
            SecretProtector.ProtectedLeafKeys.Contains(leafKey, StringComparer.OrdinalIgnoreCase))
        {
            return JsonValue.Create(
                text.Length == 0 || SecretProtector.IsProtected(text) ? text : SecretProtector.Protect(text))!;
        }

        return value switch
        {
            string s => JsonValue.Create(s)!,
            bool b => JsonValue.Create(b)!,
            int i => JsonValue.Create(i)!,
            long l => JsonValue.Create(l)!,
            double d => JsonValue.Create(d)!,

            // Written as a JSON number, not a quoted string: the configuration binder
            // accepts either, but a human comparing the file against the defaults should
            // not see one number quoted and its neighbour not.
            decimal m => JsonValue.Create(m)!,
            Enum e => JsonValue.Create(e.ToString())!,
            _ => JsonValue.Create(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "")!,
        };
    }
}
