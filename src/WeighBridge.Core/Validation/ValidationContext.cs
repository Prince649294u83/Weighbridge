namespace WeighBridge.Core.Validation;

/// <summary>
/// Why an object is being validated. The same object validates differently depending on
/// the answer.
/// </summary>
public enum ValidationMode
{
    /// <summary>The object is being created.</summary>
    Create = 0,

    /// <summary>
    /// The object already exists and is being changed.
    /// </summary>
    /// <remarks>
    /// A uniqueness rule has to exclude the row being edited here, or every update of an
    /// unchanged code would fail against itself.
    /// </remarks>
    Update = 1,

    /// <summary>The object is being deleted and only referential rules apply.</summary>
    Delete = 2,
}

/// <summary>
/// The circumstances a validation pass runs under.
/// </summary>
/// <remarks>
/// Carries what a rule cannot get from constructor injection: whether this is a create or
/// an update, which property is being checked during as-you-type validation, the
/// cancellation token, and a small bag for anything a caller needs to hand a rule (the
/// identity of the record being edited, most often).
/// </remarks>
public sealed class ValidationContext
{
    private readonly Dictionary<string, object?> _items = new(StringComparer.Ordinal);

    /// <summary>Creates a context.</summary>
    public ValidationContext(
        ValidationMode mode = ValidationMode.Create,
        string? propertyName = null,
        CancellationToken cancellationToken = default)
    {
        Mode = mode;
        PropertyName = propertyName;
        CancellationToken = cancellationToken;
    }

    /// <summary>Whether the object is being created, changed or removed.</summary>
    public ValidationMode Mode { get; }

    /// <summary>
    /// The single property being validated, or <c>null</c> when the whole object is.
    /// </summary>
    public string? PropertyName { get; }

    /// <summary>Cancels a validation pass that has to wait on the database.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Caller-supplied values a rule may need.</summary>
    public IReadOnlyDictionary<string, object?> Items => _items;

    /// <summary>Attaches a value for the rules to read. Returns the context for chaining.</summary>
    public ValidationContext With(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _items[key] = value;
        return this;
    }

    /// <summary>Reads an attached value, or <c>default</c> when it is absent or the wrong type.</summary>
    public TValue? Get<TValue>(string key)
        => _items.TryGetValue(key, out var value) && value is TValue typed ? typed : default;

    /// <summary>A context for validating one property as the operator types.</summary>
    public static ValidationContext ForProperty(
        string propertyName,
        ValidationMode mode = ValidationMode.Create,
        CancellationToken cancellationToken = default)
        => new(mode, propertyName, cancellationToken);
}
