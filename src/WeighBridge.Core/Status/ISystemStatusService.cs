namespace WeighBridge.Core.Status;

/// <summary>
/// Aggregates the health of every external subsystem and refreshes it periodically
/// in the background. The shell status bar binds directly to these instances.
/// </summary>
public interface ISystemStatusService
{
    /// <summary>The application itself — always connected once the shell is up.</summary>
    SubsystemStatus Application { get; }

    /// <summary>SQLite / database connectivity.</summary>
    SubsystemStatus Database { get; }

    /// <summary>Weight indicator connectivity.</summary>
    SubsystemStatus WeightIndicator { get; }

    /// <summary>Printer availability.</summary>
    SubsystemStatus Printer { get; }

    /// <summary>Central server reachability.</summary>
    SubsystemStatus Server { get; }

    /// <summary>Every subsystem, in the order they appear in the status bar.</summary>
    IReadOnlyList<SubsystemStatus> All { get; }

    /// <summary>Runs one probe of every subsystem immediately.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts periodic background probing.</summary>
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops periodic background probing.</summary>
    Task StopMonitoringAsync();
}
