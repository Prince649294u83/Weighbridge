using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WeighBridge.App.Dialogs;

public partial class LoginDialog : DialogWindowBase
{
    private readonly LoginDialogViewModel _viewModel;
    private bool _isPasswordVisible;
    private bool _isConfirmPasswordVisible;

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
        var password = _isPasswordVisible ? VisiblePasswordBox.Text : PasswordBox.Password;
        var confirm = _isConfirmPasswordVisible ? VisibleConfirmPasswordBox.Text : ConfirmPasswordBox.Password;
        var submission = new LoginSubmission(password, confirm);

        if (_viewModel.SubmitCommand.CanExecute(submission))
        {
            _viewModel.SubmitCommand.Execute(submission);
        }
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isPasswordVisible && VisiblePasswordBox != null)
        {
            VisiblePasswordBox.Text = PasswordBox.Password;
        }

        NotifyCanExecuteChanged();
    }

    private void OnVisiblePasswordChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isPasswordVisible && PasswordBox != null)
        {
            PasswordBox.Password = VisiblePasswordBox.Text;
        }

        NotifyCanExecuteChanged();
    }

    private void OnConfirmPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isConfirmPasswordVisible && VisibleConfirmPasswordBox != null)
        {
            VisibleConfirmPasswordBox.Text = ConfirmPasswordBox.Password;
        }

        NotifyCanExecuteChanged();
    }

    private void OnVisibleConfirmPasswordChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isConfirmPasswordVisible && ConfirmPasswordBox != null)
        {
            ConfirmPasswordBox.Password = VisibleConfirmPasswordBox.Text;
        }

        NotifyCanExecuteChanged();
    }

    private void OnTogglePasswordVisibilityClicked(object sender, RoutedEventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;
        if (_isPasswordVisible)
        {
            VisiblePasswordBox.Text = PasswordBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            VisiblePasswordBox.Visibility = Visibility.Visible;
            if (FindResource("Geometry.EyeSlash") is Geometry eyeSlash)
            {
                PasswordEyeIcon.Data = eyeSlash;
            }
            VisiblePasswordBox.Focus();
            VisiblePasswordBox.CaretIndex = VisiblePasswordBox.Text.Length;
        }
        else
        {
            PasswordBox.Password = VisiblePasswordBox.Text;
            VisiblePasswordBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            if (FindResource("Geometry.Eye") is Geometry eye)
            {
                PasswordEyeIcon.Data = eye;
            }
            PasswordBox.Focus();
        }
    }

    private void OnToggleConfirmPasswordVisibilityClicked(object sender, RoutedEventArgs e)
    {
        _isConfirmPasswordVisible = !_isConfirmPasswordVisible;
        if (_isConfirmPasswordVisible)
        {
            VisibleConfirmPasswordBox.Text = ConfirmPasswordBox.Password;
            ConfirmPasswordBox.Visibility = Visibility.Collapsed;
            VisibleConfirmPasswordBox.Visibility = Visibility.Visible;
            if (FindResource("Geometry.EyeSlash") is Geometry eyeSlash)
            {
                ConfirmPasswordEyeIcon.Data = eyeSlash;
            }
            VisibleConfirmPasswordBox.Focus();
            VisibleConfirmPasswordBox.CaretIndex = VisibleConfirmPasswordBox.Text.Length;
        }
        else
        {
            ConfirmPasswordBox.Password = VisibleConfirmPasswordBox.Text;
            VisibleConfirmPasswordBox.Visibility = Visibility.Collapsed;
            ConfirmPasswordBox.Visibility = Visibility.Visible;
            if (FindResource("Geometry.Eye") is Geometry eye)
            {
                ConfirmPasswordEyeIcon.Data = eye;
            }
            ConfirmPasswordBox.Focus();
        }
    }

    private void NotifyCanExecuteChanged()
    {
        if (_viewModel.SubmitCommand is WeighBridge.Core.Mvvm.AsyncRelayCommand<LoginSubmission> cmd)
        {
            cmd.NotifyCanExecuteChanged();
        }
        _viewModel.ErrorMessage = string.Empty;
    }
}
