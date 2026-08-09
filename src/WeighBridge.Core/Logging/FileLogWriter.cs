using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Core.Logging;

/// <summary>
/// Serialises log entries to a daily file on a dedicated background thread.
/// </summary>
/// <remarks>
/// A single writer owns the file handle and drains a queue, so logging never blocks
/// the UI thread and concurrent writes cannot interleave. Rollover happens both on
/// date change and when the configured size cap is reached.
/// </remarks>
internal sealed class FileLogWriter : IDisposable
{
    private const int MaxQueueLength = 10_000;

    private readonly FileLoggingOptions _options;
    private readonly string _logDirectory;
    private readonly BlockingCollection<string> _queue;
    private readonly Thread _worker;
    private readonly object _fileLock = new();

    private DateOnly _currentDate;
    private string? _currentFilePath;
    private int _rollIndex;
    private bool _disposed;

    public FileLogWriter(string logDirectory, FileLoggingOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentNullException.ThrowIfNull(options);

        _logDirectory = logDirectory;
        _options = options;
        _queue = new BlockingCollection<string>(new ConcurrentQueue<string>(), MaxQueueLength);

        Directory.CreateDirectory(_logDirectory);

        _currentDate = DateOnly.FromDateTime(DateTime.Now);
        _currentFilePath = ResolveFilePath(_currentDate, _rollIndex);

        PurgeExpiredFiles();

        _worker = new Thread(DrainQueue)
        {
            IsBackground = true,
            Name = "WeighBridge.FileLogWriter",
        };

        _worker.Start();
    }

    /// <summary>Path of the file currently being written, for diagnostics.</summary>
    public string? CurrentFilePath => _currentFilePath;

    /// <summary>
    /// Enqueues a formatted line. Never throws and never blocks: if the queue is
    /// saturated the entry is dropped rather than stalling the caller.
    /// </summary>
    public void Enqueue(string line)
    {
        if (_disposed || _queue.IsAddingCompleted)
        {
            return;
        }

        try
        {
            _queue.TryAdd(line);
        }
        catch (InvalidOperationException)
        {
            // The collection was completed concurrently during shutdown — ignore.
        }
    }

    /// <summary>Blocks until the queue has been flushed or the timeout elapses.</summary>
    public void Flush(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (_queue.Count > 0 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(15);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _queue.CompleteAdding();

        // Give the worker a bounded window to drain; never hang application shutdown.
        _worker.Join(TimeSpan.FromSeconds(3));

        _queue.Dispose();
    }

    private void DrainQueue()
    {
        try
        {
            foreach (var line in _queue.GetConsumingEnumerable())
            {
                WriteLine(line);
            }
        }
        catch (ObjectDisposedException)
        {
            // Raced with Dispose — nothing left to write.
        }
        catch (InvalidOperationException)
        {
            // Enumeration ended because adding was completed.
        }
    }

    private void WriteLine(string line)
    {
        lock (_fileLock)
        {
            try
            {
                RollIfRequired();

                if (_currentFilePath is not null)
                {
                    File.AppendAllText(_currentFilePath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Logging must never take the application down. A locked or full disk
                // degrades to "no file logging" rather than a crash.
            }
        }
    }

    /// <summary>Switches to a new file when the day changed or the size cap was hit.</summary>
    private void RollIfRequired()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        if (today != _currentDate)
        {
            _currentDate = today;
            _rollIndex = 0;
            _currentFilePath = ResolveFilePath(_currentDate, _rollIndex);

            PurgeExpiredFiles();
            return;
        }

        if (_currentFilePath is null || _options.MaxFileSizeMegabytes <= 0)
        {
            return;
        }

        var maxBytes = (long)_options.MaxFileSizeMegabytes * 1024 * 1024;
        var info = new FileInfo(_currentFilePath);

        if (info.Exists && info.Length >= maxBytes)
        {
            _rollIndex++;
            _currentFilePath = ResolveFilePath(_currentDate, _rollIndex);
        }
    }

    private string ResolveFilePath(DateOnly date, int rollIndex)
    {
        var pattern = string.IsNullOrWhiteSpace(_options.FileNamePattern)
            ? "weighbridge-{Date}.log"
            : _options.FileNamePattern;

        var fileName = pattern.Replace("{Date}", date.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase);

        if (rollIndex > 0)
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            fileName = $"{name}.{rollIndex}{extension}";
        }

        return Path.Combine(_logDirectory, fileName);
    }

    /// <summary>Deletes log files older than the retention window.</summary>
    private void PurgeExpiredFiles()
    {
        if (_options.RetainedDays <= 0)
        {
            return;
        }

        try
        {
            var cutoff = DateTime.Now.AddDays(-_options.RetainedDays);

            foreach (var file in Directory.EnumerateFiles(_logDirectory, "*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Retention is best-effort.
        }
    }
}
