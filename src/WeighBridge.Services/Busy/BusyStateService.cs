using System.Collections.ObjectModel;
using System.ComponentModel;
using WeighBridge.Core.Busy;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Threading;

namespace WeighBridge.Services.Busy;

/// <summary>
/// Tracks the operations in flight and reports whether the application is busy.
/// </summary>
/// <remarks>
/// <para>
/// The list under the lock is the truth and the observable collection is a mirror of it
/// updated through <see cref="IUiDispatcher"/> — the same split as
/// <c>BackgroundTaskManager</c>, and for the same reason: the mirror may lag by a dispatcher
/// hop, so nothing in here makes a decision by reading it.
/// </para>
/// <para>
/// Nothing is called while the lock is held. A handler of
/// <see cref="IBusyStateService.BusyStateChanged"/> that began another operation would
/// otherwise deadlock against the scope that raised it.
/// </para>
/// </remarks>
public sealed class BusyStateService : IBusyStateService
{
    private readonly List<BusyOperation> _operations = [];
    private readonly ObservableCollection<IBusyOperation> _mirror = [];
    private readonly object _gate = new();

    private readonly IUiDispatcher _dispatcher;
    private readonly IApplicationLogger _logger;

    /// <summary>Creates the service with nothing in flight.</summary>
    public BusyStateService(IUiDispatcher dispatcher, IApplicationLogger logger)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(logger);

        _dispatcher = dispatcher;
        _logger = logger;

        Active = new ReadOnlyObservableCollection<IBusyOperation>(_mirror);
    }

    /// <inheritdoc />
    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                return _operations.Count > 0;
            }
        }
    }

    /// <inheritdoc />
    public int ActiveCount
    {
        get
        {
            lock (_gate)
            {
                return _operations.Count;
            }
        }
    }

    /// <inheritdoc />
    public IBusyOperation? Current
    {
        get
        {
            lock (_gate)
            {
                // Most recently started, not the outermost: it describes what the operator
                // just did, which is the useful thing to put next to a spinner.
                return _operations.Count == 0 ? null : _operations[^1];
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IBusyOperation> Active { get; }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc />
    public event EventHandler<BusyStateChangedEventArgs>? BusyStateChanged;

    /// <inheritdoc />
    public Task<IBusyScope> BeginAsync(
        string title,
        bool isCancellable = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var operation = new BusyOperation(title, isCancellable, DateTimeOffset.Now)
        {
            Status = null,
            IsIndeterminate = true,
        };

        var scope = new BusyScope(this, operation, cancellationToken);

        bool becameBusy;

        lock (_gate)
        {
            becameBusy = _operations.Count == 0;
            _operations.Add(operation);
        }

        _logger.Debug("Busy operation started: {Title} ({Id})", title, operation.Id);

        // The hop is awaited rather than posted so the caller's first line after
        // BeginAsync already sees a bound indicator, which is what makes the
        // 'using var busy = await BeginAsync(...)' shape behave the way it reads.
        return OnUiThreadAsync(() =>
        {
            _mirror.Add(operation);
            RaiseChanged(becameBusy);

            return (IBusyScope)scope;
        });
    }

    /// <summary>Releases a scope. Idempotent — a second call does nothing.</summary>
    private void Release(BusyOperation operation)
    {
        bool removed;
        bool becameIdle;

        lock (_gate)
        {
            removed = _operations.Remove(operation);
            becameIdle = removed && _operations.Count == 0;
        }

        if (!removed)
        {
            return;
        }

        var elapsed = DateTimeOffset.Now - operation.StartedAt;

        _logger.Debug(
            "Busy operation finished: {Title} ({Id}) after {Elapsed:F0} ms",
            operation.Title,
            operation.Id,
            elapsed.TotalMilliseconds);

        OnUiThread(() =>
        {
            _mirror.Remove(operation);
            RaiseChanged(becameIdle);
        });
    }

    /// <summary>
    /// Announces the current state. Runs on the user-interface thread.
    /// </summary>
    /// <remarks>
    /// <see cref="IsBusy"/> is only announced when it actually flipped, so a nested
    /// operation does not make the shell re-evaluate a binding that has not changed.
    /// <see cref="ActiveCount"/> and <see cref="Current"/> change on every begin and
    /// release and are always announced.
    /// </remarks>
    private void RaiseChanged(bool busyFlipped)
    {
        if (busyFlipped)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));

        BusyStateChanged?.Invoke(this, new BusyStateChangedEventArgs(IsBusy, ActiveCount, Current));
    }

    private void OnUiThread(Action action)
    {
        if (_dispatcher.IsOnUiThread)
        {
            action();
            return;
        }

        _dispatcher.Post(action);
    }

    private Task<T> OnUiThreadAsync<T>(Func<T> operation)
        => _dispatcher.IsOnUiThread
            ? Task.FromResult(operation())
            : _dispatcher.InvokeAsync(operation);

    /// <summary>
    /// One operation's hold on the indicator.
    /// </summary>
    /// <remarks>
    /// Progress reports go straight onto the observable operation through the dispatcher.
    /// They are posted rather than awaited: a loop reporting every row must not pay a
    /// round trip per report, and a report that lands a frame late is invisible.
    /// </remarks>
    private sealed class BusyScope : IBusyScope
    {
        private readonly BusyStateService _owner;
        private readonly BusyOperation _operation;
        private readonly CancellationTokenSource _cancellation;

        // An int rather than a bool: the bool overload of Interlocked.Exchange is .NET 9.
        private int _completed;

        public BusyScope(BusyStateService owner, BusyOperation operation, CancellationToken cancellationToken)
        {
            _owner = owner;
            _operation = operation;

            // Linked, so shutdown cancels the operation even when the operator never
            // touched the cancel button.
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        public IBusyOperation Operation => _operation;

        public CancellationToken CancellationToken => _cancellation.Token;

        public bool IsCompleted => Volatile.Read(ref _completed) != 0;

        public void ReportStatus(string status) => Post(() => _operation.Status = status);

        public void ReportProgress(double percentage) => Post(() =>
        {
            _operation.IsIndeterminate = false;
            _operation.Percentage = percentage;
        });

        public void Report(double percentage, string status) => Post(() =>
        {
            _operation.IsIndeterminate = false;
            _operation.Percentage = percentage;
            _operation.Status = status;
        });

        public void ReportIndeterminate(string status) => Post(() =>
        {
            _operation.IsIndeterminate = true;
            _operation.Status = status;
        });

        public void Cancel()
        {
            if (!_operation.IsCancellable || IsCompleted)
            {
                return;
            }

            Post(() => _operation.IsCancelling = true);

            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The operation completed between the check above and here. Nothing to stop.
            }
        }

        public void Dispose()
        {
            // Interlocked rather than a bool check: a caller that disposes from a finally
            // while another thread cancels must release exactly once.
            if (Interlocked.Exchange(ref _completed, 1) != 0)
            {
                return;
            }

            _owner.Release(_operation);
            _cancellation.Dispose();
        }

        private void Post(Action action)
        {
            if (IsCompleted)
            {
                // A report arriving after the operation ended has nowhere to go, and
                // writing it would resurrect a stale line in the indicator.
                return;
            }

            _owner.OnUiThread(action);
        }
    }
}
