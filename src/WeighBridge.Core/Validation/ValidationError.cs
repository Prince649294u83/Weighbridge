namespace WeighBridge.Core.Validation;

/// <summary>
/// One thing wrong with the object being validated.
/// </summary>
/// <param name="PropertyName">
/// Property at fault, or <c>null</c> when the finding is about the object as a whole —
/// a gross weight below its own tare belongs to neither field alone.
/// </param>
/// <param name="Message">What to tell the operator. Plain language, not a rule identifier.</param>
/// <param name="Severity">Whether this blocks the operation.</param>
/// <param name="Code">
/// Stable identifier for the rule, for tests and for callers that need to react to a
/// specific failure without matching on message text.
/// </param>
public readonly record struct ValidationError(
    string? PropertyName,
    string Message,
    ValidationSeverity Severity = ValidationSeverity.Error,
    string? Code = null)
{
    /// <summary>Creates a blocking error against a property.</summary>
    public static ValidationError For(string propertyName, string message, string? code = null)
        => new(propertyName, message, ValidationSeverity.Error, code);

    /// <summary>Creates a blocking error about the object as a whole.</summary>
    public static ValidationError Object(string message, string? code = null)
        => new(null, message, ValidationSeverity.Error, code);

    /// <summary>Creates a non-blocking warning.</summary>
    public static ValidationError Warning(string? propertyName, string message, string? code = null)
        => new(propertyName, message, ValidationSeverity.Warning, code);

    /// <summary>Creates an informational note.</summary>
    public static ValidationError Information(string? propertyName, string message, string? code = null)
        => new(propertyName, message, ValidationSeverity.Information, code);

    /// <inheritdoc />
    public override string ToString()
        => PropertyName is null ? Message : $"{PropertyName}: {Message}";
}
