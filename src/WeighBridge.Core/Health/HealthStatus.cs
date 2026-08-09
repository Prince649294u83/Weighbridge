using WeighBridge.Domain.Enums;

namespace WeighBridge.Core.Health;

/// <summary>
/// How a monitored subsystem is behaving, as the health framework reports it.
/// </summary>
/// <remarks>
/// Deliberately coarser than <see cref="ConnectionState"/>. That enum describes a
/// connection and distinguishes states a monitor does not act on differently —
/// <see cref="ConnectionState.Connecting"/> is not yet an answer, and neither
/// <see cref="ConnectionState.Unknown"/> nor a transport failure tells an operator
/// anything a single "not answering" does not. This enum describes a verdict, so it also
/// covers subsystems that have no connection at all, such as free disk space.
/// </remarks>
public enum HealthStatus
{
    /// <summary>Not probed yet, or the probe has not produced a verdict.</summary>
    Unknown = 0,

    /// <summary>Working as expected.</summary>
    Healthy = 1,

    /// <summary>Working, but something needs attention before it becomes a failure.</summary>
    Warning = 2,

    /// <summary>Not usable.</summary>
    Offline = 3,

    /// <summary>Intentionally switched off, so its state is not a fault.</summary>
    Disabled = 4,
}

/// <summary>Conversions between <see cref="HealthStatus"/> and related enums.</summary>
public static class HealthStatusExtensions
{
    /// <summary>Maps a connection state onto the verdict the monitor reports.</summary>
    /// <remarks>
    /// <see cref="ConnectionState.Connecting"/> maps to <see cref="HealthStatus.Unknown"/>
    /// rather than to a fault: a probe caught mid-connect has not failed, and reporting it
    /// as offline would flap the status every time a subsystem reconnects.
    /// </remarks>
    public static HealthStatus ToHealthStatus(this ConnectionState state) => state switch
    {
        ConnectionState.Connected => HealthStatus.Healthy,
        ConnectionState.Degraded => HealthStatus.Warning,
        ConnectionState.Disconnected => HealthStatus.Offline,
        ConnectionState.Disabled => HealthStatus.Disabled,
        _ => HealthStatus.Unknown,
    };

    /// <summary>
    /// True when the status means an operator should be told something.
    /// </summary>
    /// <remarks>
    /// <see cref="HealthStatus.Disabled"/> is excluded: a subsystem switched off on purpose
    /// is not a problem, and alerting on it trains operators to ignore alerts.
    /// </remarks>
    public static bool IsAlerting(this HealthStatus status)
        => status is HealthStatus.Warning or HealthStatus.Offline;

    /// <summary>
    /// The worse of two statuses, for rolling several checks into one overall verdict.
    /// </summary>
    /// <remarks>
    /// Ranked by how much attention each demands, which is not the declaration order:
    /// <see cref="HealthStatus.Disabled"/> ranks lowest because a subsystem that is off on
    /// purpose must never drag the overall verdict below one that is merely unprobed.
    /// </remarks>
    public static HealthStatus Worst(this HealthStatus first, HealthStatus second)
        => Rank(first) >= Rank(second) ? first : second;

    private static int Rank(HealthStatus status) => status switch
    {
        HealthStatus.Disabled => 0,
        HealthStatus.Healthy => 1,
        HealthStatus.Unknown => 2,
        HealthStatus.Warning => 3,
        HealthStatus.Offline => 4,
        _ => 2,
    };
}
