using System.Windows;
using System.Windows.Input;

namespace WeighBridge.App.Dialogs;

public partial class LoginDialog : DialogWindowBase
{
    private readonly LoginDialogViewModel _viewModel;

    public LoginDialog(LoginDialogViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;
        
        _viewModel.RequestClose = () =>
        {
            DialogResult = true;
            Close();
        };
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => OnDragSurfaceMouseLeftButtonDown(sender, e);

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnLoginClicked(object sender, RoutedEventArgs e)
    {
        var submission = new LoginSubmission(PasswordBox.Password, ConfirmPasswordBox.Password);

        if (_viewModel.SubmitCommand.CanExecute(submission))
        {
            _viewModel.SubmitCommand.Execute(submission);
        }
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        // Re-evaluate CanExecute when password changes
        if (_viewModel.SubmitCommand is WeighBridge.Core.Mvvm.AsyncRelayCommand<LoginSubmission> cmd)
        {
            cmd.NotifyCanExecuteChanged();
        }
        _viewModel.ErrorMessage = string.Empty;
    }
}
