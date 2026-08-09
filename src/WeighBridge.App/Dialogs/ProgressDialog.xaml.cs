using System.Windows;

namespace WeighBridge.App.Dialogs;

/// <summary>
/// Modal dialog shown while a long-running operation is in flight. Serves both the
/// determinate progress case and the indeterminate loading case.
/// </summary>
public partial class ProgressDialog : DialogWindowBase
{
    private bool _isOperationComplete;

    public ProgressDialog(ProgressDialogViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;

        // The operator must not be able to dismiss the dialog out from under the
        // operation - the only way out is completion or the cancel button.
        CanDismissWithEscape = false;
    }

    /// <summary>
    /// Closes the dialog once the covered operation has ended. Safe to call from any
    /// thread and before the window has been shown.
    /// </summary>
    public void CompleteAndClose()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(CompleteAndClose);
            return;
        }

        _isOperationComplete = true;

        // ShowDialog has not necessarily returned control to the caller yet when the
        // operation finishes synchronously, so guard against closing an unloaded window.
        if (IsLoaded)
        {
            Close();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Alt+F4 and the system menu would otherwise leave the operation running with
        // no visible indication that anything is still happening.
        e.Cancel = !_isOperationComplete;

        base.OnClosing(e);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        // The operation can complete before the window finishes rendering; close now
        // rather than stranding a dialog with nothing behind it.
        if (_isOperationComplete)
        {
            Close();
        }
    }
}
