namespace WeighBridge.Core.Commands;

/// <summary>
/// Which of the pipeline's steps to run for one execution.
/// </summary>
/// <remarks>
/// <para>
/// The defaults are what an operator-initiated action wants: validate, authorise, show the
/// busy indicator, record an audit entry, announce the result, allow undo. A caller only
/// changes something when the situation genuinely differs — a command replayed from an
/// import does not want a notification per row.
/// </para>
/// <para>
/// Validation and permission checking can be turned off here, which sounds dangerous and is
/// why it is explicit at the call site rather than a property on the command. A command
/// cannot exempt itself.
/// </para>
/// </remarks>
public sealed record CommandExecutionOptions
{
    /// <summary>What an operator-initiated action uses.</summary>
    public static CommandExecutionOptions Default { get; } = new();

    /// <summary>
    /// Runs without notifications, busy indicator or undo recording.
    /// </summary>
    /// <remarks>
    /// For a command running inside another command, or one row of a bulk operation: the
    /// outer operation owns the indicator and the single notification, and undo is recorded
    /// once for the batch rather than once per row.
    /// </remarks>
    public static CommandExecutionOptions Nested { get; } = new()
    {
        ShowBusyIndicator = false,
        NotifyOnSuccess = false,
        NotifyOnFailure = false,
        RecordUndo = false,
    };

    /// <summary>Whether to validate a command that implements <see cref="Validation.IValidatable"/>.</summary>
    public bool Validate { get; init; } = true;

    /// <summary>Whether to enforce a command's <see cref="Security.IRequiresPermission"/> declaration.</summary>
    public bool CheckPermissions { get; init; } = true;

    /// <summary>Whether to hold the busy indicator for the duration.</summary>
    public bool ShowBusyIndicator { get; init; } = true;

    /// <summary>Whether the operator may cancel it.</summary>
    public bool IsCancellable { get; init; }

    /// <summary>Text for the busy indicator. Falls back to the command's name.</summary>
    public string? BusyTitle { get; init; }

    /// <summary>Whether to record an audit entry when it succeeds.</summary>
    public bool Audit { get; init; } = true;

    /// <summary>Whether to raise a notification when it succeeds.</summary>
    public bool NotifyOnSuccess { get; init; }

    /// <summary>Whether to raise a notification when it does not.</summary>
    public bool NotifyOnFailure { get; init; } = true;

    /// <summary>Whether to publish a <see cref="Events.Catalog.CommandExecutedEvent"/> on the bus.</summary>
    public bool PublishEvent { get; init; } = true;

    /// <summary>Whether to record an undoable command in the history.</summary>
    public bool RecordUndo { get; init; } = true;

    /// <summary>
    /// How long to allow before cancelling, or <c>null</c> for no limit.
    /// </summary>
    /// <remarks>
    /// Off by default. A weighment save that takes ninety seconds over a bad network share
    /// is slow, not wrong, and cancelling it halfway is worse than waiting.
    /// </remarks>
    public TimeSpan? Timeout { get; init; }
}
