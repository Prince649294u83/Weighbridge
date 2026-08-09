namespace WeighBridge.Core.Navigation;

/// <summary>
/// Parameters handed to a ViewModel when it is navigated to.
/// </summary>
/// <remarks>
/// Business modules (Vehicle Entry, Duplicate Slip, …) will use this to receive
/// ticket numbers or record identifiers without coupling to each other.
/// </remarks>
public sealed class NavigationContext
{
    private readonly Dictionary<string, object?> _parameters;

    public NavigationContext(IDictionary<string, object?>? parameters = null)
        => _parameters = parameters is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(parameters, StringComparer.OrdinalIgnoreCase);

    /// <summary>An empty context, used for parameterless navigation.</summary>
    public static NavigationContext Empty => new();

    /// <summary>True when the navigation was caused by a refresh of the current view.</summary>
    public bool IsRefresh { get; init; }

    /// <summary>True when the navigation came from the back/forward history.</summary>
    public bool IsHistoryNavigation { get; init; }

    /// <summary>All supplied parameters.</summary>
    public IReadOnlyDictionary<string, object?> Parameters => _parameters;

    /// <summary>Adds or replaces a parameter and returns the same instance for chaining.</summary>
    public NavigationContext With(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _parameters[key] = value;
        return this;
    }

    /// <summary>
    /// Reads a parameter, returning <paramref name="fallback"/> when it is absent or
    /// of an unexpected type.
    /// </summary>
    public T? GetValueOrDefault<T>(string key, T? fallback = default)
        => _parameters.TryGetValue(key, out var value) && value is T typed ? typed : fallback;
}
