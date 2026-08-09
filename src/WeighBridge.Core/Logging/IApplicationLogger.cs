using Microsoft.Extensions.Logging;

namespace WeighBridge.Core.Logging;

/// <summary>
/// A category logger. Every entry it writes is enriched with the operator, terminal,
/// application version, active module and correlation identifier.
/// </summary>
/// <remarks>
/// <para>
/// A module takes this rather than <see cref="ILogger{TCategoryName}"/> so it cannot
/// accidentally write uncorrelated entries, and so the category stays an operational
/// contract instead of following whatever the class happens to be called.
/// </para>
/// <para>
/// Errors go here, never to a message box. A modal dialog blocks the queue of trucks
/// behind the one on the weighbridge; the operator is told through
/// <see cref="Notifications.INotificationService"/> while the detail lands in the log.
/// </para>
/// </remarks>
public interface IApplicationLogger
{
    /// <summary>Category these entries are written under.</summary>
    string Category { get; }

    /// <summary>Very fine detail — protocol bytes, per-frame state. Off in the field.</summary>
    void Trace(string message, params object?[] args);

    /// <summary>Detail useful when diagnosing a problem.</summary>
    void Debug(string message, params object?[] args);

    /// <summary>Normal significant activity.</summary>
    void Information(string message, params object?[] args);

    /// <summary>Something recoverable that a supervisor should know about.</summary>
    void Warning(string message, params object?[] args);

    /// <summary>An operation failed.</summary>
    void Error(string message, params object?[] args);

    /// <summary>An operation failed, with the exception that caused it.</summary>
    void Error(Exception exception, string message, params object?[] args);

    /// <summary>The application cannot continue, or has lost data.</summary>
    void Critical(string message, params object?[] args);

    /// <summary>The application cannot continue, with the exception that caused it.</summary>
    void Critical(Exception exception, string message, params object?[] args);

    /// <summary>True when the level would be written, for guarding expensive messages.</summary>
    bool IsEnabled(LogLevel level);

    /// <summary>
    /// Opens an operation: a module scope and a correlation scope together, so everything
    /// logged inside shares one identifier.
    /// </summary>
    IDisposable BeginOperation(string module, string? correlationId = null);
}
