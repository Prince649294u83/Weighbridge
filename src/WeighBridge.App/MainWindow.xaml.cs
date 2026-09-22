using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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
///
/// Also handles WM_GETMINMAXINFO so that a maximised borderless window is constrained
/// exactly to the current monitor's work area, preventing content from being clipped
/// behind the taskbar on any screen size or DPI.
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
        _viewModel.SignOutRequested += OnSignOutRequested;
    }

    /// <summary>
    /// True when this window closed because the operator signed out, rather than because
    /// they finished with the application.
    /// </summary>
    /// <remarks>
    /// Read by <see cref="App"/> after the window has closed. A flag rather than an event
    /// because it must be readable at a known point — a second subscriber to the ViewModel's
    /// event would run in registration order and could be reached after
    /// <see cref="Window.Closed"/> had already fired.
    /// </remarks>
    public bool SignOutRequested { get; private set; }

    private void OnSignOutRequested(object? sender, EventArgs e)
    {
        SignOutRequested = true;
        Close();
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
        _viewModel.SignOutRequested -= OnSignOutRequested;

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

    // =========================================================================
    //  WM_GETMINMAXINFO — constrain maximised window to the monitor's work area
    // =========================================================================

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwndSource = (HwndSource)PresentationSource.FromVisual(this)!;
        hwndSource.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // WM_GETMINMAXINFO = 0x0024
        if (msg == 0x0024)
        {
            WmGetMinMaxInfo(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

        // Find the monitor this window is mostly on.
        const int MONITOR_DEFAULTTONEAREST = 0x00000002;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };

            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                // rcWork is the usable area (excluding taskbar) in device pixels.
                var workArea = monitorInfo.rcWork;
                var monitorArea = monitorInfo.rcMonitor;

                // MaxPosition is relative to the monitor's top-left corner.
                mmi.ptMaxPosition.x = Math.Abs(workArea.left - monitorArea.left);
                mmi.ptMaxPosition.y = Math.Abs(workArea.top - monitorArea.top);
                mmi.ptMaxSize.x = Math.Abs(workArea.right - workArea.left);
                mmi.ptMaxSize.y = Math.Abs(workArea.bottom - workArea.top);

                // Prevent Windows from clamping the maximized size to ptMinTrackSize
                // if the work area happens to be smaller than the default minimum tracking size.
                mmi.ptMinTrackSize.x = Math.Min(mmi.ptMinTrackSize.x, mmi.ptMaxSize.x);
                mmi.ptMinTrackSize.y = Math.Min(mmi.ptMinTrackSize.y, mmi.ptMaxSize.y);
                mmi.ptMaxTrackSize.x = mmi.ptMaxSize.x;
                mmi.ptMaxTrackSize.y = mmi.ptMaxSize.y;
            }
        }

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    // =========================================================================
    //  Win32 P/Invoke declarations
    // =========================================================================

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }
}
