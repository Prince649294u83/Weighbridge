using WeighBridge.Core.Health;
using WeighBridge.Domain.Enums;

namespace WeighBridge.Core.Abstractions;

/// <summary>
/// Result of probing an external subsystem.
/// </summary>
/// <param name="State">The observed connection state.</param>
/// <param name="Detail">Human-readable explanation shown in the status tooltip.</param>
/// <param name="Latency">How long the probe took, when measured.</param>
public readonly record struct HealthResult(ConnectionState State, string Detail, TimeSpan? Latency = null)
{
    /// <summary>True when the subsystem is fully operational.</summary>
    public bool IsHealthy => State == ConnectionState.Connected;

    /// <summary>
    /// The same result as the health framework's coarser verdict.
    /// </summary>
    /// <remarks>
    /// A projection rather than a second field: one probe cannot be healthy under one enum
    /// and offline under the other, and a stored copy would eventually disagree with
    /// <see cref="State"/>.
    /// </remarks>
    public HealthStatus Status => State.ToHealthStatus();

    /// <summary>Creates a healthy result.</summary>
    public static HealthResult Healthy(string detail, TimeSpan? latency = null)
        => new(ConnectionState.Connected, detail, latency);

    /// <summary>Creates an unreachable result.</summary>
    public static HealthResult Unreachable(string detail)
        => new(ConnectionState.Disconnected, detail);

    /// <summary>Creates a result for a subsystem switched off in configuration.</summary>
    public static HealthResult Disabled(string detail = "Disabled in configuration")
        => new(ConnectionState.Disabled, detail);

    /// <summary>Creates a reachable-but-impaired result.</summary>
    public static HealthResult Degraded(string detail)
        => new(ConnectionState.Degraded, detail);

    /// <summary>
    /// Creates a result for a probe that threw.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Unreachable"/> only in its detail text. A check that throws
    /// is not distinguishable from an unreachable subsystem by anything an operator can
    /// act on, so it must not become its own state.
    /// </remarks>
    public static HealthResult Failed(Exception error)
        => new(ConnectionState.Disconnected, error.Message);
}

/// <summary>Implemented by any subsystem whose availability appears in the status bar.</summary>
public interface IHealthCheck
{
    /// <summary>Name shown in the status bar.</summary>
    string Name { get; }

    /// <summary>Probes the subsystem. Implementations must never throw.</summary>
    Task<HealthResult> CheckAsync(CancellationToken cancellationToken = default);
}
