using WeighBridge.Core.Events;

namespace WeighBridge.Services.Events;

/// <summary>
/// One registration inside <see cref="EventBus"/>.
/// </summary>
/// <remarks>
/// <para>
/// Holds the handler delegate <em>strongly</em>, and the bus holds this object
/// <em>weakly</em>. That inversion is what makes the bus leak-free while still accepting
/// lambdas.
/// </para>
/// <para>
/// The obvious design — bus holds a weak reference to the delegate — does not work.
/// A lambda that captures <c>this</c> compiles to a method on a closure object that
/// nothing else references, so it would be collected almost immediately and the
/// subscription would vanish before the first event. Inverting it means the delegate
/// lives exactly as long as the handle, and the handle's owner decides that.
/// </para>
/// </remarks>
internal sealed class EventSubscription : IEventSubscription
{
    private readonly EventBus _bus;
    private readonly Func<ApplicationEvent, CancellationToken, Task> _invoker;
    private readonly WeakReference<object>? _owner;

    private bool _disposed;

    private EventSubscription(
        EventBus bus,
        Type eventType,
        Delegate handler,
        Func<ApplicationEvent, CancellationToken, Task> invoker,
        EventSubscriptionOptions options,
        object? owner)
    {
        _bus = bus;
        _invoker = invoker;
        EventType = eventType;
        Handler = handler;
        Options = options;
        _owner = owner is null ? null : new WeakReference<object>(owner);
    }

    /// <inheritdoc />
    public Type EventType { get; }

    /// <summary>
    /// The delegate as supplied by the subscriber, kept so
    /// <see cref="IEventSubscriber.Unsubscribe{TEvent}(ApplicationEventHandler{TEvent})"/>
    /// can match it.
    /// </summary>
    public Delegate Handler { get; }

    /// <summary>Delivery settings chosen at subscription time.</summary>
    public EventSubscriptionOptions Options { get; }

    /// <inheritdoc />
    /// <remarks>
    /// False once disposed, and also once an owner supplied at subscription time has been
    /// collected — that is how the owner-scoped overload lapses without an explicit
    /// unsubscribe.
    /// </remarks>
    public bool IsActive => !_disposed && IsOwnerAlive;

    /// <summary>True when no owner was supplied, or the owner is still reachable.</summary>
    private bool IsOwnerAlive => _owner is null || _owner.TryGetTarget(out _);

    /// <summary>
    /// Creates a subscription for a synchronous handler.
    /// </summary>
    public static EventSubscription ForSync<TEvent>(
        EventBus bus,
        ApplicationEventHandler<TEvent> handler,
        EventSubscriptionOptions options,
        object? owner)
        where TEvent : ApplicationEvent
    {
        // The cast is safe: the bus only ever routes an event to subscriptions
        // registered for that type or one of its base types.
        return new EventSubscription(
            bus,
            typeof(TEvent),
            handler,
            (applicationEvent, _) =>
            {
                handler((TEvent)applicationEvent);
                return Task.CompletedTask;
            },
            options,
            owner);
    }

    /// <summary>
    /// Creates a subscription for an asynchronous handler.
    /// </summary>
    public static EventSubscription ForAsync<TEvent>(
        EventBus bus,
        AsyncApplicationEventHandler<TEvent> handler,
        EventSubscriptionOptions options,
        object? owner)
        where TEvent : ApplicationEvent
    {
        return new EventSubscription(
            bus,
            typeof(TEvent),
            handler,
            (applicationEvent, cancellationToken) => handler((TEvent)applicationEvent, cancellationToken),
            options,
            owner);
    }

    /// <summary>True when this subscription belongs to <paramref name="candidate"/>.</summary>
    public bool IsOwnedBy(object candidate)
        => _owner is not null && _owner.TryGetTarget(out var owner) && ReferenceEquals(owner, candidate);

    /// <summary>Invokes the handler. The caller owns exception isolation.</summary>
    public Task InvokeAsync(ApplicationEvent applicationEvent, CancellationToken cancellationToken)
        => _invoker(applicationEvent, cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _bus.Remove(this);
    }
}
