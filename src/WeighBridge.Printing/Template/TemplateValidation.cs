namespace WeighBridge.Printing.Template;

/// <summary>
/// Specific error categories for template validation diagnostics.
/// </summary>
public enum TemplateErrorCategory
{
    /// <summary>Token is not in the recognized whitelist.</summary>
    UnknownToken,

    /// <summary>Directive tag syntax is invalid or unrecognized.</summary>
    MalformedDirective,

    /// <summary>Opening tag without a matching closing bracket.</summary>
    UnclosedToken,

    /// <summary>Illegal or improperly nested formatting tags.</summary>
    InvalidNesting,

    /// <summary>Directive is not supported by the target printer profile (e.g. &lt;Cut&gt; on GDI).</summary>
    CapabilityMismatch,

    /// <summary>Template exceeds defensive size, line length, or token length limits.</summary>
    LimitExceeded
}

/// <summary>
/// A diagnostic validation issue found during template parsing or validation.
/// </summary>
/// <param name="Category">The structured error category.</param>
/// <param name="Message">Descriptive explanation for operator feedback.</param>
/// <param name="LineNumber">1-based line number where the issue occurred.</param>
/// <param name="ColumnNumber">1-based column position where the issue occurred.</param>
public sealed record TemplateValidationError(
    TemplateErrorCategory Category,
    string Message,
    int LineNumber = 1,
    int ColumnNumber = 1
);

/// <summary>
/// Result of template parsing and validation.
/// </summary>
/// <param name="IsValid">True when no blocking validation errors were found.</param>
/// <param name="Errors">Collection of diagnostic validation issues.</param>
public sealed record TemplateValidationResult(
    bool IsValid,
    IReadOnlyList<TemplateValidationError> Errors)
{
    /// <summary>Creates a successful validation result.</summary>
    public static TemplateValidationResult Success() => new(true, []);

    /// <summary>Creates a failed validation result with diagnostic errors.</summary>
    public static TemplateValidationResult Failure(IReadOnlyList<TemplateValidationError> errors) => new(false, errors);

    /// <summary>Creates a failed validation result with a single error.</summary>
    public static TemplateValidationResult Failure(TemplateErrorCategory category, string message, int line = 1, int col = 1)
        => new(false, [new TemplateValidationError(category, message, line, col)]);
}

/// <summary>
/// Exception thrown when a template cannot be parsed or exceeds defensive limits.
/// </summary>
public sealed class TemplateParseException : Exception
{
    /// <summary>Validation errors causing the parse failure.</summary>
    public IReadOnlyList<TemplateValidationError> Errors { get; }

    /// <summary>Initializes a new parse exception with errors.</summary>
    public TemplateParseException(string message, IReadOnlyList<TemplateValidationError> errors)
        : base(message)
    {
        Errors = errors;
    }

    /// <summary>Initializes a new parse exception with a single error.</summary>
    public TemplateParseException(TemplateErrorCategory category, string message, int line = 1, int col = 1)
        : base(message)
    {
        Errors = [new TemplateValidationError(category, message, line, col)];
    }
}
