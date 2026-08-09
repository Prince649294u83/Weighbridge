using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using WeighBridge.Core.Events;
using WeighBridge.Services.Events;
using WeighBridge.Tests.Infrastructure;

namespace WeighBridge.Tests.Events;

/// <summary>
/// Covers the publish/subscribe contract, the weak-reference lifetime rule and the
/// failure isolation guarantee of <see cref="EventBus"/>.
/// </summary>
public sealed class EventBusTests
{
    private static EventBus CreateBus(TestUiDispatcher? dispatcher = null)
        => new(dispatcher ?? new TestUiDispatcher(), NullLogger<EventBus>.Instance);

    [Fact]
    public void Publish_InvokesSubscribedHandler()
    {
        var bus = CreateBus();
        TestEvent? received = null;

        using var subscription = bus.Subscribe<TestEvent>(e => received = e);

        bus.Publish(new TestEvent("weighment"));

        Assert.NotNull(received);
        Assert.Equal("weighment", received.Payload);
    }

    [Fact]
    public void Publish_WithNoSubscribers_DoesNotThrow()
    {
        var bus = CreateBus();

        // A module publishing into a system where nothing has subscribed yet is the
        // normal case, not an error.
        bus.Publish(new TestEvent());

        Assert.Equal(0, bus.SubscriptionCount);
    }

    [Fact]
    public void Publish_DoesNotDeliverToHandlersOfOtherEventTypes()
    {
        var bus = CreateBus();
        var otherCalls = 0;

        using var subscription = bus.Subscribe<OtherTestEvent>(_ => otherCalls++);

        bus.Publish(new TestEvent());

        Assert.Equal(0, otherCalls);
    }

    [Fact]
    public void Publish_WithExactTypeSubscription_DoesNotReceiveDerivedEvent()
    {
        var bus = CreateBus();
        var calls = 0;

        // Default options mean "this type only" — a base-type subscriber must opt in
        // before it starts seeing everything derived from it.
        using var subscription = bus.Subscribe<TestEvent>(_ => calls++);

        bus.Publish(new DerivedTestEvent());

        Assert.Equal(0, calls);
    }

    [Fact]
    public void Publish_WithDerivedOptIn_ReceivesDerivedEvent()
    {
        var bus = CreateBus();
        TestEvent? received = null;

        using var subscription = bus.Subscribe<TestEvent>(
            e => received = e,
            EventSubscriptionOptions.IncludingDerived);

        bus.Publish(new DerivedTestEvent("derived"));

        Assert.IsType<DerivedTestEvent>(received);
        Assert.Equal("derived", received.Payload);
    }

    [Fact]
    public void Publish_WithApplicationEventOptIn_ObservesEveryEvent()
    {
        var bus = CreateBus();
        List<string> observed = [];

        // This is how an audit log or a diagnostics window watches the whole
        // application without knowing a single concrete event type.
        using var subscription = bus.Subscribe<ApplicationEvent>(
            e => observed.Add(e.EventName),
            EventSubscriptionOptions.IncludingDerived);

        bus.Publish(new TestEvent());
        bus.Publish(new DerivedTestEvent());
        bus.Publish(new OtherTestEvent());

        Assert.Equal([nameof(TestEvent), nameof(DerivedTestEvent), nameof(OtherTestEvent)], observed);
    }

    [Fact]
    public void Publish_RespectsPriorityOrder()
    {
        var bus = CreateBus();
        List<string> order = [];

        using var low = bus.Subscribe<TestEvent>(_ => order.Add("low"), new EventSubscriptionOptions(Priority: -10));
        using var high = bus.Subscribe<TestEvent>(_ => order.Add("high"), new EventSubscriptionOptions(Priority: 10));
        using var middle = bus.Subscribe<TestEvent>(_ => order.Add("middle"));

        bus.Publish(new TestEvent());

        Assert.Equal(["high", "middle", "low"], order);
    }

    [Fact]
    public void Publish_WithEqualPriority_PreservesSubscriptionOrder()
    {
        var bus = CreateBus();
        List<string> order = [];

        using var first = bus.Subscribe<TestEvent>(_ => order.Add("first"));
        using var second = bus.Subscribe<TestEvent>(_ => order.Add("second"));

        bus.Publish(new TestEvent());

        Assert.Equal(["first", "second"], order);
    }

    [Fact]
    public void Publish_IsolatesAFailingHandlerFromTheRest()
    {
        var bus = CreateBus();
        var survivorCalled = false;

        // A subscriber must not be able to break the operation that announced the event.
        using var thrower = bus.Subscribe<TestEvent>(
            _ => throw new InvalidOperationException("handler defect"),
            new EventSubscriptionOptions(Priority: 10));

        using var survivor = bus.Subscribe<TestEvent>(_ => survivorCalled = true);

        bus.Publish(new TestEvent());

        Assert.True(survivorCalled);
    }

    [Fact]
    public async Task PublishAsync_AwaitsEveryHandler()
    {
        var bus = CreateBus();
        var completed = false;

        using var subscription = bus.Subscribe<TestEvent>(async (_, token) =>
        {
            await Task.Delay(20, token);
            completed = true;
        });

        await bus.PublishAsync(new TestEvent());

        Assert.True(completed);
    }

    [Fact]
    public async Task PublishAsync_IsolatesAFailingHandler()
    {
        var bus = CreateBus();
        var survivorCalled = false;

        using var thrower = bus.Subscribe<TestEvent>(
            (_, _) => throw new InvalidOperationException("handler defect"),
            new EventSubscriptionOptions(Priority: 10));

        using var survivor = bus.Subscribe<TestEvent>((_, _) =>
        {
            survivorCalled = true;
            return Task.CompletedTask;
        });

        await bus.PublishAsync(new TestEvent());

        Assert.True(survivorCalled);
    }

    [Fact]
    public async Task PublishAsync_RunsHandlersSequentiallyInPriorityOrder()
    {
        var bus = CreateBus();
        List<string> order = [];

        // The sequential guarantee is what lets a lower-priority handler rely on state a
        // higher-priority one produced; concurrency would make priority meaningless.
        using var high = bus.Subscribe<TestEvent>(
            async (_, token) =>
            {
                await Task.Delay(30, token);
                order.Add("high");
            },
            new EventSubscriptionOptions(Priority: 10));

        using var low = bus.Subscribe<TestEvent>((_, _) =>
        {
            order.Add("low");
            return Task.CompletedTask;
        });

        await bus.PublishAsync(new TestEvent());

        Assert.Equal(["high", "low"], order);
    }

    [Fact]
    public void Dispose_OfSubscriptionStopsDelivery()
    {
        var bus = CreateBus();
        var calls = 0;

        var subscription = bus.Subscribe<TestEvent>(_ => calls++);
        bus.Publish(new TestEvent());

        subscription.Dispose();
        bus.Publish(new TestEvent());

        Assert.Equal(1, calls);
        Assert.False(subscription.IsActive);
        Assert.Equal(0, bus.SubscriptionCount);
    }

    [Fact]
    public void Unsubscribe_WithMethodGroupRemovesTheSubscription()
    {
        var bus = CreateBus();
        var received = 0;

        void Handler(TestEvent _) => received++;

        using var subscription = bus.Subscribe<TestEvent>(Handler);

        Assert.True(bus.Unsubscribe<TestEvent>(Handler));

        bus.Publish(new TestEvent());

        Assert.Equal(0, received);
        Assert.False(bus.Unsubscribe<TestEvent>(Handler));
    }

    [Fact]
    public void UnsubscribeAll_RemovesEverySubscriptionOfOneOwner()
    {
        var bus = CreateBus();
        var owner = new object();
        var otherOwner = new object();
        var ownerCalls = 0;
        var otherCalls = 0;

        bus.Subscribe<TestEvent>(owner, _ => ownerCalls++);
        bus.Subscribe<OtherTestEvent>(owner, _ => ownerCalls++);
        using var untouched = bus.Subscribe<TestEvent>(otherOwner, _ => otherCalls++);

        Assert.Equal(2, bus.UnsubscribeAll(owner));

        bus.Publish(new TestEvent());
        bus.Publish(new OtherTestEvent());

        Assert.Equal(0, ownerCalls);
        Assert.Equal(1, otherCalls);
    }

    [Fact]
    public void Subscription_LapsesWhenItsHandleIsCollected()
    {
        // The load-bearing lifetime rule: the bus holds the subscription weakly, so a
        // transient ViewModel that is navigated away from stops receiving events without
        // anyone having remembered to unsubscribe. A bus that held strong references
        // would accumulate every ViewModel opened during a months-long session.
        var bus = CreateBus();
        var calls = 0;

        SubscribeAndDiscardHandle(bus, () => calls++);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        bus.Publish(new TestEvent());

        Assert.Equal(0, calls);
        Assert.Equal(0, bus.SubscriptionCount);
    }

    [Fact]
    public void Subscription_SurvivesCollectionWhileTheHandleIsHeld()
    {
        var bus = CreateBus();
        var calls = 0;

        using var subscription = bus.Subscribe<TestEvent>(_ => calls++);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        bus.Publish(new TestEvent());

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Subscription_LapsesWhenItsOwnerIsCollected()
    {
        var bus = CreateBus();
        var calls = 0;

        SubscribeWithTemporaryOwner(bus, () => calls++);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        bus.Publish(new TestEvent());

        Assert.Equal(0, calls);
    }

    [Fact]
    public void Publish_MarshalsToTheUiThreadWhenRequested()
    {
        var dispatcher = new TestUiDispatcher(isOnUiThread: false);
        var bus = CreateBus(dispatcher);
        var received = false;

        using var subscription = bus.Subscribe<TestEvent>(
            _ => received = true,
            EventSubscriptionOptions.OnUiThread);

        bus.Publish(new TestEvent());

        Assert.True(received);
        Assert.Equal(1, dispatcher.MarshalledCount);
    }

    [Fact]
    public void Publish_FromTheUiThreadDoesNotRoundTripThroughTheDispatcher()
    {
        var dispatcher = new TestUiDispatcher(isOnUiThread: true);
        var bus = CreateBus(dispatcher);

        using var subscription = bus.Subscribe<TestEvent>(
            _ => { },
            EventSubscriptionOptions.OnUiThread);

        bus.Publish(new TestEvent());

        // Queueing when already on the UI thread would reorder this handler behind
        // whatever else is pending, breaking the priority contract.
        Assert.Equal(0, dispatcher.MarshalledCount);
    }

    [Fact]
    public async Task PublishAsync_MarshalsAndStillAwaitsTheHandler()
    {
        var dispatcher = new TestUiDispatcher(isOnUiThread: false);
        var bus = CreateBus(dispatcher);
        var completed = false;

        using var subscription = bus.Subscribe<TestEvent>(
            async (_, token) =>
            {
                await Task.Delay(20, token);
                completed = true;
            },
            EventSubscriptionOptions.OnUiThread);

        await bus.PublishAsync(new TestEvent());

        // Proves the double-await: marshalling only starts the handler, so a single
        // await would return before the delay finished.
        Assert.True(completed);
        Assert.Equal(1, dispatcher.MarshalledCount);
    }

    [Fact]
    public void Handler_MaySubscribeWhileItRuns()
    {
        // Dispatch works against a snapshot taken outside the lock, so a handler that
        // mutates the subscription table cannot deadlock or invalidate the iteration.
        var bus = CreateBus();
        IEventSubscription? nested = null;
        var nestedCalls = 0;

        using var outer = bus.Subscribe<TestEvent>(_ =>
        {
            nested ??= bus.Subscribe<TestEvent>(_ => nestedCalls++);
        });

        bus.Publish(new TestEvent());
        Assert.Equal(0, nestedCalls);

        bus.Publish(new TestEvent());
        Assert.Equal(1, nestedCalls);

        nested?.Dispose();
    }

    [Fact]
    public void Handler_MayPublishWhileItRuns()
    {
        var bus = CreateBus();
        var otherReceived = false;

        using var chain = bus.Subscribe<TestEvent>(_ => bus.Publish(new OtherTestEvent()));
        using var listener = bus.Subscribe<OtherTestEvent>(_ => otherReceived = true);

        bus.Publish(new TestEvent());

        Assert.True(otherReceived);
    }

    [Fact]
    public void Event_CarriesTheAmbientCorrelationId()
    {
        using var scope = Core.Diagnostics.CorrelationScope.Begin("abc123");

        var applicationEvent = new TestEvent();

        Assert.Equal("abc123", applicationEvent.CorrelationId);
        Assert.Equal(nameof(TestEvent), applicationEvent.Source);
        Assert.NotEqual(Guid.Empty, applicationEvent.EventId);
    }

    [Fact]
    public void Reset_RemovesEverySubscription()
    {
        var bus = CreateBus();
        var calls = 0;

        using var first = bus.Subscribe<TestEvent>(_ => calls++);
        using var second = bus.Subscribe<OtherTestEvent>(_ => calls++);

        Assert.Equal(2, bus.SubscriptionCount);

        bus.Reset();

        bus.Publish(new TestEvent());
        bus.Publish(new OtherTestEvent());

        Assert.Equal(0, calls);
        Assert.Equal(0, bus.SubscriptionCount);
    }

    /// <summary>
    /// Subscribes without returning the handle, so nothing keeps the subscription alive.
    /// A separate non-inlined method guarantees the local goes out of scope.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SubscribeAndDiscardHandle(EventBus bus, Action onEvent)
        => bus.Subscribe<TestEvent>(_ => onEvent());

    /// <summary>Subscribes with an owner that becomes unreachable on return.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SubscribeWithTemporaryOwner(EventBus bus, Action onEvent)
    {
        var owner = new object();
        var subscription = bus.Subscribe<TestEvent>(owner, _ => onEvent());

        // Keeping the handle alive is not enough: the owner going away is what lapses
        // this subscription. GC.KeepAlive proves the handle is not the reason.
        GC.KeepAlive(subscription);
    }
}
