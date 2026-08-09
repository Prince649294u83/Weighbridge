using System.Windows.Threading;
using WeighBridge.Core.Threading;

namespace WeighBridge.App.Services;

/// <summary>
/// <see cref="IUiDispatcher"/> backed by the WPF <see cref="Dispatcher"/>.
/// </summary>
/// <remarks>
/// <para>
/// Captures the dispatcher of the thread that constructs it. Registered from the
/// composition root, which runs on the UI thread, so that is the right one — but it is
/// captured explicitly rather than read from <c>Application.Current</c> on each call, so
/// the class has no dependency on a static and stays usable during startup before
/// <c>Application.Current</c> is fully initialised.
/// </para>
/// <para>
/// This is the only implementation of the abstraction that knows about WPF, which is what
/// keeps every service below the presentation layer free of a UI framework reference
/// while still being able to update bound state.
/// </para>
/// </remarks>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    /// <summary>Captures the dispatcher of the calling thread.</summary>
    public WpfUiDispatcher()
        : this(Dispatcher.CurrentDispatcher)
    {
    }

    /// <summary>Wraps a specific dispatcher.</summary>
    /// <param name="dispatcher">The dispatcher to marshal onto.</param>
    public WpfUiDispatcher(Dispatcher dispatcher)
        => _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    /// <inheritdoc />
    public bool IsOnUiThread => _dispatcher.CheckAccess();

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        // BeginInvoke rather than Invoke: the contract is fire-and-forget, and a
        // blocking Invoke from a hardware polling thread would couple its cadence to
        // however long the UI takes to become responsive.
        _dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
    }

    /// <inheritdoc />
    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_dispatcher.CheckAccess())
        {
            // Inline, so awaiting from the UI thread neither yields nor queues. Letting
            // it round-trip through the dispatcher queue would reorder the caller's work
            // behind whatever is already pending.
            action();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action).Task;
    }

    /// <inheritdoc />
    public Task<TResult> InvokeAsync<TResult>(Func<TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (_dispatcher.CheckAccess())
        {
            return Task.FromResult(operation());
        }

        return _dispatcher.InvokeAsync(operation).Task;
    }
}
