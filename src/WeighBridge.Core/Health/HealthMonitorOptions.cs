namespace WeighBridge.Core.Health;

/// <summary>Tuning for the health monitor.</summary>
public sealed class HealthMonitorOptions
{
    /// <summary>Configuration section this class binds to.</summary>
    public const string SectionName = "HealthMonitoring";

    /// <summary>
    /// Seconds between automatic refreshes.
    /// </summary>
    /// <remarks>
    /// A compromise: often enough that an operator notices a dead indicator before the next
    /// vehicle arrives, rare enough that polling a serial port and a network share all shift
    /// costs nothing worth measuring.
    /// </remarks>
    public int IntervalSeconds { get; set; } = 30;

    /// <summary>The refresh interval as a timespan, floored so a bad value cannot busy-loop.</summary>
    public TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(5, IntervalSeconds));

    /// <summary>
    /// Whether the monitor probes on its own schedule.
    /// </summary>
    /// <remarks>
    /// Turning it off leaves the monitor working but manual — useful when diagnosing a
    /// device without a background probe touching the port at the same time.
    /// </remarks>
    public bool AutoRefresh { get; set; } = true;
}
