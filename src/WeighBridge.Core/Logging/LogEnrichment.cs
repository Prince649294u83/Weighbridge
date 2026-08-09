using System.Collections;
using System.Text;

namespace WeighBridge.Core.Logging;

/// <summary>
/// The context attached to a log entry: who wrote it, from where, and as part of what.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="IReadOnlyList{T}"/> of key/value pairs because that is the shape
/// Microsoft.Extensions.Logging expects from a scope state. A structured sink added later
/// — Serilog, OpenTelemetry, a database table — reads the fields individually, while the
/// existing text file logger uses <see cref="ToString"/>. Neither needs to change.
/// </para>
/// <para>
/// The text form is built once in the constructor. A log entry that is filtered out never
/// reaches here, and one that is not is about to be written, so caching always pays.
/// </para>
/// </remarks>
public sealed class LogEnrichment : IReadOnlyList<KeyValuePair<string, object?>>
{
    private readonly KeyValuePair<string, object?>[] _values;
    private readonly string _text;

    /// <summary>Creates an enrichment from the fields that have a value.</summary>
    /// <param name="module">Module the entry came from.</param>
    /// <param name="user">Operator the application is running as.</param>
    /// <param name="correlationId">Operation the entry belongs to.</param>
    /// <param name="machineName">Terminal name.</param>
    /// <param name="applicationVersion">Version that produced the entry.</param>
    public LogEnrichment(
        string? module = null,
        string? user = null,
        string? correlationId = null,
        string? machineName = null,
        string? applicationVersion = null)
    {
        Module = module;
        User = user;
        CorrelationId = correlationId;
        MachineName = machineName;
        ApplicationVersion = applicationVersion;

        var values = new List<KeyValuePair<string, object?>>(5);
        var builder = new StringBuilder(64);

        Append(values, builder, "module", module);
        Append(values, builder, "user", user);
        Append(values, builder, "corr", correlationId);
        Append(values, builder, "machine", machineName);
        Append(values, builder, "version", applicationVersion);

        _values = [.. values];
        _text = builder.ToString();
    }

    /// <summary>Module the entry came from, when known.</summary>
    public string? Module { get; }

    /// <summary>Operator the application is running as, when recorded.</summary>
    public string? User { get; }

    /// <summary>Operation the entry belongs to, when one was in scope.</summary>
    public string? CorrelationId { get; }

    /// <summary>Terminal the entry was written on, when recorded.</summary>
    public string? MachineName { get; }

    /// <summary>Application version that produced the entry, when recorded.</summary>
    public string? ApplicationVersion { get; }

    /// <summary>True when no field carried a value, so the scope is not worth opening.</summary>
    public bool IsEmpty => _values.Length == 0;

    /// <inheritdoc />
    public int Count => _values.Length;

    /// <inheritdoc />
    public KeyValuePair<string, object?> this[int index] => _values[index];

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        => ((IEnumerable<KeyValuePair<string, object?>>)_values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();

    /// <summary>
    /// Renders as <c>key=value</c> pairs. The file logger writes this between braces,
    /// which is where the existing log format already puts scope text.
    /// </summary>
    public override string ToString() => _text;

    private static void Append(
        List<KeyValuePair<string, object?>> values,
        StringBuilder builder,
        string key,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        values.Add(new KeyValuePair<string, object?>(key, value));

        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        builder.Append(key).Append('=').Append(value);
    }
}
