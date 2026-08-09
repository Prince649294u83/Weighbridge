using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Health;
using WeighBridge.Domain.Enums;
using Xunit;

namespace WeighBridge.Tests.Health;

/// <summary>
/// Covers the guarantees <see cref="HealthCheck"/> makes on behalf of its subclasses:
/// never throw, always time out, always report a latency.
/// </summary>
public sealed class HealthCheckTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ASuccessfulProbe_IsReturnedWithAMeasuredLatency()
    {
        var check = new StubGuardedCheck();

        var result = await check.CheckAsync();

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.NotNull(result.Latency);
    }

    [Fact]
    public async Task AProbeThatMeasuredItsOwnLatency_KeepsIt()
    {
        var measured = TimeSpan.FromMilliseconds(42);
        var check = new StubGuardedCheck(
            probe: _ => Task.FromResult(HealthResult.Healthy("fine", measured)));

        var result = await check.CheckAsync();

        // The probe timed the round trip alone; the base class timed the call around it, so
        // the probe's own figure is the more useful one and must survive.
        Assert.Equal(measured, result.Latency);
    }

    [Fact]
    public async Task AThrowingProbe_BecomesAnOfflineResult()
    {
        var check = new StubGuardedCheck(
            probe: _ => throw new InvalidOperationException("port not open"));

        var result = await check.CheckAsync();

        Assert.Equal(HealthStatus.Offline, result.Status);
        Assert.Equal("port not open", result.Detail);
        Assert.NotNull(result.Latency);
    }

    [Fact]
    public async Task AHangingProbe_TimesOutAsOffline()
    {
        var check = new StubGuardedCheck(
            probe: async token =>
            {
                await Task.Delay(Timeout, token);
                return HealthResult.Healthy("never reached");
            },
            timeout: TimeSpan.FromMilliseconds(20));

        var result = await check.CheckAsync().WaitAsync(Timeout);

        // A hung probe is indistinguishable from a dead subsystem, and must not stall the
        // pass behind it.
        Assert.Equal(HealthStatus.Offline, result.Status);
        Assert.Contains("No response", result.Detail);
    }

    [Fact]
    public async Task TheCallersCancellation_IsNotSwallowedAsAnOutage()
    {
        using var cancellation = new CancellationTokenSource();

        var check = new StubGuardedCheck(probe: async token =>
        {
            await cancellation.CancelAsync();
            token.ThrowIfCancellationRequested();
            return HealthResult.Healthy("unreachable");
        });

        // Shutdown, not a fault. Turning it into an offline reading would leave a false
        // outage as the last thing in the log every time the application closes.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => check.CheckAsync(cancellation.Token));
    }

    [Fact]
    public void AnEmptyName_IsRejected()
        => Assert.Throws<ArgumentException>(() => new StubGuardedCheck(" "));

    [Theory]
    [InlineData(ConnectionState.Connected, HealthStatus.Healthy)]
    [InlineData(ConnectionState.Degraded, HealthStatus.Warning)]
    [InlineData(ConnectionState.Disconnected, HealthStatus.Offline)]
    [InlineData(ConnectionState.Disabled, HealthStatus.Disabled)]
    [InlineData(ConnectionState.Connecting, HealthStatus.Unknown)]
    [InlineData(ConnectionState.Unknown, HealthStatus.Unknown)]
    public void EveryConnectionState_MapsOntoAVerdict(ConnectionState state, HealthStatus expected)
        => Assert.Equal(expected, state.ToHealthStatus());

    [Theory]
    [InlineData(HealthStatus.Healthy, HealthStatus.Warning, HealthStatus.Warning)]
    [InlineData(HealthStatus.Warning, HealthStatus.Offline, HealthStatus.Offline)]
    [InlineData(HealthStatus.Unknown, HealthStatus.Healthy, HealthStatus.Unknown)]
    [InlineData(HealthStatus.Disabled, HealthStatus.Healthy, HealthStatus.Healthy)]
    [InlineData(HealthStatus.Disabled, HealthStatus.Unknown, HealthStatus.Unknown)]
    [InlineData(HealthStatus.Offline, HealthStatus.Offline, HealthStatus.Offline)]
    public void Worst_RanksByHowMuchAttentionEachStatusDemands(
        HealthStatus first, HealthStatus second, HealthStatus expected)
    {
        // Not declaration order: Disabled has to rank below everything, including Unknown.
        Assert.Equal(expected, first.Worst(second));
        Assert.Equal(expected, second.Worst(first));
    }

    [Theory]
    [InlineData(HealthStatus.Warning, true)]
    [InlineData(HealthStatus.Offline, true)]
    [InlineData(HealthStatus.Healthy, false)]
    [InlineData(HealthStatus.Unknown, false)]
    [InlineData(HealthStatus.Disabled, false)]
    public void IsAlerting_ExcludesADeliberatelyDisabledSubsystem(HealthStatus status, bool expected)
        => Assert.Equal(expected, status.IsAlerting());
}
