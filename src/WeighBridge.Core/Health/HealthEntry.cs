using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Mvvm;

namespace WeighBridge.Core.Health;

/// <summary>
/// One monitored check and its latest verdict, observable so a diagnostics panel binds
/// to it directly.
/// </summary>
/// <remarks>
/// Read-only from outside for the same reason as
/// <see cref="Tasks.TaskRegistration"/>: the monitor owns the transitions, and a consumer
/// that could set <see cref="Status"/> would let the panel disagree with the last probe.
/// <see cref="Record"/> is public rather than <c>internal</c> because the monitor lives in
/// another assembly.
/// </remarks>
public sealed class HealthEntry : ObservableObject
{
    private HealthStatus _status = HealthStatus.Unknown;
    private string _detail = string.Empty;
    private TimeSpan? _latency;
    private DateTimeOffset? _lastChecked;
    private DateTimeOffset? _statusSince;
    private int _consecutiveFailures;

    /// <summary>Creates an entry for a check that has not been probed yet.</summary>
    public HealthEntry(IHealthCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);

        Check = check;
    }

    /// <summary>The check this entry reports on.</summary>
    public IHealthCheck Check { get; }

    /// <summary>Display name, taken from the check.</summary>
    public string Name => Check.Name;

    /// <summary>The most recent verdict.</summary>
    public HealthStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(IsAlerting));
            }
        }
    }

    /// <summary>Explanation from the most recent probe, shown as a tooltip.</summary>
    public string Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    /// <summary>How long the most recent probe took, when it was measured.</summary>
    public TimeSpan? Latency
    {
        get => _latency;
        private set => SetProperty(ref _latency, value);
    }

    /// <summary>When the check was last probed.</summary>
    public DateTimeOffset? LastChecked
    {
        get => _lastChecked;
        private set => SetProperty(ref _lastChecked, value);
    }

    /// <summary>
    /// When the current status was first observed.
    /// </summary>
    /// <remarks>
    /// Not the same as <see cref="LastChecked"/>, and the more useful of the two on a
    /// support call: "offline for two hours" points at a different cause than "offline for
    /// ten seconds".
    /// </remarks>
    public DateTimeOffset? StatusSince
    {
        get => _statusSince;
        private set => SetProperty(ref _statusSince, value);
    }

    /// <summary>
    /// Consecutive non-healthy probes.
    /// </summary>
    /// <remarks>
    /// Lets a consumer wait out a blip instead of alerting on the first one. A serial device
    /// missing a single poll is normal; missing three in a row is not.
    /// </remarks>
    public int ConsecutiveFailures
    {
        get => _consecutiveFailures;
        private set => SetProperty(ref _consecutiveFailures, value);
    }

    /// <summary>True when this check currently warrants an operator's attention.</summary>
    public bool IsAlerting => Status.IsAlerting();

    /// <summary>
    /// Applies a probe result.
    /// </summary>
    /// <returns>
    /// The status before this result, so the monitor can tell a change from a repeat without
    /// reading the property back.
    /// </returns>
    public HealthStatus Record(HealthResult result, DateTimeOffset checkedAt)
    {
        var previous = Status;
        var status = result.Status;

        Detail = result.Detail;
        Latency = result.Latency;
        LastChecked = checkedAt;

        // Disabled is not a failure, so it neither adds to the count nor clears it: a
        // subsystem switched off mid-outage should not look recovered when it comes back.
        if (status == HealthStatus.Healthy)
        {
            ConsecutiveFailures = 0;
        }
        else if (status.IsAlerting())
        {
            ConsecutiveFailures++;
        }

        if (previous != status)
        {
            Status = status;
            StatusSince = checkedAt;
        }

        return previous;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({Status})";
}
