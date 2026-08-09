using WeighBridge.Core.Mvvm;

namespace WeighBridge.Core.Navigation;

/// <summary>
/// Navigates the single main window between ViewModels. There is no window
/// switching anywhere in the application: every module is hosted in the shell's
/// content area.
/// </summary>
public interface INavigationService
{
    /// <summary>The ViewModel currently displayed in the content area.</summary>
    ViewModelBase? CurrentViewModel { get; }

    /// <summary>True when <see cref="GoBackAsync"/> can succeed.</summary>
    bool CanGoBack { get; }

    /// <summary>True when <see cref="GoForwardAsync"/> can succeed.</summary>
    bool CanGoForward { get; }

    /// <summary>Raised after the active ViewModel changed.</summary>
    event EventHandler<NavigatedEventArgs>? Navigated;

    /// <summary>Navigates to the ViewModel of type <typeparamref name="TViewModel"/>.</summary>
    Task<bool> NavigateToAsync<TViewModel>(NavigationContext? context = null)
        where TViewModel : ViewModelBase;

    /// <summary>Navigates to the ViewModel identified by <paramref name="viewModelType"/>.</summary>
    Task<bool> NavigateToAsync(Type viewModelType, NavigationContext? context = null);

    /// <summary>Moves one entry back in the navigation history.</summary>
    Task<bool> GoBackAsync();

    /// <summary>Moves one entry forward in the navigation history.</summary>
    Task<bool> GoForwardAsync();

    /// <summary>Re-navigates to the current entry, re-running its load logic.</summary>
    Task<bool> RefreshAsync();

    /// <summary>Clears the back/forward history without changing the current view.</summary>
    void ClearHistory();
}

/// <summary>Payload of <see cref="INavigationService.Navigated"/>.</summary>
public sealed class NavigatedEventArgs(ViewModelBase? previous, ViewModelBase current, NavigationContext context)
    : EventArgs
{
    /// <summary>The ViewModel that was displayed before the navigation, if any.</summary>
    public ViewModelBase? Previous { get; } = previous;

    /// <summary>The ViewModel that is now displayed.</summary>
    public ViewModelBase Current { get; } = current;

    /// <summary>The context that was passed to the new ViewModel.</summary>
    public NavigationContext Context { get; } = context;
}
