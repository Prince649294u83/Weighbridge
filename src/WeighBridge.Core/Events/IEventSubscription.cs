namespace WeighBridge.Core.Events;

/// <summary>
/// Handle on a live subscription. Disposing it unsubscribes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The handle owns the subscription's lifetime.</b> The bus holds only a weak
/// reference to it, so a subscriber that discards the handle will stop receiving events
/// at the next garbage collection. Store it in a field, or use the
/// <c>Subscribe(owner, handler)</c> overload which ties the lifetime to an object you
/// already keep alive.
/// </para>
/// <para>
/// That design is what makes the bus leak-free. A transient ViewModel that subscribes
/// and is then navigated away from becomes unreachable along with its handle, and its
/// subscription lapses without anyone having to remember to unsubscribe.
/// </para>
/// </remarks>
public interface IEventSubscription : IDisposable
{
    /// <summary>The event type this subscription listens for.</summary>
    Type EventType { get; }

    /// <summary>False once the subscription has been disposed or has lapsed.</summary>
    bool IsActive { get; }
}
