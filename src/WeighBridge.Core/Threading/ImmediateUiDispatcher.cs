namespace WeighBridge.Core.Threading;

/// <summary>
/// <see cref="IUiDispatcher"/> that runs everything inline on the calling thread.
/// </summary>
/// <remarks>
/// Registered as the fallback so that a service depending on <see cref="IUiDispatcher"/>
/// is constructible in a headless process — the test suite and any future console or
/// service host. The application replaces it with the WPF-backed implementation during
/// composition, which is why every layer registers it with <c>TryAdd</c> semantics.
/// </remarks>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    /// <remarks>
    /// Reports <c>true</c> because there is no separate UI thread to marshal to: the
    /// calling thread is as close to one as this implementation has.
    /// </remarks>
    public bool IsOnUiThread => true;

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
    }

    /// <inheritdoc />
    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<TResult> InvokeAsync<TResult>(Func<TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return Task.FromResult(operation());
    }
}
