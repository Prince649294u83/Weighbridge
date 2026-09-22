using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        if (DataContext is not VehicleEntryViewModel vm)
        {
            return;
        }

        // 1. Print Modal keyboard shortcuts
        if (vm.IsPrintModalOpen)
        {
            if (e.Key == Key.Enter)
            {
                if (vm.ConfirmPrintCommand.CanExecute(null))
                {
                    vm.ConfirmPrintCommand.Execute(null);
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                if (vm.CancelPrintCommand.CanExecute(null))
                {
                    vm.CancelPrintCommand.Execute(null);
                }
                e.Handled = true;
                return;
            }

            if (e.Key is Key.D1 or Key.NumPad1)
            {
                vm.PrintCopies = 1;
                e.Handled = true;
                return;
            }

            if (e.Key is Key.D2 or Key.NumPad2)
            {
                vm.PrintCopies = 2;
                e.Handled = true;
                return;
            }

            if (e.Key is Key.D3 or Key.NumPad3)
            {
                vm.PrintCopies = 3;
                e.Handled = true;
                return;
            }

            return;
        }

        // 2. GTMA Row single-key hotkeys (G, T, M, A)
        var focusedDep = Keyboard.FocusedElement as DependencyObject;
        bool isGtmaFocused = IsDescendantOf(focusedDep, GtmaButtonsGrid);

        if (isGtmaFocused)
        {
            if (e.Key == Key.G)
            {
                if (vm.SelectGrossModeCommand.CanExecute(null)) vm.SelectGrossModeCommand.Execute(null);
                BtnGrossMode?.Focus();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.T)
            {
                if (vm.SelectTareModeCommand.CanExecute(null)) vm.SelectTareModeCommand.Execute(null);
                BtnTareMode?.Focus();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.M)
            {
                if (vm.SelectManualTareModeCommand.CanExecute(null)) vm.SelectManualTareModeCommand.Execute(null);
                BtnManualTareMode?.Focus();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.A)
            {
                if (vm.SelectAutoTareModeCommand.CanExecute(null)) vm.SelectAutoTareModeCommand.Execute(null);
                BtnAutoTareMode?.Focus();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter)
            {
                if (vm.IsManualTareModeSelected)
                {
                    TareWeightBox?.Focus();
                }
                else
                {
                    SubmitButton?.Focus();
                }
                e.Handled = true;
                return;
            }
        }

        // 3. Enter key navigation through form
        if (e.Key == Key.Enter)
        {
            var focused = Keyboard.FocusedElement as UIElement;
            if (focused is not null and not Button)
            {
                // If pressing Enter in Charges, move directly to GTMA buttons row in F1 mode
                if (focused == ChargesTextBox || (focused is TextBox tb && tb.Name == "ChargesTextBox"))
                {
                    if (vm.IsF1Mode && BtnGrossMode is not null)
                    {
                        BtnGrossMode.Focus();
                        e.Handled = true;
                        return;
                    }
                }

                // If pressing Enter in TareWeightBox (manual tare entered), move directly to Submit button
                if (focused == TareWeightBox && SubmitButton is not null)
                {
                    SubmitButton.Focus();
                    e.Handled = true;
                    return;
                }

                focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
        }
    }

    private static bool IsDescendantOf(DependencyObject? element, DependencyObject? parent)
    {
        if (element is null || parent is null) return false;
        if (ReferenceEquals(element, parent)) return true;
        var current = element;
        while (current != null)
        {
            if (ReferenceEquals(current, parent)) return true;
            current = (current is Visual) ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }
}
