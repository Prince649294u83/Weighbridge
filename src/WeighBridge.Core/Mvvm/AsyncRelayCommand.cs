using System.Windows.Input;

namespace WeighBridge.Core.Mvvm;

/// <summary>
/// Asynchronous <see cref="ICommand"/> that prevents re-entrancy while the operation
/// is running and surfaces faults through an optional error handler instead of
/// crashing on an unobserved <see cref="Task"/> exception.
/// </summary>
/// <remarks>
/// When a command was built without its own <c>onError</c> handler, faults go to
/// <see cref="UnhandledExecutionError"/>. If nobody has subscribed there either, the
/// exception is rethrown so tests see it — but the host is expected to subscribe at
/// startup, which is what turns "fire-and-forget command failed silently" into a log
/// entry and an operator-visible notification.
/// </remarks>
public sealed class AsyncRelayCommand : ICommand
{
    /// <summary>
    /// Receives every fault from a command that carries no handler of its own.
    /// The composition root subscribes once at startup.
    /// </summary>
    public static event Action<Exception>? UnhandledExecutionError;

    internal static bool HasGlobalHandler() => UnhandledExecutionError is not null;

    internal static void RaiseUnhandled(Exception exception) => UnhandledExecutionError?.Invoke(exception);

    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception>? _onError;

    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        _execute = execute;
        _canExecute = canExecute;
        _onError = onError;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <summary>True while the wrapped operation is in flight.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning == value)
            {
                return;
            }

            _isRunning = value;
            NotifyCanExecuteChanged();
        }
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    /// <inheritdoc />
    public void Execute(object? parameter) => _ = ExecuteAsync();

    /// <summary>Awaitable execution path, primarily for unit tests.</summary>
    public async Task ExecuteAsync()
    {
        if (!CanExecute(null))
        {
            return;
        }

        IsRunning = true;

        try
        {
            await _execute().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is a decision, not a fault.
        }
        catch (Exception ex) when (_onError is not null || HasGlobalHandler())
        {
            if (_onError is not null)
            {
                _onError(ex);
            }
            else
            {
                RaiseUnhandled(ex);
            }
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>Forces the UI to re-query <see cref="CanExecute"/>.</summary>
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Strongly typed asynchronous <see cref="ICommand"/>. Behaves exactly like
/// <see cref="AsyncRelayCommand"/> but passes the command parameter through to the
/// wrapped operation, which is what an item-driven action such as a navigation click
/// needs.
/// </summary>
public sealed class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private readonly Action<Exception>? _onError;

    private bool _isRunning;

    public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null, Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        _execute = execute;
        _canExecute = canExecute;
        _onError = onError;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <summary>True while the wrapped operation is in flight.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning == value)
            {
                return;
            }

            _isRunning = value;
            NotifyCanExecuteChanged();
        }
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke(Cast(parameter)) ?? true);

    /// <inheritdoc />
    public void Execute(object? parameter) => _ = ExecuteAsync(Cast(parameter));

    /// <summary>Awaitable execution path, primarily for unit tests.</summary>
    public async Task ExecuteAsync(T? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        IsRunning = true;

        try
        {
            await _execute(parameter).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is a decision, not a fault.
        }
        catch (Exception ex) when (_onError is not null || AsyncRelayCommand.HasGlobalHandler())
        {
            if (_onError is not null)
            {
                _onError(ex);
            }
            else
            {
                AsyncRelayCommand.RaiseUnhandled(ex);
            }
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>Forces the UI to re-query <see cref="CanExecute"/>.</summary>
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static T? Cast(object? parameter) => parameter is T typed ? typed : default;
}
