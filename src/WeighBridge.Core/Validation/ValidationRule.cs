namespace WeighBridge.Core.Validation;

/// <summary>
/// One rule: a condition and what to say when it fails.
/// </summary>
/// <remarks>
/// Async by construction because some rules have to ask the database — "this vehicle
/// number is already on the weighbridge" cannot be answered in memory. Synchronous rules
/// are the common case and are added through the synchronous overloads on
/// <see cref="Validator{T}"/>, which wrap them without a thread hop.
/// </remarks>
/// <typeparam name="T">Type the rule applies to.</typeparam>
public sealed class ValidationRule<T>
{
    private readonly Func<T, ValidationContext, Task<bool>> _isSatisfied;
    private readonly Func<T, string> _message;

    /// <summary>Creates a rule.</summary>
    /// <param name="propertyName">Property the finding is attached to, or <c>null</c> for object level.</param>
    /// <param name="isSatisfied">Returns true when the rule holds.</param>
    /// <param name="message">Builds the message shown when it does not.</param>
    /// <param name="severity">Whether failing this rule blocks the operation.</param>
    /// <param name="code">Stable rule identifier.</param>
    public ValidationRule(
        string? propertyName,
        Func<T, ValidationContext, Task<bool>> isSatisfied,
        Func<T, string> message,
        ValidationSeverity severity = ValidationSeverity.Error,
        string? code = null)
    {
        ArgumentNullException.ThrowIfNull(isSatisfied);
        ArgumentNullException.ThrowIfNull(message);

        PropertyName = propertyName;
        Severity = severity;
        Code = code;

        _isSatisfied = isSatisfied;
        _message = message;
    }

    /// <summary>Property this rule reports against, or <c>null</c> for an object-level rule.</summary>
    public string? PropertyName { get; }

    /// <summary>Severity of the finding this rule produces.</summary>
    public ValidationSeverity Severity { get; }

    /// <summary>Stable identifier for the rule.</summary>
    public string? Code { get; }

    /// <summary>
    /// Runs the rule.
    /// </summary>
    /// <returns>The finding, or <c>null</c> when the rule holds.</returns>
    public async Task<ValidationError?> EvaluateAsync(T instance, ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (await _isSatisfied(instance, context).ConfigureAwait(false))
        {
            return null;
        }

        return new ValidationError(PropertyName, _message(instance), Severity, Code);
    }
}
