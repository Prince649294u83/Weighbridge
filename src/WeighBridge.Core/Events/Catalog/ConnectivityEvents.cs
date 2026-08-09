using WeighBridge.Domain.Enums;

namespace WeighBridge.Core.Events.Catalog;

/// <summary>
/// Base for the two subsystem-connectivity events, so a subscriber that treats
/// connection and disconnection alike can handle one type.
/// </summary>
/// <remarks>
/// Subscribe to this with <see cref="EventSubscriptionOptions.IncludingDerived"/> to
/// receive both <see cref="ConnectionLostEvent"/> and
/// <see cref="DatabaseConnectedEvent"/> and any future sibling.
/// </remarks>
public abstract class SubsystemConnectivityEvent : ApplicationEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="subsystemName">Subsystem the event concerns, e.g. <c>Database</c>.</param>
    /// <param name="state">State the subsystem moved into.</param>
    /// <param name="detail">Human-readable explanation.</param>
    /// <param name="source">Component that raised the event.</param>
    protected SubsystemConnectivityEvent(
        string subsystemName,
        ConnectionState state,
        string detail,
        string? source = null)
        : base(source)
    {
        SubsystemName = subsystemName;
        State = state;
        Detail = detail;
    }

    /// <summary>Subsystem the event concerns.</summary>
    public string SubsystemName { get; }

    /// <summary>State the subsystem moved into.</summary>
    public ConnectionState State { get; }

    /// <summary>Human-readable explanation, suitable for a notification body.</summary>
    public string Detail { get; }
}

/// <summary>
/// Announces that a subsystem became unreachable.
/// </summary>
/// <remarks>
/// Covers every subsystem, not just the database: indicator, printer, camera, server.
/// A single event type means a subscriber that wants to warn the operator about any lost
/// connection writes one handler rather than one per device.
/// </remarks>
public sealed class ConnectionLostEvent(string subsystemName, string detail, string? source = null)
    : SubsystemConnectivityEvent(subsystemName, ConnectionState.Disconnected, detail, source);

/// <summary>Announces that the database became reachable.</summary>
public sealed class DatabaseConnectedEvent(string detail, string? source = null)
    : SubsystemConnectivityEvent("Database", ConnectionState.Connected, detail, source);
