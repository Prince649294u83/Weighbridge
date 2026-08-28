namespace WeighBridge.Core.Validation;

/// <summary>
/// Whether a validation finding blocks the operation.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>Informational — worth showing but does not prevent anything.</summary>
    Information = 0,

    /// <summary>A concern that may or may not need fixing. Does not block the operation.</summary>
    Warning = 1,

    /// <summary>A defect that must be fixed before the operation can proceed.</summary>
    Error = 2,
}
