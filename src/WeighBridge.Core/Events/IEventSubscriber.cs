namespace WeighBridge.Core.Events;

/// <summary>
/// The subscribing half of the event bus.
/// </summary>
/// <remarks>
/// Depend on this interface rather than on <see cref="IEventBus"/> when a component only
/// listens. Every <c>Subscribe</c> overload returns a handle whose lifetime governs the
/// subscription — see <see cref="IEventSubscription"/> for why.
/// </remarks>
public interface IEventSubscriber
{
    /// <summary>
    /// Subscribes a synchronous handler. Keep the returned handle alive for as long as
    /// the subscription should last.
    /// </summary>
    IEventSubscription Subscribe<TEvent>(
        ApplicationEventHandler<TEvent> handler,
        EventSubscriptionOptions? options = null)
        where TEvent : ApplicationEvent;

    /// <summary>
    /// Subscribes an asynchronous handler. Keep the returned handle alive for as long as
    /// the subscription should last.
    /// </summary>
    IEventSubscription Subscribe<TEvent>(
        AsyncApplicationEventHandler<TEvent> handler,
        EventSubscriptionOptions? options = null)
        where TEvent : ApplicationEvent;

    /// <summary>
    /// Subscribes a synchronous handler whose lifetime follows <paramref name="owner"/>.
    /// </summary>
    /// <param name="owner">
    /// Object the subscription belongs to, normally <c>this</c>. The bus references it
    /// weakly; the subscription lapses once the owner is collected, so a ViewModel can
    /// subscribe without storing a handle or implementing <see cref="IDisposable"/>.
    /// </param>
    /// <param name="handler">The handler to invoke.</param>
    /// <param name="options">Delivery settings.</param>
    IEventSubscription Subscribe<TEvent>(
        object owner,
        ApplicationEventHandler<TEvent> handler,
        EventSubscriptionOptions? options = null)
        where TEvent : ApplicationEvent;

    /// <summary>
    /// Subscribes an asynchronous handler whose lifetime follows <paramref name="owner"/>.
    /// </summary>
    IEventSubscription Subscribe<TEvent>(
        object owner,
        AsyncApplicationEventHandler<TEvent> handler,
        EventSubscriptionOptions? options = null)
        where TEvent : ApplicationEvent;

    /// <summary>
    /// Removes a subscription registered with the same handler delegate.
    /// </summary>
    /// <returns>True when a matching subscription was found and removed.</returns>
    /// <remarks>
    /// Provided for symmetry with the classic subscribe/unsubscribe pair. Disposing the
    /// handle is the preferred route: it cannot fail to match, whereas a handler written
    /// as a lambda produces a different delegate instance on every evaluation and can
    /// never be unsubscribed this way.
    /// </remarks>
    bool Unsubscribe<TEvent>(ApplicationEventHandler<TEvent> handler)
        where TEvent : ApplicationEvent;

    /// <summary>Removes a subscription registered with the same asynchronous handler.</summary>
    bool Unsubscribe<TEvent>(AsyncApplicationEventHandler<TEvent> handler)
        where TEvent : ApplicationEvent;

    /// <summary>
    /// Removes every subscription owned by <paramref name="owner"/>. Useful in a
    /// <c>Dispose</c> when a component registered several handlers.
    /// </summary>
    /// <returns>The number of subscriptions removed.</returns>
    int UnsubscribeAll(object owner);
}
