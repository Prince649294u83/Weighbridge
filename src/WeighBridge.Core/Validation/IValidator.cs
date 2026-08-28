namespace WeighBridge.Core.Validation;

/// <summary>
/// Validates one kind of object.
/// </summary>
/// <remarks>
/// A validator lives beside the type it validates, never in a view's code-behind: the same
/// rules have to hold whether the object arrived from a form, an import or a background
/// synchronisation.
/// </remarks>
/// <typeparam name="T">Type being validated.</typeparam>
public interface IValidator<in T> : IValidator
{
    /// <summary>Validates an instance.</summary>
    Task<ValidationResult> ValidateAsync(T instance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a single property, for validation as the operator types.
    /// </summary>
    /// <remarks>
    /// Runs only the rules bound to that property, so a form does not re-run an
    /// object-level rule that cannot pass until the last field is filled in.
    /// </remarks>
    Task<ValidationResult> ValidatePropertyAsync(
        T instance,
        string propertyName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The type-agnostic face of a validator.
/// </summary>
/// <remarks>
/// Exists so the command pipeline can run whatever validator a command carries without
/// being generic over it. Passing an instance of the wrong type is a programming error and
/// throws rather than reporting itself as a validation failure — an operator must never see
/// a wiring mistake dressed up as bad data.
/// </remarks>
public interface IValidator
{
    /// <summary>The type this validator accepts.</summary>
    Type ValidatedType { get; }

    /// <summary>Validates an instance of <see cref="ValidatedType"/>.</summary>
    Task<ValidationResult> ValidateAsync(object instance, CancellationToken cancellationToken = default);
}
