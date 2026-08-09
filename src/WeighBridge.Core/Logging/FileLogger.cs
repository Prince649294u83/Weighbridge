using System.Text;
using Microsoft.Extensions.Logging;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Core.Logging;

/// <summary>
/// <see cref="ILogger"/> implementation that formats entries and hands them to the
/// shared <see cref="FileLogWriter"/>.
/// </summary>
internal sealed class FileLogger(string categoryName, FileLogWriter writer, FileLoggingOptions options) : ILogger
{
    private readonly string _categoryName = categoryName;
    private readonly FileLogWriter _writer = writer;
    private readonly FileLoggingOptions _options = options;

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => FileLoggerScope.Push(state.ToString());

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
        => _options.Enabled && logLevel != LogLevel.None && logLevel >= _options.MinimumLevel;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(formatter);

        var message = formatter(state, exception);

        if (string.IsNullOrEmpty(message) && exception is null)
        {
            return;
        }

        var builder = new StringBuilder(256);

        builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        builder.Append(" [").Append(FormatLevel(logLevel)).Append(']');
        builder.Append(" [").Append(Environment.CurrentManagedThreadId.ToString("D2")).Append(']');
        builder.Append(' ').Append(_categoryName);

        var scope = FileLoggerScope.Current;

        if (!string.IsNullOrEmpty(scope))
        {
            builder.Append(" {").Append(scope).Append('}');
        }

        builder.Append(" :: ").Append(message);

        if (exception is not null)
        {
            builder.AppendLine();
            builder.Append(exception);
        }

        _writer.Enqueue(builder.ToString());
    }

    /// <summary>Fixed-width level tokens keep the log columns readable.</summary>
    private static string FormatLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "___",
    };
}
