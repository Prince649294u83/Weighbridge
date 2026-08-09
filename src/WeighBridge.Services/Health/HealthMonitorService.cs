using System.Collections.ObjectModel;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Events;
using WeighBridge.Core.Events.Catalog;
using WeighBridge.Core.Health;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Threading;

namespace WeighBridge.Services.Health;

/// <summary>
/// The application's health monitor: holds the registered checks, probes them and reports
/// what each one last said.
/// </summary>
/// <remarks>
/// <para>
/// Has no timer. Automatic refreshing is a <see cref="HealthRefreshTask"/> registered with
/// the background task manager, so there is one shutdown path, one concurrency cap and one
/// place that logs a failing loop — rather than a second scheduler living in here that
/// nothing else can see or stop.
/// </para>
/// <para>
/// Probes run concurrently and are individually guarded: one subsystem that stops answering
/// must not delay the verdict on the others. <see cref="HealthCheck"/> already contains a
/// throwing probe, and the guard here covers checks that implement
/// <see cref="IHealthCheck"/> directly.
/// </para>
/// </remarks>
public sealed class HealthMonitorService : IHealthMonitor, IDisposable
{
    private readonly ObservableCollection<HealthEntry> _entries = [];
    private readonly Dictionary<string, HealthEntry> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    // One probe pass at a time. A refresh triggered by the operator while the timed one is
    // still running would otherwise poll the same serial port twice at once.
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private readonly IEventPublisher _eventPublisher;
    private readonly IUiDispatcher _dispatcher;
    private readonly IApplicationLogger _logger;

    private bool _disposed;

    /// <summary>Creates the monitor with no checks registered.</summary>
    public HealthMonitorService(
        IEventPublisher eventPublisher,
        IUiDispatcher dispatcher,
        IApplicationLogger logger)
    {
        ArgumentNullException.ThrowIfNull(eventPublisher);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(logger);

        _eventPublisher = eventPublisher;
        _dispatcher = dispatcher;
        _logger = logger;

        Entries = new ReadOnlyObservableCollection<HealthEntry>(_entries);
    }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<HealthEntry> Entries { get; }

    /// <inheritdoc />
    public HealthStatus Overall
    {
        get
        {
            HealthEntry[] snapshot;

            lock (_gate)
            {
                snapshot = [.. _entries];
            }

            // No checks is not a claim of health: nothing has been verified.
            if (snapshot.Length == 0)
            {
                return HealthStatus.Unknown;
            }

            var overall = HealthStatus.Disabled;

            foreach (var entry in snapshot)
            {
                overall = overall.Worst(entry.Status);
            }

            return overall;
        }
    }

    /// <inheritdoc />
    public event EventHandler<HealthChangedEventArgs>? HealthChanged;

    /// <inheritdoc />
    public HealthEntry Register(IHealthCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var entry = new HealthEntry(check);

        lock (_gate)
        {
            // The name is the handle every other method takes, so a duplicate has to fail
            // loudly rather than shadow the first check.
            if (!_byName.TryAdd(check.Name, entry))
            {
                throw new InvalidOperationException(
                    $"A health check named '{check.Name}' is already registered.");
            }
        }

        OnUiThread(() => _entries.Add(entry));
        _logger.Debug("Health check registered: {Check}", check.Name);

        return entry;
    }

    /// <inheritdoc />
    public bool Unregister(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        HealthEntry entry;

        lock (_gate)
        {
            if (!_byName.Remove(name, out var removed))
            {
                return false;
            }

            entry = removed;
        }

        OnUiThread(() => _entries.Remove(entry));
        _logger.Debug("Health check unregistered: {Check}", name);

        return true;
    }

    /// <inheritdoc />
    public HealthEntry? Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        lock (_gate)
        {
            return _byName.GetValueOrDefault(name);
        }
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        HealthEntry[] snapshot;

        lock (_gate)
        {
            snapshot = [.. _entries];
        }

        if (snapshot.Length == 0)
        {
            return;
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await Task.WhenAll(snapshot.Select(entry => ProbeAsync(entry, cancellationToken)))
                .ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }

        // Each probe swallows its own cancellation so shutdown leaves no false outage
        // behind, which would otherwise let this method return as though the pass had
        // succeeded. A cancelled pass has to surface as cancelled: the caller is
        // HealthRefreshTask, and the task manager tells a cancelled run from a completed
        // one by exactly this exception.
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <inheritdoc />
    public async Task<bool> RefreshAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Find(name) is not { } entry)
        {
            return false;
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await ProbeAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }

        cancellationToken.ThrowIfCancellationRequested();

        return true;
    }

    /// <summary>Probes one check and records the result, whatever it turns out to be.</summary>
    private async Task ProbeAsync(HealthEntry entry, CancellationToken cancellationToken)
    {
        HealthResult result;

        try
        {
            result = await entry.Check.CheckAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down. Recording an outage here would leave a false one in the log.
            return;
        }
        catch (Exception ex)
        {
            // IHealthCheck says implementations must never throw. One did, so treat it as
            // the outage it almost certainly is rather than letting it end the whole pass.
            _logger.Error(ex, "Health check {Check} threw instead of returning a result", entry.Name);
            result = HealthResult.Failed(ex);
        }

        await RecordAsync(entry, result).ConfigureAwait(false);
    }

    /// <summary>Applies a result, then announces it if the status actually moved.</summary>
    private async Task RecordAsync(HealthEntry entry, HealthResult result)
    {
        var checkedAt = DateTimeOffset.Now;

        // Awaited rather than blocked on: the previous status has to come back before the
        // event can be raised, and blocking a thread pool thread on the dispatcher would
        // deadlock whenever the user-interface thread is itself waiting on this pass.
        var previous = await OnUiThreadAsync(() => entry.Record(result, checkedAt))
            .ConfigureAwait(false);

        if (previous == entry.Status)
        {
            return;
        }

        // A subsystem going down or coming back is worth a line either way; the level
        // differs so a log filtered to warnings shows outages without the recoveries.
        if (entry.Status.IsAlerting())
        {
            _logger.Warning(
                "Health check {Check} is {Status}: {Detail}", entry.Name, entry.Status, result.Detail);
        }
        else
        {
            _logger.Information(
                "Health check {Check} is {Status}: {Detail}", entry.Name, entry.Status, result.Detail);
        }

        var args = new HealthChangedEventArgs(entry, previous);

        HealthChanged?.Invoke(this, args);

        _eventPublisher.Publish(new HealthStatusChangedEvent(
            entry.Name,
            previous,
            entry.Status,
            result.Detail,
            nameof(HealthMonitorService)));
    }

    /// <summary>Runs a mutation of bound state on the user-interface thread.</summary>
    /// <remarks>
    /// <see cref="ObservableCollection{T}"/> and every property on
    /// <see cref="HealthEntry"/> raise change notification, which WPF only permits from the
    /// thread that created the binding. Probes complete on a thread pool thread.
    /// </remarks>
    private void OnUiThread(Action action)
    {
        if (_dispatcher.IsOnUiThread)
        {
            action();
            return;
        }

        _dispatcher.Post(action);
    }

    /// <summary>
    /// Runs an operation on the user-interface thread and returns its result.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="OnUiThread"/> because a caller that needs the return value
    /// has to await the hop, whereas a collection mutation can be posted and forgotten.
    /// </remarks>
    private Task<T> OnUiThreadAsync<T>(Func<T> operation)
        => _dispatcher.IsOnUiThread
            ? Task.FromResult(operation())
            : _dispatcher.InvokeAsync(operation);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshGate.Dispose();
    }
}
