using Microsoft.Extensions.Logging;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using WeighBridge.Core.Dialogs;

namespace WeighBridge.App.Dialogs;

/// <summary>
/// Shows the application's custom dialog windows.
/// </summary>
/// <remarks>
/// Every method marshals onto the UI dispatcher, so ViewModels and services may call
/// them from a worker thread without knowing anything about WPF threading.
/// </remarks>
public sealed class DialogService : IDialogService
{
    private readonly ILogger<DialogService> _logger;
    private readonly Dispatcher _dispatcher;

    public DialogService(ILogger<DialogService> logger)
    {
        _logger = logger;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    /// <inheritdoc />
    public Task ShowInformationAsync(string title, string message, string? details = null)
        => ShowMessageAsync(title, message, DialogSeverity.Information, details);

    /// <inheritdoc />
    public Task ShowSuccessAsync(string title, string message, string? details = null)
        => ShowMessageAsync(title, message, DialogSeverity.Success, details);

    /// <inheritdoc />
    public Task ShowWarningAsync(string title, string message, string? details = null)
        => ShowMessageAsync(title, message, DialogSeverity.Warning, details);

    /// <inheritdoc />
    public Task ShowErrorAsync(string title, string message, string? details = null)
        => ShowMessageAsync(title, message, DialogSeverity.Error, details);

    /// <inheritdoc />
    public Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string confirmText = "Yes",
        string cancelText = "No",
        bool isDestructive = false)
        => InvokeAsync(() =>
        {
            var viewModel = new MessageDialogViewModel(
                title,
                message,
                isDestructive ? DialogSeverity.Warning : DialogSeverity.Question,
                details: null,
                confirmText,
                cancelText,
                isDestructive);

            return ShowDialogWindow(new MessageDialog(viewModel)) == true;
        });

    /// <inheritdoc />
    public Task ShowLoadingAsync(string message, Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return RunCoveredAsync(
            title: message,
            status: "Please wait…",
            isCancellable: false,
            isIndeterminate: true,
            operation: _ => operation());
    }

    /// <inheritdoc />
    public Task ShowProgressAsync(string title, Func<IProgressReporter, Task> operation, bool isCancellable = false)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return RunCoveredAsync(
            title,
            status: "Starting…",
            isCancellable,
            isIndeterminate: true,
            operation);
    }

    private Task ShowMessageAsync(string title, string message, DialogSeverity severity, string? details)
    {
        _logger.LogDebug("Showing {Severity} dialog: {Title}", severity, title);

        return InvokeAsync(() =>
        {
            var viewModel = new MessageDialogViewModel(title, message, severity, details, confirmText: "OK");

            ShowDialogWindow(new MessageDialog(viewModel));
            return true;
        });
    }

    /// <summary>
    /// Shows a modal progress dialog, runs <paramref name="operation"/> behind it and
    /// closes the dialog when the operation ends however it ends.
    /// </summary>
    private Task RunCoveredAsync(
        string title,
        string status,
        bool isCancellable,
        bool isIndeterminate,
        Func<IProgressReporter, Task> operation)
        => InvokeAsync(() =>
        {
            var viewModel = new ProgressDialogViewModel(title, status, isCancellable, isIndeterminate);
            var dialog = new ProgressDialog(viewModel) { Owner = ResolveOwner() };

            Exception? failure = null;

            dialog.Loaded += async (_, _) =>
            {
                try
                {
                    await operation(viewModel).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Operation '{Title}' was cancelled by the operator.", title);
                }
                catch (Exception ex)
                {
                    // Rethrown to the caller below; the dialog must still come down.
                    failure = ex;
                }
                finally
                {
                    dialog.CompleteAndClose();
                }
            };

            // ShowDialog pumps messages until CompleteAndClose runs, so by the time it
            // returns the operation has finished and 'failure' is settled.
            dialog.ShowDialog();
            viewModel.Dispose();

            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

            return true;
        });

    private bool? ShowDialogWindow(Window dialog)
    {
        dialog.Owner = ResolveOwner();
        return dialog.ShowDialog();
    }

    /// <summary>
    /// Picks the window a dialog should be centred on and modal to. Returns
    /// <c>null</c> during startup, before any window exists.
    /// </summary>
    private static Window? ResolveOwner()
    {
        var application = Application.Current;

        if (application is null)
        {
            return null;
        }

        var active = application.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive && window.IsLoaded);

        if (active is not null)
        {
            return active;
        }

        return application.MainWindow is { IsLoaded: true } main ? main : null;
    }

    /// <summary>
    /// Runs <paramref name="callback"/> on the UI thread. Every dialog method funnels
    /// through here so callers never have to think about which thread they are on.
    /// </summary>
    private Task<T> InvokeAsync<T>(Func<T> callback)
        => _dispatcher.CheckAccess()
            ? Task.FromResult(callback())
            : _dispatcher.InvokeAsync(callback).Task;
}
