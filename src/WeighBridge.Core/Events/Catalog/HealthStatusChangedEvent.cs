using WeighBridge.Core.Health;

namespace WeighBridge.Core.Events.Catalog;

/// <summary>
/// Announces that a monitored subsystem changed status.
/// </summary>
/// <remarks>
/// Lets a subscriber react to an outage without holding a reference to the monitor — a
/// status bar recolouring an indicator, or a notification telling the operator the printer
/// stopped answering before they try to print a slip.
/// </remarks>
public sealed class HealthStatusChangedEvent(
    string checkName,
    HealthStatus previousStatus,
    HealthStatus currentStatus,
    string detail,
    string? source = null) : ApplicationEvent(source)
{
    /// <summary>Display name of the check that changed.</summary>
    public string CheckName { get; } = checkName;

    /// <summary>The status before this probe.</summary>
    public HealthStatus PreviousStatus { get; } = previousStatus;

    /// <summary>The status after it.</summary>
    public HealthStatus CurrentStatus { get; } = currentStatus;

    /// <summary>Explanation from the probe, for the event log.</summary>
    public string Detail { get; } = detail;

    /// <summary>True when the subsystem went from not-healthy to healthy.</summary>
    public bool IsRecovery
        => CurrentStatus == HealthStatus.Healthy && PreviousStatus != HealthStatus.Healthy;

    /// <inheritdoc />
    public override string ToString()
        => $"Health {CheckName}: {PreviousStatus} → {CurrentStatus}";
}
