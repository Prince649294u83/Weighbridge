namespace WeighBridge.Core.Abstractions;

/// <summary>
/// Reports whether the optional central server is reachable.
/// </summary>
/// <remarks>
/// Module 0.1 registers a placeholder that reports "Disabled" when no server is
/// configured, which is the default for a standalone weighbridge.
/// </remarks>
public interface IServerConnectivityService : IHealthCheck
{
    /// <summary>Base address the service probes, or an empty string when unconfigured.</summary>
    string EndpointDescription { get; }
}
