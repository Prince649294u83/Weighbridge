using Microsoft.Extensions.Logging;

namespace WeighBridge.Tests.Infrastructure;

/// <summary>Captures one entry written to <see cref="RecordingLoggerProvider"/>.</summary>
internal sealed record RecordedEntry(
    string Category,
    LogLevel Level,
    string Message,
    Exception? Exception,
    IReadOnlyList<object?> Scopes)
{
    /// <summary>Scope text joined, as the file logger would render it.</summary>
    public string ScopeText => string.Join(" | ", Scopes.Select(scope => scope?.ToString()));
}

/// <summary>
/// <see cref="ILoggerProvider"/> that keeps entries in memory so a test can assert on what
/// would have been written, including the scopes active at the time.
/// </summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly List<RecordedEntry> _entries = [];
    private readonly object _gate = new();

    /// <summary>Minimum level this provider accepts, for testing the enabled check.</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;

    /// <summary>Everything recorded so far.</summary>
    public IReadOnlyList<RecordedEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    /// <summary>The most recent entry, or <c>null</c>.</summary>
    public RecordedEntry? Last
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count == 0 ? null : _entries[^1];
            }
        }
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new RecordingLogger(this, categoryName);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private void Add(RecordedEntry entry)
    {
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    private sealed class RecordingLogger(RecordingLoggerProvider owner, string category) : ILogger
    {
        private readonly AsyncLocal<Stack<object?>> _scopes = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            _scopes.Value ??= new Stack<object?>();
            _scopes.Value.Push(state);
            return new ScopeHandle(_scopes.Value);
        }

        public bool IsEnabled(LogLevel logLevel)
            => logLevel != LogLevel.None && logLevel >= owner.MinimumLevel;

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

            var scopes = _scopes.Value is null ? [] : _scopes.Value.ToArray();

            owner.Add(new RecordedEntry(
                category,
                logLevel,
                formatter(state, exception),
                exception,
                scopes));
        }

        private sealed class ScopeHandle(Stack<object?> scopes) : IDisposable
        {
            public void Dispose()
            {
                if (scopes.Count > 0)
                {
                    scopes.Pop();
                }
            }
        }
    }
}
