using WeighBridge.Core.Commands;

namespace WeighBridge.Core.Events.Catalog;

/// <summary>
/// Announces that a command finished, whatever it finished as.
/// </summary>
/// <remarks>
/// Published for every outcome, not only success. A subscriber watching for a run of
/// denials, or a panel showing the last thing that happened, needs the failures too — and
/// filtering on <see cref="Outcome"/> is the subscriber's job, not the pipeline's.
/// </remarks>
public sealed class CommandExecutedEvent(
    string commandName,
    CommandOutcome outcome,
    TimeSpan duration,
    string correlationId,
    string? message = null,
    string? source = null) : ApplicationEvent(source, correlationId)
{
    /// <summary>Name of the command that ran.</summary>
    public string CommandName { get; } = commandName;

    /// <summary>How it finished.</summary>
    public CommandOutcome Outcome { get; } = outcome;

    /// <summary>How long the whole pipeline took, including validation and authorization.</summary>
    public TimeSpan Duration { get; } = duration;

    /// <summary>What the operator was told, when anything was.</summary>
    public string? Message { get; } = message;

    /// <summary>True only when the command ran to completion.</summary>
    public bool IsSuccess => Outcome == CommandOutcome.Succeeded;

    /// <inheritdoc />
    public override string ToString()
        => $"{CommandName}: {Outcome} in {Duration.TotalMilliseconds:F0} ms";
}
