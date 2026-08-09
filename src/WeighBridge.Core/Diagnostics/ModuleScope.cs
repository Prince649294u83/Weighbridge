namespace WeighBridge.Core.Diagnostics;

/// <summary>
/// Ambient module name, flowed across async calls so every log entry written while a
/// module is active is attributed to it without the module passing its own name down.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="CorrelationScope"/> deliberately: the two answer the two questions
/// asked of an industrial log — <em>which part of the application</em> wrote this, and
/// <em>which operation</em> was it part of. Kept separate because their lifetimes differ:
/// a module scope spans a page, a correlation scope spans one command.
/// </para>
/// <para>
/// An explicit module argument at a log call site always wins over the ambient value.
/// </para>
/// </remarks>
public static class ModuleScope
{
    private static readonly AsyncLocal<Scope?> Ambient = new();

    /// <summary>The innermost active module name, or <c>null</c>.</summary>
    public static string? Current => Ambient.Value?.Module;

    /// <summary>
    /// Opens a module scope. Disposing it restores the enclosing one, so scopes nest
    /// correctly when one module calls into a shared service that opens its own.
    /// </summary>
    public static IDisposable Begin(string module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        return new Scope(module);
    }

    private sealed class Scope : IDisposable
    {
        private readonly Scope? _parent;
        private bool _disposed;

        internal Scope(string module)
        {
            Module = module;
            _parent = Ambient.Value;
            Ambient.Value = this;
        }

        internal string Module { get; }

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
}
