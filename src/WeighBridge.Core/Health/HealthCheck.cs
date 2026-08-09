using WeighBridge.Core.Abstractions;

namespace WeighBridge.Core.Health;

/// <summary>
/// Base class for a health check, which turns the "never throw" rule from
/// <see cref="IHealthCheck"/> into something the implementation cannot get wrong.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IHealthCheck.CheckAsync"/> documents that implementations must never throw,
/// but a probe talks to a serial port, a printer queue or a network share — the places
/// exceptions actually come from. Leaving each check to remember its own try/catch means
/// the monitor's reliability depends on the least careful one.
/// </para>
/// <para>
/// Derive and implement <see cref="ProbeAsync"/> as though exceptions were allowed. This
/// class catches them, stamps the latency and returns a result either way. Cancellation is
/// the one exception let through: it means the monitor is shutting down, which is not the
/// subsystem's fault and must not be recorded as one.
/// </para>
/// </remarks>
public abstract class HealthCheck : IHealthCheck
{
    /// <summary>Creates a check with the name the status bar shows.</summary>
    protected HealthCheck(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>
    /// How long a probe may run before it counts as offline.
    /// </summary>
    /// <remarks>
    /// A hung probe is indistinguishable from a dead subsystem from the operator's side,
    /// and a check with no timeout would stall every later refresh behind it. Override to
    /// widen it for a subsystem that is legitimately slow.
    /// </remarks>
    protected virtual TimeSpan Timeout => TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public async Task<HealthResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        // Linked rather than replaced, so the monitor's own cancellation still wins.
        using var timeout = new CancellationTokenSource(Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeout.Token);

        try
        {
            var result = await ProbeAsync(linked.Token).ConfigureAwait(false);

            // A probe that reported no latency gets the measured one; one that measured its
            // own — the round trip alone, without the setup around it — keeps it.
            return result.Latency is null
                ? result with { Latency = System.Diagnostics.Stopwatch.GetElapsedTime(startedAt) }
                : result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new HealthResult(
                Domain.Enums.ConnectionState.Disconnected,
                $"No response within {Timeout.TotalSeconds:0.#}s",
                System.Diagnostics.Stopwatch.GetElapsedTime(startedAt));
        }
        catch (Exception ex)
        {
            return HealthResult.Failed(ex) with
            {
                Latency = System.Diagnostics.Stopwatch.GetElapsedTime(startedAt),
            };
        }
    }

    /// <summary>
    /// Probes the subsystem. May throw and may block; the base class handles both.
    /// </summary>
    protected abstract Task<HealthResult> ProbeAsync(CancellationToken cancellationToken);

    /// <inheritdoc />
    public override string ToString() => Name;
}
