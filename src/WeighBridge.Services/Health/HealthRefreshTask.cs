using Microsoft.Extensions.Options;
using WeighBridge.Core.Health;
using WeighBridge.Core.Tasks;

namespace WeighBridge.Services.Health;

/// <summary>
/// Re-probes the registered health checks on a schedule.
/// </summary>
/// <remarks>
/// <para>
/// The monitor's automatic refresh, expressed as a background task rather than a timer
/// inside <see cref="HealthMonitorService"/>. That way it inherits everything Component 4
/// already provides: one shutdown path, the concurrency cap, restart-on-failure, and a
/// registration an operator can see, pause and resume like any other task.
/// </para>
/// <para>
/// The only exception to the "no actual tasks, only framework" rule for the task manager,
/// and it is infrastructure rather than a feature: without it the monitor would need its own
/// scheduler, which is the duplication the manager exists to prevent.
/// </para>
/// </remarks>
public sealed class HealthRefreshTask : RecurringTask
{
    private readonly IHealthMonitor _monitor;

    /// <summary>Creates the task using the interval from configuration.</summary>
    public HealthRefreshTask(IHealthMonitor monitor, IOptions<HealthMonitorOptions> options)
        : base(
            "health-refresh",
            "Re-probes subsystem health",
            (options ?? throw new ArgumentNullException(nameof(options))).Value.Interval,
            // Low: a health probe is diagnostic. It must never take a slot from work an
            // operator is waiting on at the weighbridge.
            BackgroundTaskPriority.Low)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        _monitor = monitor;
    }

    /// <inheritdoc />
    public override async Task ExecuteAsync(
        IProgress<BackgroundTaskProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        progress.Report(new BackgroundTaskProgress("Probing subsystems"));

        await _monitor.RefreshAsync(cancellationToken).ConfigureAwait(false);

        progress.Report(new BackgroundTaskProgress($"Overall: {_monitor.Overall}", 100));
    }
}
