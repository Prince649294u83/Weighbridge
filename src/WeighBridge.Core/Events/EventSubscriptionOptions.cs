namespace WeighBridge.Core.Events;

/// <summary>Where a handler runs when its event is published.</summary>
public enum EventDispatchMode
{
    /// <summary>
    /// Run on whichever thread published the event. Correct for anything that only
    /// touches its own state.
    /// </summary>
    PublisherThread = 0,

    /// <summary>
    /// Marshal onto the user-interface thread before running. Required for a handler
    /// that touches bound state — a weight arriving from the serial port thread cannot
    /// be written into an observable collection from that thread.
    /// </summary>
    UiThread = 1,
}

/// <summary>
/// Per-subscription delivery settings.
/// </summary>
/// <param name="DispatchMode">Thread the handler is invoked on.</param>
/// <param name="Priority">
/// Delivery order within one event type; higher runs first. Equal priorities preserve
/// subscription order. Use sparingly — a handler that depends on running before another
/// is usually a sign the two belong in one handler.
/// </param>
/// <param name="ReceiveDerivedEvents">
/// When true, the handler also receives events derived from
/// <c>TEvent</c>. Subscribing to <see cref="ApplicationEvent"/> with this enabled yields
/// every event on the bus, which is how an audit log or a diagnostics window observes
/// the whole application.
/// </param>
public sealed record EventSubscriptionOptions(
    EventDispatchMode DispatchMode = EventDispatchMode.PublisherThread,
    int Priority = 0,
    bool ReceiveDerivedEvents = false)
{
    /// <summary>Publisher thread, default priority, exact type match.</summary>
    public static EventSubscriptionOptions Default { get; } = new();

    /// <summary>Marshalled onto the user-interface thread.</summary>
    public static EventSubscriptionOptions OnUiThread { get; } =
        new(EventDispatchMode.UiThread);

    /// <summary>
    /// Receives the subscribed type and everything derived from it, on the publisher's
    /// thread.
    /// </summary>
    public static EventSubscriptionOptions IncludingDerived { get; } =
        new(ReceiveDerivedEvents: true);
}
