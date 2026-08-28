namespace WeighBridge.Core.Validation;

/// <summary>
/// Implemented by something that knows how to check itself.
/// </summary>
/// <remarks>
/// <para>
/// This is what the command pipeline looks for. A command that carries its own rules is
/// validated before it runs, without the pipeline having to resolve a validator for a type
/// it knows nothing about.
/// </para>
/// <para>
/// A command whose rules are substantial should hold a <see cref="IValidator{T}"/> injected
/// through its constructor and delegate to it here, rather than growing the rules inline —
/// the same rules usually have to hold for an import or a synchronisation too.
/// </para>
/// </remarks>
public interface IValidatable
{
    /// <summary>
    /// Checks whether this instance is fit to be used.
    /// </summary>
    /// <remarks>
    /// Reports findings rather than throwing. An exception from here is treated by the
    /// pipeline as a failure of the validation step itself, not as invalid data.
    /// </remarks>
    Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default);
}
