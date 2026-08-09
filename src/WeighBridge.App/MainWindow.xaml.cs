using System.Windows;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using WeighBridge.App.Navigation;
using WeighBridge.App.ViewModels;
using WeighBridge.Core.Dialogs;

namespace WeighBridge.App;

/// <summary>
/// Code-behind for the application shell.
/// </summary>
/// <remarks>
/// Deliberately thin. Only three things belong here, and all three are window
/// concerns that a ViewModel cannot own without taking a dependency on WPF:
/// the clock tick that drives the title bar, the minimise/maximise/close commands,
/// and handing the view locator to the navigation host. Everything else is a binding.
/// </remarks>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IDialogService _dialogService;
    private readonly ILogger<MainWindow> _logger;
    private readonly DispatcherTimer _clockTimer;

    public MainWindow(
        MainWindowViewModel viewModel,
        IViewLocator viewLocator,
        IDialogService dialogService,
        ILogger<MainWindow> logger)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(viewLocator);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(logger);

        _viewModel = viewModel;
        _dialogService = dialogService;
        _logger = logger;

        InitializeComponent();

        DataContext = viewModel;

        // The host resolves views itself so the shell never names a view type.
        ContentHost.ViewLocator = viewLocator;

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _clockTimer.Tick += OnClockTick;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>
    /// Starts the clock and opens the startup module once the window is on screen, so
    /// a slow first health probe cannot delay the shell becoming visible.
    /// </summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        _viewModel.UpdateClock();
        _clockTimer.Start();

        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            // Swallowed on purpose: an exception escaping an async void handler reaches
            // the dispatcher and would tear the application down during startup. The
            // shell itself is already usable, so report it and leave it standing.
            _logger.LogError(ex, "Shell initialization failed");

            await _dialogService.ShowErrorAsync(
                "Startup was not completed",
                "The application started but could not open the first page. The details have been written to the log file.",
                ex.ToString());
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _clockTimer.Stop();
        _clockTimer.Tick -= OnClockTick;
        Closed -= OnClosed;

        _viewModel.Dispose();
    }

    private void OnClockTick(object? sender, EventArgs e) => _viewModel.UpdateClock();

    private void OnMinimizeClicked(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximizeRestoreClicked(object sender, RoutedEventArgs e)
    {
        // SystemCommands rather than assigning WindowState, so the window animates and
        // reports itself to the shell exactly as the native caption buttons would.
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
            return;
        }

        SystemCommands.MaximizeWindow(this);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
