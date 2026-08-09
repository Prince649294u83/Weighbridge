namespace WeighBridge.Core.Threading;

/// <summary>
/// Marshals work onto the user-interface thread.
/// </summary>
/// <remarks>
/// <para>
/// Infrastructure that lives below the presentation layer regularly needs to touch
/// state a view is bound to — an event bus delivering to a ViewModel, a background task
/// reporting progress, a notification being added to an observable collection. WPF only
/// permits that from the thread that created the object, but the Core and Services
/// layers deliberately carry no WPF reference.
/// </para>
/// <para>
/// This abstraction is the seam. The application supplies a
/// <c>Dispatcher</c>-backed implementation; the test suite supplies one that runs
/// inline, which is what lets the whole infrastructure layer be exercised headlessly.
/// </para>
/// </remarks>
public interface IUiDispatcher
{
    /// <summary>
    /// True when the caller is already on the user-interface thread and can touch
    /// bound state directly.
    /// </summary>
    bool IsOnUiThread { get; }

    /// <summary>
    /// Queues <paramref name="action"/> on the user-interface thread and returns without
    /// waiting for it. Use when the caller has nothing to do with the outcome.
    /// </summary>
    void Post(Action action);

    /// <summary>
    /// Runs <paramref name="action"/> on the user-interface thread and awaits it.
    /// Executes inline when the caller is already on that thread, so an <c>await</c>
    /// from the UI thread costs nothing.
    /// </summary>
    Task InvokeAsync(Action action);

    /// <summary>
    /// Runs <paramref name="operation"/> on the user-interface thread and awaits its
    /// result.
    /// </summary>
    Task<TResult> InvokeAsync<TResult>(Func<TResult> operation);
}
