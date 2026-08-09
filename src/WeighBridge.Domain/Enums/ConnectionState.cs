namespace WeighBridge.Domain.Enums;

/// <summary>
/// Connection state of an external subsystem (database, weight indicator, printer,
/// server). Drives the coloured indicators in the shell status bar.
/// </summary>
public enum ConnectionState
{
    /// <summary>No connection attempt has been made yet.</summary>
    Unknown = 0,

    /// <summary>A connection attempt is in progress.</summary>
    Connecting = 1,

    /// <summary>The subsystem is reachable and healthy.</summary>
    Connected = 2,

    /// <summary>The subsystem is reachable but degraded or reporting warnings.</summary>
    Degraded = 3,

    /// <summary>The subsystem is not reachable.</summary>
    Disconnected = 4,

    /// <summary>The subsystem is intentionally switched off in configuration.</summary>
    Disabled = 5,
}
