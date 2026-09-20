using System.Windows.Controls;
using WeighBridge.App.ViewModels;

namespace WeighBridge.App.Views;

/// <summary>Settings module view.</summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void DiagnosticConsole_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && vm.DiagnosticAutoScroll && sender is TextBox tb)
        {
            tb.CaretIndex = tb.Text.Length;
            tb.ScrollToEnd();
        }
    }
}

