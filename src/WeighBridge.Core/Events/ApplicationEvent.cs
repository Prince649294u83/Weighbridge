using WeighBridge.Core.Diagnostics;

namespace WeighBridge.Core.Events;

/// <summary>
/// Base class for every message that travels over <see cref="IEventBus"/>.
/// </summary>
/// <remarks>
/// <para>
/// Modules never reference each other. A module that has something to announce
/// publishes an event derived from this class; modules that care subscribe. Neither
/// side knows the other exists, which is what allows a module to be added, removed or
/// replaced without touching anything else.
/// </para>
/// <para>
/// Derived events should be immutable: an event describes something that has already
/// happened, and several handlers see the same instance. A handler that mutated it
/// would change what later handlers observe.
/// </para>
/// </remarks>
public abstract class ApplicationEvent
{
    /// <summary>
    /// Initialises the envelope fields every event carries.
    /// </summary>
    /// <param name="source">
    /// Component that raised the event, for diagnostics. Defaults to the concrete
    /// event type name.
    /// </param>
    /// <param name="correlationId">
    /// Identifier tying this event to the operation that caused it. Defaults to the
    /// ambient <see cref="CorrelationScope"/>, so an event published inside a command
    /// is automatically correlated with it.
    /// </param>
    protected ApplicationEvent(string? source = null, string? correlationId = null)
    {
        EventId = Guid.NewGuid();
        OccurredAt = DateTimeOffset.Now;
        Source = string.IsNullOrWhiteSpace(source) ? GetType().Name : source;
        CorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? CorrelationScope.Current
            : correlationId;
    }

    /// <summary>Unique identity of this occurrence.</summary>
    public Guid EventId { get; }

    /// <summary>When the event happened, in local time with offset.</summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>Component that raised the event.</summary>
    public string Source { get; }

    /// <summary>
    /// Correlation identifier of the originating operation, or <c>null</c> when the
    /// event was raised outside any correlated operation.
    /// </summary>
    public string? CorrelationId { get; }

    /// <summary>Concrete event type name, convenient for logs and audit records.</summary>
    public string EventName => GetType().Name;

    /// <inheritdoc />
    public override string ToString() => $"{EventName} from {Source} at {OccurredAt:HH:mm:ss.fff}";
}
