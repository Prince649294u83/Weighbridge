using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WeighBridge.App.Dialogs;
using WeighBridge.Core.Dialogs;

namespace WeighBridge.App;

/// <summary>
/// WPF application entry point.
/// </summary>
/// <remarks>
/// <para>
/// Owns three things and nothing else: the <see cref="Bootstrapper"/> that composes the
/// application, the global exception handlers, and the shutdown sequence. There is no
/// business logic and no service resolution beyond the logger and dialog service the
/// exception handlers need.
/// </para>
/// <para>
/// The handlers are attached in the constructor - before any startup work runs - so a
/// failure inside <see cref="OnStartup"/> is reported rather than silently killing the
/// process.
/// </para>
/// </remarks>
public partial class App : Application
{
    private readonly Bootstrapper _bootstrapper = new();

    private ILogger<App>? _logger;
    private IDialogService? _dialogService;

    /// <summary>Guards against an exception raised while reporting an exception.</summary>
    private bool _isReportingFailure;

    private bool _hasShutDown;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // The shell is the application: closing it ends the process, and no dialog
        // opened along the way can keep it alive.
        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }

    /// <inheritdoc />
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            await _bootstrapper.StartAsync();

            _logger = _bootstrapper.Services.GetRequiredService<ILogger<App>>();
            _dialogService = _bootstrapper.Services.GetRequiredService<IDialogService>();

            var shell = _bootstrapper.CreateShell();
            MainWindow = shell;
            shell.Show();

            _logger.LogInformation("Application shell displayed; startup complete");

            // After the window is on screen: a database on a slow network share must not
            // delay the operator seeing the application.
            _ = InitializeDatabaseAsync();
        }
        catch (Exception ex)
        {
            await ReportStartupFailureAsync(ex);
            Shutdown(-1);
        }
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        RunShutdown();
        base.OnExit(e);
    }

    /// <inheritdoc />
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Windows is logging off or restarting. It will not wait long, so state is
        // persisted here rather than relying on OnExit being reached.
        _logger?.LogInformation("Windows session is ending ({Reason})", e.ReasonSessionEnding);
        RunShutdown();

        base.OnSessionEnding(e);
    }

    /// <summary>
    /// Runs database preparation off the startup path, reporting failure to the status
    /// bar instead of to the operator: a missing database is a condition to display, not
    /// an error to interrupt with.
    /// </summary>
    private async Task InitializeDatabaseAsync()
    {
        try
        {
            await _bootstrapper.InitializeDatabaseAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Background database initialisation failed");
        }
    }

    /// <summary>
    /// Persists state and flushes the log exactly once, however shutdown was reached.
    /// </summary>
    private void RunShutdown()
    {
        if (_hasShutDown)
        {
            return;
        }

        _hasShutDown = true;

        try
        {
            // Blocking is correct here: the process is ending and the work must finish
            // before it does. ShutdownAsync never resumes on the UI thread, so this
            // cannot deadlock the dispatcher.
            _bootstrapper.ShutdownAsync().GetAwaiter().GetResult();
            _bootstrapper.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Shutdown failed");
        }
    }

    /// <summary>
    /// Handles an exception that reached the UI message loop.
    /// </summary>
    /// <remarks>
    /// Marked handled in every case. An unhandled dispatcher exception terminates the
    /// process, and a weighbridge operator mid-transaction losing the application is a
    /// worse outcome than the application continuing in a slightly unknown state. The
    /// exception is fully logged, so the fault is never hidden from support.
    /// </remarks>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogCritical(e.Exception, "Unhandled exception on the UI thread");

        e.Handled = true;

        ReportUnexpectedFailure(
            "Something went wrong",
            "An unexpected problem occurred. The application is still running, and the details have been written to the log file.",
            e.Exception);
    }

    /// <summary>
    /// Last chance to record an exception from a non-UI thread.
    /// </summary>
    /// <remarks>
    /// The runtime is already committed to terminating when <c>IsTerminating</c> is set;
    /// nothing can stop it. The only useful action is to get the exception onto disk
    /// before the log writer's thread dies with the process.
    /// </remarks>
    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;

        _logger?.LogCritical(
            exception,
            "Unhandled exception on a background thread (terminating: {IsTerminating})",
            e.IsTerminating);

        if (e.IsTerminating)
        {
            RunShutdown();
        }
    }

    /// <summary>
    /// Handles a faulted <see cref="Task"/> whose exception was never awaited.
    /// </summary>
    /// <remarks>
    /// Observed so the finalizer does not escalate it to a process-level crash. These are
    /// usually a fire-and-forget save or health probe, which is exactly the kind of
    /// failure that should be logged and not shown.
    /// </remarks>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }

    /// <summary>
    /// Shows a failure without ever letting the report itself become a second failure.
    /// </summary>
    private void ReportUnexpectedFailure(string title, string message, Exception exception)
    {
        if (_isReportingFailure || _dialogService is null)
        {
            return;
        }

        _isReportingFailure = true;

        // Not awaited: the handler must return so the dispatcher can pump the modal
        // dialog's own message loop.
        _ = ShowAsync();

        async Task ShowAsync()
        {
            try
            {
                await _dialogService.ShowErrorAsync(title, message, exception.ToString());
            }
            catch (Exception dialogFailure)
            {
                _logger?.LogError(dialogFailure, "Failed to display the error dialog");
            }
            finally
            {
                _isReportingFailure = false;
            }
        }
    }

    /// <summary>
    /// Reports a failure that happened before the container existed.
    /// </summary>
    /// <remarks>
    /// The dialog service normally comes from the container, which may be exactly what
    /// failed to build - so one is constructed by hand against a null logger. The
    /// application's own dialog is still used rather than <c>MessageBox</c>: a broken
    /// startup is the first thing an operator sees, and it should look like the product.
    /// </remarks>
    private async Task ReportStartupFailureAsync(Exception exception)
    {
        _logger?.LogCritical(exception, "Application startup failed");

        try
        {
            var dialogs = _dialogService ?? new DialogService(NullLogger<DialogService>.Instance);

            await dialogs.ShowErrorAsync(
                "WeighBridge could not start",
                "The application could not complete its startup and will close. Please contact support with the log file.",
                exception.ToString());
        }
        catch (Exception dialogFailure)
        {
            _logger?.LogCritical(dialogFailure, "Failed to display the startup failure dialog");
        }

        RunShutdown();
    }
}
