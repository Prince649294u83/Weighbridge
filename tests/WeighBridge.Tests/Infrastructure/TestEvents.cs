using WeighBridge.Core.Events;

namespace WeighBridge.Tests.Infrastructure;

/// <summary>Base test event, used to verify base-type subscription.</summary>
internal class TestEvent(string payload = "") : ApplicationEvent
{
    public string Payload { get; } = payload;
}

/// <summary>Derived test event, used to verify hierarchy walking.</summary>
internal sealed class DerivedTestEvent(string payload = "") : TestEvent(payload);

/// <summary>Unrelated event, used to verify that handlers are not cross-delivered.</summary>
internal sealed class OtherTestEvent : ApplicationEvent;
