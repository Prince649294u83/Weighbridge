namespace WeighBridge.Core.Logging;

/// <summary>
/// Ambient logging scope, flowed across async calls so a scope opened on the UI
/// thread still decorates entries written from a continuation.
/// </summary>
internal sealed class FileLoggerScope : IDisposable
{
    private static readonly AsyncLocal<FileLoggerScope?> Ambient = new();

    private readonly FileLoggerScope? _parent;
    private bool _disposed;

    private FileLoggerScope(string? description, FileLoggerScope? parent)
    {
        Description = description;
        _parent = parent;
    }

    /// <summary>Text supplied when the scope was opened.</summary>
    public string? Description { get; }

    /// <summary>The innermost active scope description, or <c>null</c>.</summary>
    public static string? Current => Ambient.Value?.Description;

    /// <summary>Opens a new scope; disposing it restores the previous one.</summary>
    public static IDisposable Push(string? description)
    {
        var scope = new FileLoggerScope(description, Ambient.Value);
        Ambient.Value = scope;
        return scope;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Ambient.Value = _parent;
    }
}
