namespace WeighBridge.Core.Commands;

/// <summary>
/// How a command finished.
/// </summary>
/// <remarks>
/// Each failure kind is separate because the operator is told a different thing and a
/// different person has to act. Invalid data is theirs to fix; a denial needs a supervisor;
/// a fault needs support. Collapsing them into one <c>false</c> is what makes an application
/// say "operation failed" and leave nobody knowing what to do next.
/// </remarks>
public enum CommandOutcome
{
    /// <summary>The command ran and did what it was asked to.</summary>
    Succeeded = 0,

    /// <summary>Validation found something blocking. The command never ran.</summary>
    ValidationFailed = 1,

    /// <summary>The operator is not permitted to do this. The command never ran.</summary>
    Denied = 2,

    /// <summary>The command was cancelled, by the operator or by shutdown.</summary>
    Cancelled = 3,

    /// <summary>The command threw.</summary>
    Failed = 4,
}

/// <summary>
/// What came of executing a command.
/// </summary>
/// <remarks>
/// Returned rather than thrown. A denial and a validation failure are ordinary outcomes on a
/// weighbridge, and a caller that had to catch three exception types to tell them apart would
/// eventually catch none of them.
/// </remarks>
public class CommandResult
{
    private static readonly CommandResult SucceededInstance = new(CommandOutcome.Succeeded, null, null, null, null);

    /// <summary>Creates a result. Use the factory methods.</summary>
    protected CommandResult(
        CommandOutcome outcome,
        string? message,
        Exception? error,
        Validation.ValidationResult? validation,
        Security.AuthorizationResult? authorization)
    {
        Outcome = outcome;
        Message = message;
        Error = error;
        Validation = validation;
        Authorization = authorization;
    }

    /// <summary>A plain success carrying no value.</summary>
    public static CommandResult Succeeded => SucceededInstance;

    /// <summary>How it finished.</summary>
    public CommandOutcome Outcome { get; }

    /// <summary>True only when the command actually ran to completion.</summary>
    public bool IsSuccess => Outcome == CommandOutcome.Succeeded;

    /// <summary>What to tell the operator, on any outcome that needs saying.</summary>
    public string? Message { get; }

    /// <summary>The exception, when the command threw.</summary>
    public Exception? Error { get; }

    /// <summary>The findings, when validation was what stopped it.</summary>
    public Validation.ValidationResult? Validation { get; }

    /// <summary>The denial, when permission was what stopped it.</summary>
    public Security.AuthorizationResult? Authorization { get; }

    /// <summary>Creates a success carrying a message worth showing.</summary>
    public static CommandResult Success(string message)
        => new(CommandOutcome.Succeeded, message, null, null, null);

    /// <summary>Creates a validation failure.</summary>
    /// <remarks>
    /// The message is the single finding when there is only one, and a count otherwise. A
    /// notification has room for one line; the caller that wants all of them reads
    /// <see cref="Validation"/> and shows a summary.
    /// </remarks>
    public static CommandResult Invalid(Validation.ValidationResult validation)
    {
        ArgumentNullException.ThrowIfNull(validation);

        var blocking = validation.Blocking.ToArray();

        var message = blocking.Length switch
        {
            0 => "The information supplied is not valid.",
            1 => blocking[0].Message,
            _ => $"{blocking.Length} problems need fixing before this can be saved.",
        };

        return new CommandResult(CommandOutcome.ValidationFailed, message, null, validation, null);
    }

    /// <summary>Creates a permission denial.</summary>
    public static CommandResult Denied(Security.AuthorizationResult authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        return new CommandResult(CommandOutcome.Denied, authorization.Reason, null, null, authorization);
    }

    /// <summary>Creates a cancelled result.</summary>
    public static CommandResult Cancelled(string? message = null)
        => new(CommandOutcome.Cancelled, message ?? "The operation was cancelled.", null, null, null);

    /// <summary>Creates a failure from the exception that caused it.</summary>
    public static CommandResult Failed(Exception error, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new CommandResult(CommandOutcome.Failed, message ?? error.Message, error, null, null);
    }

    /// <inheritdoc />
    public override string ToString() => Message is null ? Outcome.ToString() : $"{Outcome}: {Message}";
}

/// <summary>
/// A result that carries what the command produced.
/// </summary>
/// <remarks>
/// The identity of a saved record, most often — the caller needs it to navigate to what it
/// just created, and reading it back out of the command object afterwards would work only
/// for commands that kept it.
/// </remarks>
/// <typeparam name="TValue">Type of the produced value.</typeparam>
public sealed class CommandResult<TValue> : CommandResult
{
    private CommandResult(
        CommandOutcome outcome,
        TValue? value,
        string? message,
        Exception? error,
        Validation.ValidationResult? validation,
        Security.AuthorizationResult? authorization)
        : base(outcome, message, error, validation, authorization)
        => Value = value;

    /// <summary>What the command produced. Meaningful only on success.</summary>
    public TValue? Value { get; }

    /// <summary>Creates a success carrying a value.</summary>
    public static CommandResult<TValue> Success(TValue value, string? message = null)
        => new(CommandOutcome.Succeeded, value, message, null, null, null);

    /// <summary>Restates a non-success result of the untyped kind as a typed one.</summary>
    public static CommandResult<TValue> From(CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new CommandResult<TValue>(
            result.Outcome,
            default,
            result.Message,
            result.Error,
            result.Validation,
            result.Authorization);
    }
}
