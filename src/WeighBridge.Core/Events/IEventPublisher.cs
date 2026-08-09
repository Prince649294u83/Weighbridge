namespace WeighBridge.Core.Events;

/// <summary>
/// The publishing half of the event bus.
/// </summary>
/// <remarks>
/// Depend on this interface rather than on <see cref="IEventBus"/> when a component only
/// announces things. The narrower dependency documents the intent and keeps the
/// component out of the subscription business entirely.
/// </remarks>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes without waiting for handlers.
    /// </summary>
    /// <remarks>
    /// Returns as soon as the handlers have been dispatched. Handler failures are logged,
    /// never surfaced to the publisher — a subscriber must not be able to break the
    /// operation that announced the event. Use
    /// <see cref="PublishAsync{TEvent}(TEvent, CancellationToken)"/> when the caller has
    /// to know that every handler has finished.
    /// </remarks>
    void Publish<TEvent>(TEvent applicationEvent)
        where TEvent : ApplicationEvent;

    /// <summary>
    /// Publishes and completes once every handler has run.
    /// </summary>
    /// <remarks>
    /// Handlers run in priority order, one after another rather than concurrently, so a
    /// handler may rely on the state left by a higher-priority one. Individual failures
    /// are isolated and logged.
    /// </remarks>
    Task PublishAsync<TEvent>(TEvent applicationEvent, CancellationToken cancellationToken = default)
        where TEvent : ApplicationEvent;
}
