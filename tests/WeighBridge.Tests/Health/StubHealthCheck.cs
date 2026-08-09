using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Health;
using WeighBridge.Domain.Enums;

namespace WeighBridge.Tests.Health;

/// <summary>
/// A health check whose verdict each test dictates.
/// </summary>
/// <remarks>
/// Implements <see cref="IHealthCheck"/> directly rather than deriving from
/// <see cref="HealthCheck"/>, so a test can make it throw and prove the monitor's own guard
/// works. <see cref="StubGuardedCheck"/> covers the base class.
/// </remarks>
internal sealed class StubHealthCheck(
    string name = "stub-check",
    ConnectionState state = ConnectionState.Connected) : IHealthCheck
{
    public string Name { get; } = name;

    /// <summary>The state the next probe reports. Settable so one check can change verdict.</summary>
    public ConnectionState State { get; set; } = state;

    /// <summary>When set, the probe throws this instead of returning.</summary>
    public Exception? Throws { get; set; }

    /// <summary>Held by the probe until a test completes it, when set.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>How many times the probe has been entered.</summary>
    public int Probes;

    public async Task<HealthResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Probes);

        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (Throws is not null)
        {
            throw Throws;
        }

        return new HealthResult(State, $"{Name} is {State}");
    }
}

/// <summary>
/// A check built on <see cref="HealthCheck"/>, for the tests that exercise the base
/// class's exception and timeout handling.
/// </summary>
internal sealed class StubGuardedCheck(
    string name = "guarded",
    Func<CancellationToken, Task<HealthResult>>? probe = null,
    TimeSpan? timeout = null) : HealthCheck(name)
{
    private readonly Func<CancellationToken, Task<HealthResult>>? _probe = probe;
    private readonly TimeSpan? _timeout = timeout;

    protected override TimeSpan Timeout => _timeout ?? base.Timeout;

    protected override Task<HealthResult> ProbeAsync(CancellationToken cancellationToken)
        => _probe is null
            ? Task.FromResult(HealthResult.Healthy("fine"))
            : _probe(cancellationToken);
}
