using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using WeighBridge.App.ViewModels;

namespace WeighBridge.App.Views;

/// <summary>User administration module.</summary>
public partial class AdministrationView : UserControl
{
    public AdministrationView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private AdministrationViewModel? ViewModel => DataContext as AdministrationViewModel;

    /// <summary>
    /// Carries the typed password to the view model. <see cref="PasswordBox.Password"/> is
    /// deliberately not a dependency property, so this cannot be a binding.
    /// </summary>
    private void OnFormPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.FormPassword = FormPasswordBox.Password;
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged previous)
        {
            previous.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is INotifyPropertyChanged current)
        {
            current.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    /// <summary>
    /// The view model clears the password when a form is opened for a new or an existing user,
    /// and the box has to follow it. A box still showing the characters typed for the previous
    /// account claims that account has a password pending when the view model holds none, and
    /// the next keystroke would submit the leftovers along with it.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AdministrationViewModel.FormPassword)
            && string.IsNullOrEmpty(ViewModel?.FormPassword)
            && FormPasswordBox.Password.Length > 0)
        {
            // Assigning raises PasswordChanged, which writes the same empty string back to the
            // view model; SetProperty sees no change there, so this settles in one pass.
            FormPasswordBox.Password = string.Empty;
        }
    }
}
