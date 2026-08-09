namespace WeighBridge.Core.Events;

/// <summary>
/// Synchronous handler for an event of type <typeparamref name="TEvent"/>.
/// </summary>
/// <remarks>
/// Handlers must be quick and must not throw: the bus isolates a failure so the
/// remaining subscribers still receive the event, but a handler that blocks delays
/// every subscriber behind it. Anything slow belongs on a background task.
/// </remarks>
public delegate void ApplicationEventHandler<in TEvent>(TEvent applicationEvent)
    where TEvent : ApplicationEvent;

/// <summary>
/// Asynchronous handler for an event of type <typeparamref name="TEvent"/>.
/// </summary>
/// <remarks>
/// The cancellation token is the one passed to
/// <see cref="IEventPublisher.PublishAsync{TEvent}(TEvent, CancellationToken)"/>, which
/// during shutdown is already cancelled — a handler that honours it lets the
/// application close promptly.
/// </remarks>
public delegate Task AsyncApplicationEventHandler<in TEvent>(
    TEvent applicationEvent,
    CancellationToken cancellationToken)
    where TEvent : ApplicationEvent;
