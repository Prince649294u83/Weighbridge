using WeighBridge.Core.Busy;
using WeighBridge.Core.Security;

namespace WeighBridge.Core.Commands;

/// <summary>
/// What a command is given when it runs.
/// </summary>
/// <remarks>
/// <para>
/// Handed to the command rather than injected into it, because these values differ per
/// execution: the correlation identifier, the token, the progress sink for this run. A
/// command's constructor takes its repositories; the context carries the run.
/// </para>
/// <para>
/// Deliberately holds no service provider. A command that could resolve anything from here
/// would be a service locator with extra steps, and its dependencies would stop being
/// visible in its constructor.
/// </para>
/// </remarks>
public sealed class CommandContext
{
    private readonly IProgressSink? _progress;

    /// <summary>Creates a context for one execution.</summary>
    public CommandContext(
        string commandName,
        string correlationId,
        OperatorIdentity operatorIdentity,
        CancellationToken cancellationToken,
        IProgressSink? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(operatorIdentity);

        CommandName = commandName;
        CorrelationId = correlationId;
        Operator = operatorIdentity;
        CancellationToken = cancellationToken;

        _progress = progress;
    }

    /// <summary>Name of the command being run.</summary>
    public string CommandName { get; }

    /// <summary>
    /// Ties every log line, audit entry and event from this execution together.
    /// </summary>
    /// <remarks>
    /// The same identifier the logging framework's <see cref="Diagnostics.CorrelationScope"/>
    /// puts on log lines, so a support call about one slip can be traced through a day's log
    /// with a single search.
    /// </remarks>
    public string CorrelationId { get; }

    /// <summary>Who the command is running on behalf of.</summary>
    public OperatorIdentity Operator { get; }

    /// <summary>
    /// Cancelled by the operator, by a configured timeout, or by shutdown.
    /// </summary>
    /// <remarks>
    /// A command that does anything long has to pass this down. Ignoring it is what makes an
    /// application take thirty seconds to close.
    /// </remarks>
    public CancellationToken CancellationToken { get; }

    /// <summary>Detail added to the audit entry by the command as it runs.</summary>
    public IDictionary<string, object?> AuditData { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>Reports the step being worked on.</summary>
    /// <remarks>
    /// Does nothing when the execution has no busy indicator, so a command can report
    /// unconditionally rather than testing first.
    /// </remarks>
    public void ReportStatus(string status) => _progress?.ReportStatus(status);

    /// <summary>Reports how far along the command is.</summary>
    public void ReportProgress(double percentage, string status)
        => _progress?.Report(percentage, status);

    /// <summary>Records a value on the audit entry for this execution.</summary>
    public CommandContext Audit(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        AuditData[key] = value;
        return this;
    }
}
