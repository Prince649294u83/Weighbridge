namespace WeighBridge.Core.Diagnostics;

/// <summary>
/// Ambient correlation identifier that ties together everything one operator action
/// causes: the command that ran, the events it published, the log entries it wrote and
/// the audit record it left behind.
/// </summary>
/// <remarks>
/// <para>
/// Backed by <see cref="AsyncLocal{T}"/> so the identifier flows across
/// <c>await</c> boundaries. A weighment started on the UI thread keeps the same
/// correlation identifier in the continuation that writes to the database and in the
/// background task that prints the slip.
/// </para>
/// <para>
/// This is the one piece of static state the infrastructure layer keeps, and it is
/// deliberate: an ambient value is the only way to correlate without threading a
/// parameter through every signature in the application. It is read-only from the
/// outside and reset by disposing the scope.
/// </para>
/// </remarks>
public static class CorrelationScope
{
    private static readonly AsyncLocal<Scope?> Ambient = new();

    /// <summary>
    /// The identifier of the innermost active scope, or <c>null</c> when the current
    /// call is not part of a correlated operation.
    /// </summary>
    public static string? Current => Ambient.Value?.CorrelationId;

    /// <summary>
    /// The current identifier, or a freshly generated one when no scope is active.
    /// Use when a value is required but starting a scope is not appropriate.
    /// </summary>
    public static string CurrentOrNew => Ambient.Value?.CorrelationId ?? NewId();

    /// <summary>
    /// Generates a correlation identifier: twelve hexadecimal characters, short enough
    /// to stay readable in a log column and wide enough to avoid collisions within a
    /// session.
    /// </summary>
    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// Starts a correlation scope. Disposing the returned object restores the previous
    /// scope, so scopes nest correctly.
    /// </summary>
    /// <param name="correlationId">
    /// Identifier to adopt. When omitted, an existing ambient identifier is inherited
    /// and a new one is generated only if there is none — so a nested operation joins
    /// the caller's correlation instead of starting its own.
    /// </param>
    public static IDisposable Begin(string? correlationId = null)
    {
        var effectiveId = string.IsNullOrWhiteSpace(correlationId)
            ? CurrentOrNew
            : correlationId;

        var scope = new Scope(effectiveId, Ambient.Value);
        Ambient.Value = scope;
        return scope;
    }

    /// <summary>One entry in the scope chain.</summary>
    private sealed class Scope(string correlationId, Scope? parent) : IDisposable
    {
        private bool _disposed;

        public string CorrelationId { get; } = correlationId;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Ambient.Value = parent;
        }
    }
}
