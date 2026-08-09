namespace WeighBridge.Core.Navigation;

/// <summary>
/// Implemented by ViewModels that participate in the navigation lifecycle.
/// </summary>
public interface INavigationAware
{
    /// <summary>Called after the ViewModel became the active content.</summary>
    Task OnNavigatedToAsync(NavigationContext context);

    /// <summary>
    /// Called before navigating away. Returning <c>false</c> cancels the navigation —
    /// this is how unsaved-changes prompts will be implemented in later modules.
    /// </summary>
    Task<bool> CanNavigateAwayAsync();

    /// <summary>Called after the ViewModel stopped being the active content.</summary>
    Task OnNavigatedFromAsync();
}
