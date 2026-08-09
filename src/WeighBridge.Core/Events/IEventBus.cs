namespace WeighBridge.Core.Events;

/// <summary>
/// Application-wide publish/subscribe channel — the only route by which one module
/// learns that something happened in another.
/// </summary>
/// <remarks>
/// <para>
/// A module publishes; interested modules subscribe. Neither knows the other exists, so
/// a module can be added, replaced or removed without a single edit elsewhere. This is
/// the mechanism that keeps the Open/Closed Principle enforceable across a codebase this
/// size.
/// </para>
/// <para>
/// Register as a singleton. It is safe to publish and subscribe from any thread; the
/// subscription tables are guarded, and handlers that need the user-interface thread say
/// so through <see cref="EventSubscriptionOptions.OnUiThread"/> instead of marshalling
/// themselves.
/// </para>
/// <para>
/// Most components should depend on <see cref="IEventPublisher"/> or
/// <see cref="IEventSubscriber"/> instead of this combined interface, so their
/// constructor states which half they actually use.
/// </para>
/// </remarks>
public interface IEventBus : IEventPublisher, IEventSubscriber
{
    /// <summary>Live subscription count, for diagnostics and tests.</summary>
    int SubscriptionCount { get; }

    /// <summary>
    /// Removes every subscription. Intended for shutdown and for test isolation, not for
    /// normal operation — a module must not clear another module's subscriptions.
    /// </summary>
    void Reset();
}
