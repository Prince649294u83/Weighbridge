using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Navigation;

namespace WeighBridge.Services.Navigation;

/// <summary>
/// Navigation service for the single-window shell.
/// </summary>
/// <remarks>
/// ViewModels are resolved from the container on each navigation, so a module always
/// receives its dependencies through constructor injection. History is kept as two
/// stacks; navigating to a new destination clears the forward stack, matching the
/// behaviour of a browser and of the legacy application's back button.
/// <para>
/// The service is deliberately free of WPF references: the shell subscribes to
/// <see cref="Navigated"/> and swaps its content, which keeps this logic testable.
/// </para>
/// </remarks>
public sealed class NavigationService(IServiceProvider services, ILogger<NavigationService> logger) : INavigationService
{
    /// <summary>Upper bound on retained history entries, guarding against unbounded growth.</summary>
    private const int MaxHistoryDepth = 50;

    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));
    private readonly ILogger<NavigationService> _logger = logger;

    private readonly List<NavigationEntry> _backStack = [];
    private readonly List<NavigationEntry> _forwardStack = [];

    private NavigationEntry? _current;
    private bool _isNavigating;

    /// <inheritdoc />
    public ViewModelBase? CurrentViewModel => _current?.ViewModel;

    /// <inheritdoc />
    public bool CanGoBack => _backStack.Count > 0;

    /// <inheritdoc />
    public bool CanGoForward => _forwardStack.Count > 0;

    /// <inheritdoc />
    public event EventHandler<NavigatedEventArgs>? Navigated;

    /// <inheritdoc />
    public Task<bool> NavigateToAsync<TViewModel>(NavigationContext? context = null)
        where TViewModel : ViewModelBase
        => NavigateToAsync(typeof(TViewModel), context);

    /// <inheritdoc />
    public async Task<bool> NavigateToAsync(Type viewModelType, NavigationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);

        if (!typeof(ViewModelBase).IsAssignableFrom(viewModelType))
        {
            throw new ArgumentException(
                $"{viewModelType.Name} must derive from {nameof(ViewModelBase)}.",
                nameof(viewModelType));
        }

        // Re-entrancy guard: a double-click on a navigation item must not run two
        // navigations concurrently.
        if (_isNavigating)
        {
            _logger.LogDebug("Navigation to {ViewModel} ignored: a navigation is already in progress", viewModelType.Name);
            return false;
        }

        var effectiveContext = context ?? NavigationContext.Empty;

        _isNavigating = true;

        try
        {
            if (!await CanLeaveCurrentAsync().ConfigureAwait(true))
            {
                _logger.LogInformation("Navigation to {ViewModel} was cancelled by {Current}", viewModelType.Name, _current?.ViewModelType.Name);
                return false;
            }

            var viewModel = (ViewModelBase)ActivatorUtilities.GetServiceOrCreateInstance(_services, viewModelType);
            var entry = new NavigationEntry(viewModelType, viewModel, effectiveContext);

            if (_current is not null)
            {
                PushHistory(_backStack, _current);
                _forwardStack.Clear();
            }

            await ActivateAsync(entry, effectiveContext).ConfigureAwait(true);

            _logger.LogInformation("Navigated to {ViewModel}", viewModelType.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Navigation to {ViewModel} failed", viewModelType.Name);
            throw;
        }
        finally
        {
            _isNavigating = false;
        }
    }

    /// <inheritdoc />
    public Task<bool> GoBackAsync() => MoveThroughHistoryAsync(_backStack, _forwardStack, "back");

    /// <inheritdoc />
    public Task<bool> GoForwardAsync() => MoveThroughHistoryAsync(_forwardStack, _backStack, "forward");

    /// <inheritdoc />
    public async Task<bool> RefreshAsync()
    {
        if (_current is null)
        {
            return false;
        }

        var refreshContext = new NavigationContext(_current.Context.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value))
        {
            IsRefresh = true,
        };

        // A refresh rebuilds the ViewModel so any cached state is discarded, which is
        // what an operator expects from a Refresh action.
        var viewModel = (ViewModelBase)ActivatorUtilities.GetServiceOrCreateInstance(_services, _current.ViewModelType);
        var entry = new NavigationEntry(_current.ViewModelType, viewModel, refreshContext);

        await ActivateAsync(entry, refreshContext).ConfigureAwait(true);

        _logger.LogInformation("Refreshed {ViewModel}", entry.ViewModelType.Name);
        return true;
    }

    /// <inheritdoc />
    public void ClearHistory()
    {
        _backStack.Clear();
        _forwardStack.Clear();
        _logger.LogDebug("Navigation history cleared");
    }

    /// <summary>Shared implementation of back and forward traversal.</summary>
    private async Task<bool> MoveThroughHistoryAsync(
        List<NavigationEntry> source,
        List<NavigationEntry> destination,
        string direction)
    {
        if (source.Count == 0 || _isNavigating)
        {
            return false;
        }

        _isNavigating = true;

        try
        {
            if (!await CanLeaveCurrentAsync().ConfigureAwait(true))
            {
                return false;
            }

            var entry = source[^1];
            source.RemoveAt(source.Count - 1);

            if (_current is not null)
            {
                PushHistory(destination, _current);
            }

            var historyContext = new NavigationContext(entry.Context.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value))
            {
                IsHistoryNavigation = true,
            };

            await ActivateAsync(entry, historyContext).ConfigureAwait(true);

            _logger.LogInformation("Navigated {Direction} to {ViewModel}", direction, entry.ViewModelType.Name);
            return true;
        }
        finally
        {
            _isNavigating = false;
        }
    }

    /// <summary>Asks the outgoing ViewModel for permission to leave.</summary>
    private async Task<bool> CanLeaveCurrentAsync()
        => _current is null || await _current.ViewModel.CanNavigateAwayAsync().ConfigureAwait(true);

    /// <summary>
    /// Makes <paramref name="entry"/> current, running the lifecycle callbacks and
    /// raising <see cref="Navigated"/>.
    /// </summary>
    private async Task ActivateAsync(NavigationEntry entry, NavigationContext context)
    {
        var previous = _current?.ViewModel;

        if (previous is not null)
        {
            await previous.OnNavigatedFromAsync().ConfigureAwait(true);
        }

        _current = entry;

        Navigated?.Invoke(this, new NavigatedEventArgs(previous, entry.ViewModel, context));

        await entry.ViewModel.OnNavigatedToAsync(context).ConfigureAwait(true);
    }

    /// <summary>Pushes an entry, trimming the oldest when the cap is exceeded.</summary>
    private static void PushHistory(List<NavigationEntry> stack, NavigationEntry entry)
    {
        stack.Add(entry);

        if (stack.Count > MaxHistoryDepth)
        {
            stack.RemoveAt(0);
        }
    }

    /// <summary>One position in the navigation history.</summary>
    private sealed record NavigationEntry(Type ViewModelType, ViewModelBase ViewModel, NavigationContext Context);
}
