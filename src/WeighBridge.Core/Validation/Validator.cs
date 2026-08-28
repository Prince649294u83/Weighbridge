namespace WeighBridge.Core.Validation;

/// <summary>
/// Base class for a validator: declare rules in the constructor, get the running of them
/// for free.
/// </summary>
/// <remarks>
/// <para>
/// Rules run in declaration order and every one of them runs, so the operator sees the
/// whole picture rather than one failure per submission.
/// </para>
/// <para>
/// A rule that throws is not swallowed: a validator that cannot reach the database has not
/// found the data valid, and reporting "valid" there would let a duplicate ticket through.
/// </para>
/// <para>
/// Contains no business rules itself. Concrete validators for vehicles, tickets and rates
/// arrive with the modules that own those types.
/// </para>
/// </remarks>
/// <typeparam name="T">Type being validated.</typeparam>
public abstract class Validator<T> : IValidator<T>
{
    private readonly List<ValidationRule<T>> _rules = [];

    /// <inheritdoc />
    public Type ValidatedType => typeof(T);

    /// <summary>The declared rules, for tests and diagnostics.</summary>
    public IReadOnlyList<ValidationRule<T>> Rules => _rules;

    /// <inheritdoc />
    public Task<ValidationResult> ValidateAsync(T instance, CancellationToken cancellationToken = default)
        => ValidateAsync(instance, new ValidationContext(cancellationToken: cancellationToken));

    /// <inheritdoc />
    public Task<ValidationResult> ValidatePropertyAsync(
        T instance,
        string propertyName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        return ValidateAsync(
            instance,
            ValidationContext.ForProperty(propertyName, cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Validates against an explicit context — the overload a caller uses to say this is an
    /// update rather than a create.
    /// </summary>
    /// <remarks>
    /// When the context names a property, only the rules bound to that property run, so a
    /// form validating as the operator types does not re-run an object-level rule that
    /// cannot pass until the last field is filled in.
    /// </remarks>
    public async Task<ValidationResult> ValidateAsync(T instance, ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var applicable = context.PropertyName is { } property
            ? _rules.Where(rule => string.Equals(rule.PropertyName, property, StringComparison.Ordinal))
            : _rules;

        var findings = new List<ValidationError>();

        foreach (var rule in applicable)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (await rule.EvaluateAsync(instance, context).ConfigureAwait(false) is { } error)
            {
                findings.Add(error);
            }
        }

        var additional = await ValidateAdditionalAsync(instance, context).ConfigureAwait(false);

        findings.AddRange(additional.Errors);

        return ValidationResult.From(findings);
    }

    /// <inheritdoc />
    Task<ValidationResult> IValidator.ValidateAsync(object instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (instance is not T typed)
        {
            throw new ArgumentException(
                $"This validator accepts {typeof(T).Name}, not {instance.GetType().Name}.",
                nameof(instance));
        }

        return ValidateAsync(typed, cancellationToken);
    }

    /// <summary>Adds a rule that has to ask something asynchronous.</summary>
    protected void AddRule(
        string? propertyName,
        Func<T, ValidationContext, Task<bool>> isSatisfied,
        Func<T, string> message,
        ValidationSeverity severity = ValidationSeverity.Error,
        string? code = null)
        => _rules.Add(new ValidationRule<T>(propertyName, isSatisfied, message, severity, code));

    /// <summary>Adds a synchronous property rule with a fixed message.</summary>
    protected void AddRule(
        string propertyName,
        Func<T, bool> isSatisfied,
        string message,
        ValidationSeverity severity = ValidationSeverity.Error,
        string? code = null)
    {
        ArgumentNullException.ThrowIfNull(isSatisfied);

        AddRule(
            propertyName,
            (instance, _) => Task.FromResult(isSatisfied(instance)),
            _ => message,
            severity,
            code);
    }

    /// <summary>
    /// Adds a synchronous rule about the object as a whole — a relationship between fields
    /// that belongs to no single one of them.
    /// </summary>
    protected void AddObjectRule(
        Func<T, bool> isSatisfied,
        string message,
        ValidationSeverity severity = ValidationSeverity.Error,
        string? code = null)
    {
        ArgumentNullException.ThrowIfNull(isSatisfied);

        AddRule(
            propertyName: null,
            (instance, _) => Task.FromResult(isSatisfied(instance)),
            _ => message,
            severity,
            code);
    }

    /// <summary>
    /// Findings that do not fit the rule list — a call to another validator for a nested
    /// object, most often. Runs after the rules; the default returns nothing.
    /// </summary>
    protected virtual Task<ValidationResult> ValidateAdditionalAsync(T instance, ValidationContext context)
        => Task.FromResult(ValidationResult.Success);
}
