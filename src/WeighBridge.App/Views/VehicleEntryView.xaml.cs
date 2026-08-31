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
}
