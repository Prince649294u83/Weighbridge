using WeighBridge.Core.Threading;

namespace WeighBridge.Tests.Infrastructure;

/// <summary>
/// <see cref="IUiDispatcher"/> for tests: records what was marshalled and can pretend to
/// be off the UI thread so the marshalling path is exercised headlessly.
/// </summary>
internal sealed class TestUiDispatcher(bool isOnUiThread = true) : IUiDispatcher
{
    /// <inheritdoc />
    public bool IsOnUiThread { get; set; } = isOnUiThread;

    /// <summary>How many operations were routed through the dispatcher.</summary>
    public int MarshalledCount { get; private set; }

    /// <inheritdoc />
    public void Post(Action action)
    {
        MarshalledCount++;
        action();
    }

    /// <inheritdoc />
    public Task InvokeAsync(Action action)
    {
        MarshalledCount++;
        action();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<TResult> InvokeAsync<TResult>(Func<TResult> operation)
    {
        MarshalledCount++;
        return Task.FromResult(operation());
    }
}
