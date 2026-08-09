namespace WeighBridge.Core.Diagnostics;

/// <summary>
/// Opens a module scope and a correlation scope together, so one <c>using</c> statement
/// tags everything an operation logs with both where it came from and what it belongs to.
/// </summary>
/// <remarks>
/// This is what the command pipeline opens around each command: every entry written by
/// validation, execution, notification and audit then shares one correlation identifier,
/// which is what makes a failure reconstructable from the log afterwards.
/// </remarks>
public static class OperationScope
{
    /// <summary>
    /// Begins an operation.
    /// </summary>
    /// <param name="module">Module the operation belongs to.</param>
    /// <param name="correlationId">
    /// Correlation identifier. <c>null</c> inherits an enclosing one, or starts a new one
    /// when there is none — so a nested operation stays part of its parent's story.
    /// </param>
    public static IDisposable Begin(string module, string? correlationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);

        var correlation = CorrelationScope.Begin(correlationId);

        try
        {
            return new CompositeScope(ModuleScope.Begin(module), correlation);
        }
        catch
        {
            // Never leak the correlation scope if the module scope rejects its argument.
            correlation.Dispose();
            throw;
        }
    }

    /// <summary>Disposes both scopes, innermost first.</summary>
    private sealed class CompositeScope(IDisposable module, IDisposable correlation) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            module.Dispose();
            correlation.Dispose();
        }
    }
}
