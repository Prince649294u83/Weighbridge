using System.Windows;
using WeighBridge.App.ViewModels;

namespace WeighBridge.App.Views;

/// <summary>
/// Code-behind for the Report Preview modal window.
/// </summary>
public partial class ReportPreviewWindow : Window
{
    public ReportPreviewWindow(ReportPreviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        viewModel.RequestClose += () => Dispatcher.Invoke(Close);
    }
}
