using System.Collections.ObjectModel;
using WeighBridge.Core.Abstractions;

namespace WeighBridge.Core.Health;

/// <summary>
/// Watches the registered health checks and reports what each one last said.
/// </summary>
/// <remarks>
/// <para>
/// The single place a subsystem's availability is decided. A module that probes its own
/// device on its own timer produces an answer nobody else can see, and two such modules
/// probing the same device disagree.
/// </para>
/// <para>
/// Distinct from <see cref="Status.ISystemStatusService"/>, which owns the fixed set of
/// indicators in the shell status bar. This one takes checks registered at runtime and
/// makes no claim about how — or whether — they are displayed.
/// </para>
/// </remarks>
public interface IHealthMonitor
{
    /// <summary>Every registered check and its latest verdict.</summary>
    ReadOnlyObservableCollection<HealthEntry> Entries { get; }

    /// <summary>
    /// The worst status across all entries.
    /// </summary>
    /// <remarks>
    /// Worst rather than an average: one offline subsystem is an outage regardless of how
    /// many healthy ones surround it.
    /// </remarks>
    HealthStatus Overall { get; }

    /// <summary>Raised after a check's status changes. Never for an unchanged repeat.</summary>
    event EventHandler<HealthChangedEventArgs>? HealthChanged;

    /// <summary>Adds a check to the monitored set.</summary>
    /// <exception cref="InvalidOperationException">A check with that name is registered.</exception>
    HealthEntry Register(IHealthCheck check);

    /// <summary>Removes a check.</summary>
    /// <returns>False when no check by that name is registered.</returns>
    bool Unregister(string name);

    /// <summary>Finds a check's entry, or null when it is not registered.</summary>
    HealthEntry? Find(string name);

    /// <summary>Probes every registered check once.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Probes one check.</summary>
    /// <returns>False when no check by that name is registered.</returns>
    Task<bool> RefreshAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>Describes a check whose status changed.</summary>
public sealed class HealthChangedEventArgs(
    HealthEntry entry,
    HealthStatus previousStatus) : EventArgs
{
    /// <summary>The entry that changed.</summary>
    public HealthEntry Entry { get; } = entry;

    /// <summary>The status before this probe.</summary>
    public HealthStatus PreviousStatus { get; } = previousStatus;

    /// <summary>The status after it.</summary>
    public HealthStatus CurrentStatus { get; } = entry.Status;

    /// <summary>True when the subsystem went from not-healthy to healthy.</summary>
    public bool IsRecovery
        => CurrentStatus == HealthStatus.Healthy && PreviousStatus != HealthStatus.Healthy;
}
