using WeighBridge.Core.Navigation;

namespace WeighBridge.Core.Mvvm;

/// <summary>
/// Base class for every ViewModel. Provides the presentation state that the shell
/// needs (title, description, busy indication) and default no-op implementations of
/// the navigation lifecycle so derived ViewModels only override what they use.
/// </summary>
public abstract class ViewModelBase : ObservableObject, INavigationAware
{
    private string _title = string.Empty;
    private string _description = string.Empty;
    private bool _isBusy;
    private string? _busyMessage;

    /// <summary>Module title rendered in the content header.</summary>
    public string Title
    {
        get => _title;
        protected set => SetProperty(ref _title, value);
    }

    /// <summary>Short explanation of the module's purpose.</summary>
    public string Description
    {
        get => _description;
        protected set => SetProperty(ref _description, value);
    }

    /// <summary>True while a long-running operation blocks interaction.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        protected set => SetProperty(ref _isBusy, value);
    }

    /// <summary>Optional message shown next to the loading indicator.</summary>
    public string? BusyMessage
    {
        get => _busyMessage;
        protected set => SetProperty(ref _busyMessage, value);
    }

    /// <inheritdoc />
    public virtual Task OnNavigatedToAsync(NavigationContext context) => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task<bool> CanNavigateAwayAsync() => Task.FromResult(true);

    /// <inheritdoc />
    public virtual Task OnNavigatedFromAsync() => Task.CompletedTask;

    /// <summary>
    /// Runs <paramref name="operation"/> with <see cref="IsBusy"/> raised, guaranteeing
    /// the flag is cleared even when the operation throws.
    /// </summary>
    protected async Task RunBusyAsync(Func<Task> operation, string? busyMessage = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        IsBusy = true;
        BusyMessage = busyMessage;

        try
        {
            await operation().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            BusyMessage = null;
        }
    }
}
