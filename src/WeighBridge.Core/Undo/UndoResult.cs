namespace WeighBridge.Core.Undo;

/// <summary>
/// How an undo or redo attempt turned out.
/// </summary>
public enum UndoOutcome
{
    /// <summary>The command was undone or redone.</summary>
    Succeeded = 0,

    /// <summary>There was nothing in the history to act on.</summary>
    NothingToDo = 1,

    /// <summary>The compensating operation threw. The entry was dropped from the history.</summary>
    Failed = 2,

    /// <summary>The attempt was cancelled before it finished.</summary>
    Cancelled = 3,
}

/// <summary>
/// The result of asking the manager to undo or redo.
/// </summary>
/// <remarks>
/// A result rather than an exception because a failed undo is not exceptional from the
/// caller's side — it is something the operator has to be told about, and a ViewModel that
/// had to wrap every undo in a try/catch would eventually forget to.
/// </remarks>
public sealed class UndoResult
{
    private static readonly UndoResult NothingInstance = new(UndoOutcome.NothingToDo, null, null);

    private UndoResult(UndoOutcome outcome, string? description, Exception? error)
    {
        Outcome = outcome;
        Description = description;
        Error = error;
    }

    /// <summary>Nothing was there to undo or redo.</summary>
    public static UndoResult Nothing => NothingInstance;

    /// <summary>How it turned out.</summary>
    public UndoOutcome Outcome { get; }

    /// <summary>What was undone or redone, when something was.</summary>
    public string? Description { get; }

    /// <summary>Why it failed, when it did.</summary>
    public Exception? Error { get; }

    /// <summary>True when the history actually moved.</summary>
    public bool Succeeded => Outcome == UndoOutcome.Succeeded;

    /// <summary>Records a success.</summary>
    public static UndoResult Success(string description) => new(UndoOutcome.Succeeded, description, null);

    /// <summary>Records a failure and the exception behind it.</summary>
    public static UndoResult Failed(string description, Exception error)
        => new(UndoOutcome.Failed, description, error);

    /// <summary>Records a cancelled attempt.</summary>
    public static UndoResult Cancelled(string description) => new(UndoOutcome.Cancelled, description, null);

    /// <inheritdoc />
    public override string ToString()
        => Description is null ? Outcome.ToString() : $"{Outcome}: {Description}";
}
