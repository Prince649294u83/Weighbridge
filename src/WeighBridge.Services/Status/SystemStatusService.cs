using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Status;
using WeighBridge.Domain.Enums;

namespace WeighBridge.Services.Status;

/// <summary>
/// Aggregates subsystem health for the shell status bar and re-probes it on a timer.
/// </summary>
/// <remarks>
/// Every subsystem starts as <see cref="ConnectionState.Disconnected"/> and is only
/// promoted once a probe actually succeeds — the status bar never claims a connection
/// the application has not verified. Probes run concurrently and are individually
/// guarded, so one slow subsystem cannot stall the others.
/// </remarks>
public sealed class SystemStatusService : ISystemStatusService, IAsyncDisposable
{
    private readonly IHealthCheck _databaseCheck;
    private readonly IWeightIndicatorService _weightIndicator;
    private readonly IPrintService _printService;
    private readonly IServerConnectivityService _serverConnectivity;
    private readonly DatabaseOptions _databaseOptions;
    private readonly ILogger<SystemStatusService> _logger;

    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private bool _disposed;

    public SystemStatusService(
        IHealthCheck databaseCheck,
        IWeightIndicatorService weightIndicator,
        IPrintService printService,
        IServerConnectivityService serverConnectivity,
        IOptions<DatabaseOptions> databaseOptions,
        ILogger<SystemStatusService> logger)
    {
        _databaseCheck = databaseCheck;
        _weightIndicator = weightIndicator;
        _printService = printService;
        _serverConnectivity = serverConnectivity;
        _databaseOptions = databaseOptions.Value;
        _logger = logger;

        Application = new SubsystemStatus("Application", ConnectionState.Connected)
        {
            Detail = "Running",
        };

        Database = new SubsystemStatus("Database", ConnectionState.Disconnected);
        WeightIndicator = new SubsystemStatus("Weight Indicator", ConnectionState.Disconnected);
        Printer = new SubsystemStatus("Printer", ConnectionState.Disconnected);
        Server = new SubsystemStatus("Server", ConnectionState.Disconnected);

        All = [Application, Database, WeightIndicator, Printer, Server];
    }

    /// <inheritdoc />
    public SubsystemStatus Application { get; }

    /// <inheritdoc />
    public SubsystemStatus Database { get; }

    /// <inheritdoc />
    public SubsystemStatus WeightIndicator { get; }

    /// <inheritdoc />
    public SubsystemStatus Printer { get; }

    /// <inheritdoc />
    public SubsystemStatus Server { get; }

    /// <inheritdoc />
    public IReadOnlyList<SubsystemStatus> All { get; }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // Overlapping refreshes (timer tick plus a manual refresh) would double the
        // probe load for no benefit.
        if (!await _refreshGate.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await Task.WhenAll(
                ProbeAsync(Database, _databaseCheck, cancellationToken),
                ProbeAsync(WeightIndicator, _weightIndicator, cancellationToken),
                ProbeAsync(Printer, _printService, cancellationToken),
                ProbeAsync(Server, _serverConnectivity, cancellationToken)).ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <inheritdoc />
    public Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_monitorTask is not null)
        {
            return Task.CompletedTask;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, _databaseOptions.HealthCheckIntervalSeconds));

        _monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitorTask = MonitorLoopAsync(interval, _monitorCancellation.Token);

        _logger.LogInformation("Subsystem monitoring started with a {Interval} interval", interval);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopMonitoringAsync()
    {
        if (_monitorCancellation is null)
        {
            return;
        }

        await _monitorCancellation.CancelAsync().ConfigureAwait(false);

        if (_monitorTask is not null)
        {
            try
            {
                await _monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }

        _monitorCancellation.Dispose();
        _monitorCancellation = null;
        _monitorTask = null;

        _logger.LogInformation("Subsystem monitoring stopped");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await StopMonitoringAsync().ConfigureAwait(false);
        _refreshGate.Dispose();
    }

    private async Task MonitorLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }
    }

    /// <summary>
    /// Runs one health check and copies the outcome onto the observable status. A
    /// misbehaving check can never take the monitoring loop down.
    /// </summary>
    private async Task ProbeAsync(SubsystemStatus status, IHealthCheck check, CancellationToken cancellationToken)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await check.CheckAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var detail = result.Latency is { } latency
                ? $"{result.Detail} ({latency.TotalMilliseconds:F0} ms)"
                : result.Detail;

            var previousState = status.State;
            status.Update(result.State, detail);

            if (previousState != result.State)
            {
                _logger.LogInformation(
                    "{Subsystem} status changed from {Previous} to {Current}: {Detail}",
                    status.Name,
                    previousState,
                    result.State,
                    detail);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down — leave the last known state in place.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check for {Subsystem} threw", status.Name);
            status.Update(ConnectionState.Disconnected, ex.Message);
        }
    }
}
