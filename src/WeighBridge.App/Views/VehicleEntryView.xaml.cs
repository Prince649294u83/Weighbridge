using System.Windows.Controls;
using System.Windows.Input;
using WeighBridge.App.ViewModels;

namespace WeighBridge.App.Views;

/// <summary>
/// The weighbridge operator's screen view.
/// </summary>
public partial class VehicleEntryView : UserControl
{
    public VehicleEntryView()
    {
        InitializeComponent();
    }

    private void OnWaitingRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is VehicleEntryViewModel vm && vm.SelectPendingTransactionCommand.CanExecute(null))
        {
            vm.SelectPendingTransactionCommand.Execute(null);
        }
    }

    private void OnFormPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var focused = Keyboard.FocusedElement as System.Windows.UIElement;
            if (focused is not null and not Button)
            {
                focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
        }
    }
}
