using WeighBridge.Core.Mvvm;
using WeighBridge.Domain.Enums;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// One entry in the shell's left navigation panel.
/// </summary>
/// <remarks>
/// Holds the ViewModel type rather than a view, so the panel is a list of destinations
/// the navigation service can reach and the shell never names a view type. Adding a
/// module means adding one of these to <see cref="MainWindowViewModel"/>.
/// </remarks>
public sealed class ShellNavigationItem : ObservableObject
{
    private bool _isSelected;

    public ShellNavigationItem(ApplicationModule module, string label, string iconKey, Type viewModelType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(iconKey);
        ArgumentNullException.ThrowIfNull(viewModelType);

        Module = module;
        Label = label;
        IconKey = iconKey;
        ViewModelType = viewModelType;
    }

    public ApplicationModule Module { get; }

    public string Label { get; }

    /// <summary>Key into <c>Resources/Icons.xaml</c>, resolved by the view.</summary>
    public string IconKey { get; }

    public Type ViewModelType { get; }

    /// <summary>
    /// Set by the shell in response to navigation, so the highlight follows Back and
    /// Forward as well as a click on the panel itself.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
