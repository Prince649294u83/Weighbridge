namespace WeighBridge.Core.Validation;

/// <summary>
/// Everything a validation pass found, not just the first failure.
/// </summary>
/// <remarks>
/// Reporting all findings at once is the point. An operator who fixes one field, submits,
/// and is told about the next field has to make four round trips through a form that could
/// have shown four messages the first time.
/// </remarks>
public sealed class ValidationResult
{
    private static readonly ValidationResult SuccessInstance = new([]);

    private readonly ValidationError[] _errors;

    private ValidationResult(ValidationError[] errors) => _errors = errors;

    /// <summary>A pass with nothing to report.</summary>
    public static ValidationResult Success => SuccessInstance;

    /// <summary>Every finding, in the order the rules produced them.</summary>
    public IReadOnlyList<ValidationError> Errors => _errors;

    /// <summary>
    /// True when nothing blocking was found. Warnings and information do not make a
    /// result invalid.
    /// </summary>
    public bool IsValid => !_errors.Any(error => error.Severity == ValidationSeverity.Error);

    /// <summary>True when there is nothing at all to show the operator.</summary>
    public bool IsEmpty => _errors.Length == 0;

    /// <summary>The blocking findings alone.</summary>
    public IEnumerable<ValidationError> Blocking
        => _errors.Where(error => error.Severity == ValidationSeverity.Error);

    /// <summary>The non-blocking findings worth showing anyway.</summary>
    public IEnumerable<ValidationError> Warnings
        => _errors.Where(error => error.Severity == ValidationSeverity.Warning);

    /// <summary>Creates a result from a set of findings.</summary>
    public static ValidationResult From(IEnumerable<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var materialised = errors as ValidationError[] ?? [.. errors];

        return materialised.Length == 0 ? Success : new ValidationResult(materialised);
    }

    /// <summary>Creates a failed result from one or more findings.</summary>
    public static ValidationResult Failure(params ValidationError[] errors) => From(errors);

    /// <summary>Creates a failed result with a single property error.</summary>
    public static ValidationResult Failure(string propertyName, string message, string? code = null)
        => From([ValidationError.For(propertyName, message, code)]);

    /// <summary>
    /// Merges several results into one, so an object-level rule and a set of property
    /// rules produce a single verdict.
    /// </summary>
    public static ValidationResult Merge(params ValidationResult?[] results)
    {
        ArgumentNullException.ThrowIfNull(results);

        return From(results.Where(result => result is not null).SelectMany(result => result!._errors));
    }

    /// <summary>Findings for one property, for a field-level adorner.</summary>
    public IEnumerable<ValidationError> ForProperty(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        return _errors.Where(error =>
            string.Equals(error.PropertyName, propertyName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Every finding on one line each, for a summary panel, a notification detail area or
    /// a log entry.
    /// </summary>
    public string ToSummary()
        => string.Join(Environment.NewLine, _errors.Select(error => error.ToString()));

    /// <inheritdoc />
    public override string ToString()
        => IsEmpty ? "Valid" : $"{_errors.Length} finding(s), valid: {IsValid}";
}
