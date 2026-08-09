using Microsoft.Extensions.Logging;
using WeighBridge.Core.Events;
using WeighBridge.Core.Threading;

namespace WeighBridge.Services.Events;

/// <summary>
/// Thread-safe, weak-referencing implementation of <see cref="IEventBus"/>.
/// </summary>
/// <remarks>
/// <para>
/// Subscriptions are held through <see cref="WeakReference{T}"/>, so the bus never keeps a
/// subscriber alive. In an application that runs for months on a weighbridge terminal and
/// creates a fresh ViewModel on every navigation, a bus holding strong references would
/// accumulate every ViewModel the operator ever opened. The lifetime is instead owned by
/// the <see cref="IEventSubscription"/> handle the caller receives — see
/// <see cref="EventSubscription"/> for why the reference is inverted rather than weak on
/// the delegate itself.
/// </para>
/// <para>
/// All mutation happens under a single lock, and dispatch happens outside it against a
/// snapshot. That means a handler is free to publish, subscribe or unsubscribe while it
/// runs without deadlocking or invalidating the iteration in progress.
/// </para>
/// <para>
/// A failing handler never reaches the publisher. A subscriber must not be able to break
/// the operation that announced the event, so every invocation is guarded and failures go
/// to the log.
/// </para>
/// </remarks>
public sealed class EventBus : IEventBus
{
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<EventBus> _logger;

    /// <summary>Guards <see cref="_subscriptions"/>. Never held during dispatch.</summary>
    private readonly object _gate = new();

    /// <summary>
    /// Subscriptions keyed by the event type they were registered for — not by the type
    /// being published. Resolving a published event to its handlers walks the type
    /// hierarchy, which is what makes base-type subscriptions possible.
    /// </summary>
    private readonly Dictionary<Type, List<WeakReference<EventSubscription>>> _subscriptions = [];

    /// <summary>Creates the bus.</summary>
    /// <param name="dispatcher">Used for subscriptions that asked for the UI thread.</param>
    /// <param name="logger">Receives dispatch diagnostics and handler failures.</param>
    public EventBus(IUiDispatcher dispatcher, ILogger<EventBus> logger)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public int SubscriptionCount
    {
        get
        {
            lock (_gate)
            {
                var total = 0;

                foreach (var list in _subscriptions.Values)
                {
                    Prune(list);
                    total += list.Count;
                }

                return total;
            }
        }
    }

    // -- Subscription -------------------------------------------------------------

    /// <inheritdoc />
    public IEventSubscription Subscribe<TEvent>(
        ApplicationEventHandler<TEvent> handler,
        EventSubscriptionOptions? options = null)
        where TEvent : ApplicationEvent
        => SubscribeCore(owner: null, handler, options);

    /// <inheritdoc />
    public IEventSubscription Subscribe<TEvent>(
        AsyncApplicationEventHandler<TEvent> handler,
        EventSubscriptionOptions? options = null)
        where TEvent : ApplicationEvent
        => SubscribeCore(owner: null, handler, options);

    /// <inheritdoc />
    public IEventSubscription Subscribe<TEvent>(
        object owner,
        ApplicationEventHandler<TEvent> handler,
        EventSubscriptionOptions? options = null)
        where TEvent : ApplicationEvent
    {
        ArgumentNullException.ThrowIfNull(owner);
        return SubscribeCore(owner, handler, options);
    }

    /// <inheritdoc />
    public IEventSubscription Subscribe<TEvent>(
        object owner,
        AsyncApplicationEventHandler<TEvent> handler,
        EventSubscriptionOptions? options = null)
        where TEvent : ApplicationEvent
    {
        ArgumentNullException.ThrowIfNull(owner);
        return SubscribeCore(owner, handler, options);
    }

    /// <inheritdoc />
    public bool Unsubscribe<TEvent>(ApplicationEventHandler<TEvent> handler)
        where TEvent : ApplicationEvent
        => UnsubscribeCore(typeof(TEvent), handler);

    /// <inheritdoc />
    public bool Unsubscribe<TEvent>(AsyncApplicationEventHandler<TEvent> handler)
        where TEvent : ApplicationEvent
        => UnsubscribeCore(typeof(TEvent), handler);

    /// <inheritdoc />
    public int UnsubscribeAll(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        List<EventSubscription> removed = [];

        lock (_gate)
        {
            foreach (var list in _subscriptions.Values)
            {
                for (var index = list.Count - 1; index >= 0; index--)
                {
                    if (!list[index].TryGetTarget(out var candidate))
                    {
                        list.RemoveAt(index);
                        continue;
                    }

                    if (candidate.IsOwnedBy(owner))
                    {
                        removed.Add(candidate);
                        list.RemoveAt(index);
                    }
                }
            }
        }

        // Disposed outside the lock: Dispose calls back into Remove, which takes it.
        foreach (var subscription in removed)
        {
            subscription.Dispose();
        }

        if (removed.Count > 0)
        {
            _logger.LogTrace("Removed {Count} subscription(s) owned by {Owner}", removed.Count, owner.GetType().Name);
        }

        return removed.Count;
    }

    /// <inheritdoc />
    public void Reset()
    {
        lock (_gate)
        {
            _subscriptions.Clear();
        }

        _logger.LogDebug("Event bus subscriptions cleared");
    }

    // -- Publication --------------------------------------------------------------

    /// <inheritdoc />
    public void Publish<TEvent>(TEvent applicationEvent)
        where TEvent : ApplicationEvent
    {
        ArgumentNullException.ThrowIfNull(applicationEvent);

        var targets = ResolveTargets(applicationEvent.GetType());

        if (targets.Count == 0)
        {
            // Not a warning: most events in a running system have no subscriber for
            // most of the session, and an unheard event is a normal state of affairs.
            _logger.LogTrace("Published {Event} with no subscribers", applicationEvent.EventName);
            return;
        }

        _logger.LogDebug(
            "Publishing {Event} to {Count} subscriber(s)",
            applicationEvent.EventName,
            targets.Count);

        foreach (var subscription in targets)
        {
            if (RequiresMarshalling(subscription))
            {
                // Queued, not awaited. A synchronous publish from the serial-port thread
                // must not block until the UI thread has finished its handler.
                _dispatcher.Post(() => Invoke(subscription, applicationEvent, CancellationToken.None));
                continue;
            }

            Invoke(subscription, applicationEvent, CancellationToken.None);
        }
    }

    /// <inheritdoc />
    public async Task PublishAsync<TEvent>(TEvent applicationEvent, CancellationToken cancellationToken = default)
        where TEvent : ApplicationEvent
    {
        ArgumentNullException.ThrowIfNull(applicationEvent);

        var targets = ResolveTargets(applicationEvent.GetType());

        if (targets.Count == 0)
        {
            _logger.LogTrace("Published {Event} with no subscribers", applicationEvent.EventName);
            return;
        }

        _logger.LogDebug(
            "Publishing {Event} to {Count} subscriber(s) and awaiting completion",
            applicationEvent.EventName,
            targets.Count);

        // Sequential rather than concurrent: handlers run in priority order and a
        // lower-priority handler may depend on state a higher-priority one produced.
        // Concurrency here would make that ordering meaningless.
        foreach (var subscription in targets)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Dispatch of {Event} was cancelled part-way", applicationEvent.EventName);
                return;
            }

            await InvokeAsync(subscription, applicationEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    // -- Internals ----------------------------------------------------------------

    /// <summary>Removes <paramref name="subscription"/>. Called from its own Dispose.</summary>
    internal void Remove(EventSubscription subscription)
    {
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(subscription.EventType, out var list))
            {
                return;
            }

            for (var index = list.Count - 1; index >= 0; index--)
            {
                if (!list[index].TryGetTarget(out var candidate) || ReferenceEquals(candidate, subscription))
                {
                    list.RemoveAt(index);
                }
            }

            if (list.Count == 0)
            {
                _subscriptions.Remove(subscription.EventType);
            }
        }
    }

    private IEventSubscription SubscribeCore<TEvent>(
        object? owner,
        ApplicationEventHandler<TEvent> handler,
        EventSubscriptionOptions? options)
        where TEvent : ApplicationEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        return Register(EventSubscription.ForSync(
            this,
            handler,
            options ?? EventSubscriptionOptions.Default,
            owner));
    }

    private IEventSubscription SubscribeCore<TEvent>(
        object? owner,
        AsyncApplicationEventHandler<TEvent> handler,
        EventSubscriptionOptions? options)
        where TEvent : ApplicationEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        return Register(EventSubscription.ForAsync(
            this,
            handler,
            options ?? EventSubscriptionOptions.Default,
            owner));
    }

    private IEventSubscription Register(EventSubscription subscription)
    {
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(subscription.EventType, out var list))
            {
                list = [];
                _subscriptions[subscription.EventType] = list;
            }

            // Opportunistic: pruning as we go keeps the lists from growing with dead
            // entries in a session that never calls SubscriptionCount.
            Prune(list);
            list.Add(new WeakReference<EventSubscription>(subscription));
        }

        _logger.LogTrace(
            "Subscribed to {EventType} ({Mode}, priority {Priority})",
            subscription.EventType.Name,
            subscription.Options.DispatchMode,
            subscription.Options.Priority);

        return subscription;
    }

    private bool UnsubscribeCore(Type eventType, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        EventSubscription? match = null;

        lock (_gate)
        {
            if (_subscriptions.TryGetValue(eventType, out var list))
            {
                for (var index = list.Count - 1; index >= 0; index--)
                {
                    if (!list[index].TryGetTarget(out var candidate))
                    {
                        list.RemoveAt(index);
                        continue;
                    }

                    if (candidate.Handler.Equals(handler))
                    {
                        match = candidate;
                        list.RemoveAt(index);
                        break;
                    }
                }
            }
        }

        match?.Dispose();
        return match is not null;
    }

    /// <summary>
    /// Resolves the ordered set of subscriptions that should receive an event of
    /// <paramref name="eventType"/>.
    /// </summary>
    /// <remarks>
    /// Walks from the concrete event type up to <see cref="ApplicationEvent"/>.
    /// Subscriptions registered for the exact type always match; those registered for a
    /// base type match only when they opted into derived events. Because the walk goes
    /// derived-to-base, an exact-type handler precedes a base-type handler of equal
    /// priority — so a module's own handler runs before a global audit or diagnostics
    /// subscriber.
    /// </remarks>
    private List<EventSubscription> ResolveTargets(Type eventType)
    {
        List<(EventSubscription Subscription, int Order)> matches = [];

        lock (_gate)
        {
            var order = 0;

            for (var candidateType = eventType;
                 candidateType is not null && typeof(ApplicationEvent).IsAssignableFrom(candidateType);
                 candidateType = candidateType.BaseType)
            {
                if (!_subscriptions.TryGetValue(candidateType, out var list))
                {
                    continue;
                }

                Prune(list);

                var isExactType = candidateType == eventType;

                foreach (var reference in list)
                {
                    if (reference.TryGetTarget(out var subscription)
                        && (isExactType || subscription.Options.ReceiveDerivedEvents))
                    {
                        matches.Add((subscription, order++));
                    }
                }
            }
        }

        return [.. matches
            .OrderByDescending(match => match.Subscription.Options.Priority)
            .ThenBy(match => match.Order)
            .Select(match => match.Subscription)];
    }

    /// <summary>
    /// True when the subscription asked for the UI thread and the caller is not already
    /// on it. Publishing from the UI thread runs such a handler inline, which preserves
    /// ordering against handlers that did not ask to be marshalled.
    /// </summary>
    private bool RequiresMarshalling(EventSubscription subscription)
        => subscription.Options.DispatchMode == EventDispatchMode.UiThread && !_dispatcher.IsOnUiThread;

    /// <summary>Invokes a handler on the fire-and-forget path, isolating failures.</summary>
    private void Invoke(
        EventSubscription subscription,
        ApplicationEvent applicationEvent,
        CancellationToken cancellationToken)
    {
        // Re-checked here as well as during resolution: the snapshot was taken before
        // dispatch began, and an earlier handler may have disposed this subscription.
        if (!subscription.IsActive)
        {
            return;
        }

        try
        {
            var task = subscription.InvokeAsync(applicationEvent, cancellationToken);

            if (task.IsCompletedSuccessfully)
            {
                return;
            }

            // An asynchronous handler reached through the synchronous Publish. Nobody is
            // awaiting the task, so it is observed here — otherwise its failure would
            // resurface minutes later as an unobserved task exception on the finalizer
            // thread, detached from the event that caused it.
            _ = task.ContinueWith(
                faulted => LogFailure(subscription, applicationEvent, faulted.Exception!),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            LogFailure(subscription, applicationEvent, ex);
        }
    }

    /// <summary>Invokes a handler on the awaited path, isolating failures.</summary>
    private async Task InvokeAsync(
        EventSubscription subscription,
        ApplicationEvent applicationEvent,
        CancellationToken cancellationToken)
    {
        if (!subscription.IsActive)
        {
            return;
        }

        try
        {
            if (RequiresMarshalling(subscription))
            {
                // Two awaits, deliberately. The first marshals the *start* of the handler
                // onto the UI thread and yields its task; the second awaits that task.
                // Collapsing them into a blocking call on the dispatcher would deadlock
                // the moment a handler awaited anything.
                var handlerTask = await _dispatcher
                    .InvokeAsync(() => subscription.InvokeAsync(applicationEvent, cancellationToken))
                    .ConfigureAwait(false);

                await handlerTask.ConfigureAwait(false);
                return;
            }

            await subscription.InvokeAsync(applicationEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown, or a caller that gave up. Not a handler defect.
        }
        catch (Exception ex)
        {
            LogFailure(subscription, applicationEvent, ex);
        }
    }

    private void LogFailure(EventSubscription subscription, ApplicationEvent applicationEvent, Exception exception)
        => _logger.LogError(
            exception,
            "A handler for {Event} threw. The event was still delivered to the remaining subscribers. Correlation {CorrelationId}, subscription on {EventType}",
            applicationEvent.EventName,
            applicationEvent.CorrelationId ?? "(none)",
            subscription.EventType.Name);

    /// <summary>Drops entries whose subscription has lapsed or been disposed.</summary>
    private static void Prune(List<WeakReference<EventSubscription>> list)
        => list.RemoveAll(reference => !reference.TryGetTarget(out var subscription) || !subscription.IsActive);
}
