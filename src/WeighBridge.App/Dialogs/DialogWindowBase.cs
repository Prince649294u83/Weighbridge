using System.Windows;
using System.Windows.Input;

namespace WeighBridge.App.Dialogs;

/// <summary>
/// Shared chrome and behaviour for every custom dialog: borderless transparent window,
/// draggable surface, and Escape-to-dismiss.
/// </summary>
/// <remarks>
/// The application never shows a stock <c>MessageBox</c> or a Win32 common dialog, so
/// all of that chrome is reimplemented here once instead of per dialog.
/// </remarks>
public abstract class DialogWindowBase : Window
{
    protected DialogWindowBase()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = null;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UseLayoutRounding = true;
    }

    /// <summary>
    /// Set to <c>false</c> by modal progress dialogs, which must not be dismissable
    /// while the operation they are covering is still running.
    /// </summary>
    protected bool CanDismissWithEscape { get; set; } = true;

    /// <summary>Drag handler for the dialog surface. Wire to a header's MouseLeftButtonDown.</summary>
    protected void OnDragSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove throws if the button was already released before the message was
            // dispatched. Nothing to recover from - the drag simply did not start.
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && CanDismissWithEscape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // A dialog opened without an owner (during startup, before the shell exists)
        // would land in the top-left corner, so centre it on the screen instead.
        if (Owner is null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }
}
