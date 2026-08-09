using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Core.Logging;

/// <summary>
/// <see cref="ILoggerProvider"/> that writes a rolling daily log file into the
/// application's Logs folder.
/// </summary>
/// <remarks>
/// Registered through <see cref="FileLoggerExtensions.AddWeighBridgeFileLogger"/>.
/// The provider owns the single <see cref="FileLogWriter"/> shared by every category.
/// </remarks>
[ProviderAlias("File")]
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly FileLoggingOptions _options;
    private readonly FileLogWriter _writer;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);

    private bool _disposed;

    public FileLoggerProvider(string logDirectory, FileLoggingOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _writer = new FileLogWriter(logDirectory, options);
    }

    /// <summary>Path of the log file currently being written.</summary>
    public string? CurrentLogFilePath => _writer.CurrentFilePath;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _writer, _options));

    /// <summary>Blocks briefly until queued entries have been written.</summary>
    public void Flush() => _writer.Flush(TimeSpan.FromSeconds(2));

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loggers.Clear();
        _writer.Dispose();
    }
}
