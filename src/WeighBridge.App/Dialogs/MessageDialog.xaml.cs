using System.Windows;
using System.Windows.Input;

namespace WeighBridge.App.Dialogs;

/// <summary>
/// The single dialog window behind every information, success, warning, error and
/// confirmation prompt in the application.
/// </summary>
public partial class MessageDialog : DialogWindowBase
{
    public MessageDialog(MessageDialogViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => OnDragSurfaceMouseLeftButtonDown(sender, e);

    private void OnConfirmClicked(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnToggleDetailsClicked(object sender, RoutedEventArgs e)
    {
        var isExpanding = DetailsPanel.Visibility != Visibility.Visible;

        DetailsPanel.Visibility = isExpanding ? Visibility.Visible : Visibility.Collapsed;
        DetailsToggle.Content = isExpanding ? "Hide technical details" : "Show technical details";
    }
}
