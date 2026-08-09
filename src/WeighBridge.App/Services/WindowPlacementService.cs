using Microsoft.Extensions.Logging;
using System.Windows;
using WeighBridge.Core.Settings;

namespace WeighBridge.App.Services;

/// <summary>
/// Restores the main window to where the operator left it and records changes back to
/// <see cref="UserPreferences"/>.
/// </summary>
/// <remarks>
/// Only the restored (non-maximised) bounds are stored, so un-maximising after a restart
/// returns the window to a sensible size instead of leaving it filling the screen. The
/// stored rectangle is validated against the current virtual desktop on every restore -
/// monitors get unplugged, and a window placed on a monitor that is no longer there is
/// invisible and unrecoverable without editing the preferences file by hand.
/// </remarks>
public sealed class WindowPlacementService : IWindowPlacementService
{
    /// <summary>Minimum on-screen overlap required for a stored position to be reused.</summary>
    private const double MinimumVisibleExtent = 120d;

    private readonly ISettingsService _settingsService;
    private readonly ILogger<WindowPlacementService> _logger;

    private Window? _window;

    /// <summary>
    /// Guards the tracked snapshot below, which is written on the UI thread and read by
    /// the shutdown sequence on a worker thread.
    /// </summary>
    private readonly object _stateGate = new();

    /// <summary>Restored bounds, tracked separately from the maximised bounds.</summary>
    private Rect _restoreBounds;

    /// <summary>Last known maximised state, excluding minimisation.</summary>
    private bool _isMaximized;

    public WindowPlacementService(ISettingsService settingsService, ILogger<WindowPlacementService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (_window is not null)
        {
            throw new InvalidOperationException("A window is already attached to this placement service.");
        }

        _window = window;

        Restore(window);

        window.LocationChanged += OnWindowBoundsChanged;
        window.SizeChanged += OnWindowBoundsChanged;
        window.StateChanged += OnWindowStateChanged;
        window.Closing += OnWindowClosing;
    }

    /// <inheritdoc />
    public Task SaveAsync()
    {
        if (_window is null)
        {
            return Task.CompletedTask;
        }

        Capture();
        return _settingsService.SaveAsync();
    }

    private void Restore(Window window)
    {
        var placement = _settingsService.Preferences.Window;

        var width = Sanitise(placement.Width, window.Width, window.MinWidth);
        var height = Sanitise(placement.Height, window.Height, window.MinHeight);

        window.Width = width;
        window.Height = height;

        if (placement is { Left: { } storedLeft, Top: { } storedTop } &&
            TryFitToDesktop(storedLeft, storedTop, width, height, out var position))
        {
            // Manual placement has to replace the startup location, otherwise WPF centres
            // the window and discards the coordinates we just applied.
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = position.X;
            window.Top = position.Y;
        }
        else
        {
            if (placement.HasPosition)
            {
                _logger.LogInformation(
                    "Stored window position {Left},{Top} is off-screen; centring instead.",
                    placement.Left,
                    placement.Top);
            }

            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        lock (_stateGate)
        {
            _restoreBounds = new Rect(window.Left, window.Top, width, height);
            _isMaximized = placement.IsMaximized;
        }

        // Applied last so the restore bounds above describe the un-maximised window.
        window.WindowState = placement.IsMaximized ? WindowState.Maximized : WindowState.Normal;

        _logger.LogDebug(
            "Restored window placement: {Width}x{Height}, maximised {IsMaximized}.",
            width,
            height,
            placement.IsMaximized);
    }

    /// <summary>
    /// Copies the tracked geometry into the preferences object without saving.
    /// </summary>
    /// <remarks>
    /// Reads only the snapshot maintained by the event handlers below, never the
    /// <see cref="Window"/> itself. Every geometry property on <see cref="Window"/> is
    /// dispatcher-affine, and this runs on the shutdown worker thread; marshalling back to
    /// the UI thread is not an option either, because that thread is blocked waiting for
    /// the shutdown sequence to finish.
    /// </remarks>
    private void Capture()
    {
        Rect bounds;
        bool isMaximized;

        lock (_stateGate)
        {
            bounds = _restoreBounds;
            isMaximized = _isMaximized;
        }

        var placement = _settingsService.Preferences.Window;
        placement.IsMaximized = isMaximized;

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        placement.Width = bounds.Width;
        placement.Height = bounds.Height;

        // Before the window is shown, Left and Top are NaN under CenterScreen placement.
        // Storing that would record a position we cannot restore, so leave the previous
        // value - or "unset" - in place until a real one is known.
        if (!double.IsNaN(bounds.X) && !double.IsNaN(bounds.Y))
        {
            placement.Left = bounds.X;
            placement.Top = bounds.Y;
        }
    }

    /// <summary>Tracks the un-maximised rectangle as the operator moves and resizes.</summary>
    /// <remarks>
    /// The rectangle comes from <see cref="Window.RestoreBounds"/> rather than from
    /// <c>Left</c>/<c>Top</c>/<c>ActualWidth</c>/<c>ActualHeight</c>. Reading the live
    /// properties is unreliable during a state transition: WPF raises
    /// <see cref="Window.LocationChanged"/> while <see cref="Window.WindowState"/> still
    /// reports <see cref="WindowState.Normal"/>, so the handler sees the *destination*
    /// position of a maximise (a small negative overshoot, the invisible resize border) or
    /// of a minimise (parked around -32000 device pixels) and stores it as though the
    /// operator had moved the window there. Windows already maintains the pre-maximise
    /// rectangle for us, and <c>RestoreBounds</c> surfaces it directly.
    /// </remarks>
    private void OnWindowBoundsChanged(object? sender, EventArgs e)
    {
        if (_window is null)
        {
            return;
        }

        var bounds = _window.RestoreBounds;

        // Empty until the window has been shown; a transition can also briefly report a
        // position no monitor can display. Keeping the previous snapshot is always better
        // than overwriting it with a rectangle we could not restore from.
        if (bounds.IsEmpty || !IsUsablePosition(bounds))
        {
            return;
        }

        lock (_stateGate)
        {
            _restoreBounds = bounds;
        }
    }

    /// <summary>Tracks maximisation without letting a minimise overwrite it.</summary>
    /// <remarks>
    /// Minimised is deliberately ignored: an operator who minimises a maximised window and
    /// then closes it from the taskbar expects it back maximised, not restored.
    /// </remarks>
    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_window is null || _window.WindowState == WindowState.Minimized)
        {
            return;
        }

        var isMaximized = _window.WindowState == WindowState.Maximized;

        lock (_stateGate)
        {
            _isMaximized = isMaximized;
        }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Last chance to read the live window while the UI thread still owns it: the
        // shutdown sequence that follows can only see the snapshot.
        OnWindowBoundsChanged(sender, e);
        OnWindowStateChanged(sender, e);
        Capture();
    }

    /// <summary>The bounding rectangle of every connected monitor.</summary>
    private static Rect GetVirtualDesktop() => new(
        SystemParameters.VirtualScreenLeft,
        SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth,
        SystemParameters.VirtualScreenHeight);

    /// <summary>
    /// True when enough of <paramref name="bounds"/> falls on a connected monitor for the
    /// operator to see and grab the window.
    /// </summary>
    private static bool IsUsablePosition(Rect bounds)
    {
        if (double.IsNaN(bounds.X) || double.IsNaN(bounds.Y) || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        var desktop = GetVirtualDesktop();

        if (desktop.Width <= 0 || desktop.Height <= 0)
        {
            return false;
        }

        var visible = Rect.Intersect(bounds, desktop);

        return !visible.IsEmpty
            && visible.Width >= MinimumVisibleExtent
            && visible.Height >= MinimumVisibleExtent;
    }

    /// <summary>
    /// Returns a position that keeps a usable portion of the window on a connected
    /// monitor, or <c>false</c> when the stored rectangle is unusable.
    /// </summary>
    private static bool TryFitToDesktop(double left, double top, double width, double height, out Point position)
    {
        position = default;

        if (!IsUsablePosition(new Rect(left, top, width, height)))
        {
            return false;
        }

        var desktop = GetVirtualDesktop();

        // A title bar dragged above the desktop cannot be grabbed again, so nudge the
        // window fully inside rather than rejecting an otherwise valid position.
        var x = Math.Clamp(left, desktop.Left, Math.Max(desktop.Left, desktop.Right - width));
        var y = Math.Clamp(top, desktop.Top, Math.Max(desktop.Top, desktop.Bottom - height));

        position = new Point(x, y);
        return true;
    }

    /// <summary>Falls back through stored value, window value and minimum.</summary>
    private static double Sanitise(double stored, double current, double minimum)
    {
        var floor = double.IsNaN(minimum) || minimum <= 0 ? 1d : minimum;

        if (!double.IsNaN(stored) && stored >= floor)
        {
            return stored;
        }

        return !double.IsNaN(current) && current >= floor ? current : floor;
    }
}
