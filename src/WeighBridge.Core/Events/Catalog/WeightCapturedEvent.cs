namespace WeighBridge.Core.Events.Catalog;

/// <summary>
/// Announces a weight reading accepted from the indicator.
/// </summary>
/// <remarks>
/// Published by the Hardware layer once the weight indicator module lands. Raised only
/// for a reading the operator or the software has accepted, not for every frame arriving
/// on the serial port — a live stream belongs on a polled property, not on the bus.
/// </remarks>
public sealed class WeightCapturedEvent : ApplicationEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="weight">The captured weight.</param>
    /// <param name="unit">Unit the weight is expressed in, e.g. <c>kg</c>.</param>
    /// <param name="isStable">True when the indicator reported the reading as stable.</param>
    /// <param name="source">Component that raised the event.</param>
    public WeightCapturedEvent(decimal weight, string unit, bool isStable, string? source = null)
        : base(source)
    {
        Weight = weight;
        Unit = unit;
        IsStable = isStable;
    }

    /// <summary>The captured weight.</summary>
    public decimal Weight { get; }

    /// <summary>Unit the weight is expressed in.</summary>
    public string Unit { get; }

    /// <summary>True when the indicator reported the reading as stable.</summary>
    public bool IsStable { get; }
}
