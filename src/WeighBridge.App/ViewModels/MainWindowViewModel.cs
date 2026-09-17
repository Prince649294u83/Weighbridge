using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Application;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Dialogs;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Navigation;
using WeighBridge.Core.Security;
using WeighBridge.Core.Settings;
using WeighBridge.Core.Status;
using WeighBridge.Core.Theming;
using WeighBridge.Domain.Enums;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// Drives the application shell: the title bar, the left navigation panel, the content
/// area and the status bar.
/// </summary>
/// <remarks>
/// The shell owns no module behaviour. It resolves the destination a navigation item
/// names, hands it to <see cref="INavigationService"/> and reflects the outcome â€” so a
/// module is added by registering a ViewModel and listing it here, with no change to
/// how the shell works.
/// </remarks>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly INavigationService _navigationService;
    private readonly ISystemStatusService _statusService;
    private readonly IThemeService _themeService;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly IApplicationInfoService _applicationInfo;
    private readonly IDatabaseInitializer _databaseInitializer;
    private readonly IAuthenticationService _authentication;
    private readonly IDateTimeFormatter _dateTimeFormatter;
    private readonly ApplicationOptions _applicationOptions;
    private readonly ILogger<MainWindowViewModel> _logger;

    private readonly RelayCommand _goBackCommand;
    private readonly RelayCommand _goForwardCommand;

    private ShellNavigationItem? _selectedItem;
    private ViewModelBase? _currentViewModel;
    private bool _isNavigationCollapsed;
    private DateTime _now = DateTime.Now;
    private bool _disposed;

    public MainWindowViewModel(
        INavigationService navigationService,
        ISystemStatusService statusService,
        IThemeService themeService,
        ISettingsService settingsService,
        IDialogService dialogService,
        IApplicationInfoService applicationInfo,
        IDatabaseInitializer databaseInitializer,
        IPermissionService permissions,
        IAuthenticationService authentication,
        IDateTimeFormatter dateTimeFormatter,
        IOptions<ApplicationOptions> applicationOptions,
        ILogger<MainWindowViewModel> logger)
    {
        _navigationService = navigationService;
        _statusService = statusService;
        _themeService = themeService;
        _settingsService = settingsService;
        _dialogService = dialogService;
        _applicationInfo = applicationInfo;
        _databaseInitializer = databaseInitializer;
        _authentication = authentication;
        _dateTimeFormatter = dateTimeFormatter;
        _applicationOptions = applicationOptions.Value;
        _logger = logger;

        Title = _applicationOptions.Name;
        CurrentUserName = permissions.CurrentOperator.DisplayName;

        NavigationItems =
        [
            new ShellNavigationItem(ApplicationModule.Dashboard, "Dashboard", "Icon.Dashboard", typeof(DashboardViewModel)),
            new ShellNavigationItem(ApplicationModule.VehicleEntry, "Vehicle Entry", "Icon.VehicleEntry", typeof(VehicleEntryViewModel)),
            new ShellNavigationItem(ApplicationModule.DuplicateSlip, "Duplicate Slip", "Icon.DuplicateSlip", typeof(DuplicateSlipViewModel)),
            new ShellNavigationItem(ApplicationModule.Reports, "Reports", "Icon.Reports", typeof(ReportsViewModel)),
            new ShellNavigationItem(ApplicationModule.Masters, "Masters", "Icon.Masters", typeof(MastersViewModel)),
            new ShellNavigationItem(ApplicationModule.Settings, "Settings", "Icon.Settings", typeof(SettingsViewModel)),
        ];

        // Administration is user management and nothing else, so an operator without
        // UsersManage has no reason to reach it. Evaluated once because the shell is only
        // built after a successful login; the enforcement that actually stops a write is
        // AdministrationViewModel.AuthoriseUserManagementAsync, not this list.
        if (permissions.HasPermission(Permissions.UsersManage))
        {
            NavigationItems.Add(new ShellNavigationItem(
                ApplicationModule.Administration, "Administration", "Icon.Administration", typeof(AdministrationViewModel)));
        }

        _isNavigationCollapsed = _settingsService.Preferences.IsNavigationCollapsed;

        NavigateCommand = new AsyncRelayCommand<ShellNavigationItem>(NavigateToItemAsync, onError: OnCommandFailed);
        _goBackCommand = new RelayCommand(() => _ = GoBackAsync(), () => _navigationService.CanGoBack);
        _goForwardCommand = new RelayCommand(() => _ = GoForwardAsync(), () => _navigationService.CanGoForward);
        RefreshCommand = new AsyncRelayCommand(RefreshCurrentAsync, onError: OnCommandFailed);
        ToggleNavigationCommand = new RelayCommand(ToggleNavigation);
        ToggleThemeCommand = new RelayCommand(_themeService.ToggleTheme);
        RefreshStatusCommand = new AsyncRelayCommand(
            () => _statusService.RefreshAsync(),
            onError: OnCommandFailed);
        SignOutCommand = new AsyncRelayCommand(SignOutAsync, onError: OnCommandFailed);

        _navigationService.Navigated += OnNavigated;
        _dateTimeFormatter.FormatChanged += OnFormatChanged;
    }

    private void OnFormatChanged(object? sender, EventArgs e)
    {
        OnPropertiesChanged(nameof(CurrentDate), nameof(CurrentTime));
    }

    /// <summary>Destinations shown in the left navigation panel, in display order.</summary>
    public ObservableCollection<ShellNavigationItem> NavigationItems { get; }

    /// <summary>The five subsystem indicators shown in the status bar.</summary>
    public IReadOnlyList<SubsystemStatus> Subsystems => _statusService.All;

    public SubsystemStatus DatabaseStatus => _statusService.Database;

    public SubsystemStatus WeightIndicatorStatus => _statusService.WeightIndicator;

    public SubsystemStatus PrinterStatus => _statusService.Printer;

    public SubsystemStatus ServerStatus => _statusService.Server;

    public SubsystemStatus ApplicationStatus => _statusService.Application;

    /// <summary>Title-bar product name.</summary>
    public string ApplicationName => _applicationInfo.ApplicationName;

    /// <summary>Status-bar version, already formatted for display.</summary>
    public string DisplayVersion => _applicationInfo.DisplayVersion;

    /// <summary>
    /// Signed-in operator, shown in the title bar.
    /// </summary>
    /// <remarks>
    /// Read once, for the same reason the Administration item is: a shell exists only for
    /// the duration of one session. Signing out closes this window and
    /// <c>App</c> builds a new shell for whoever signs in next, so the operator cannot
    /// change while this window exists — which is also what keeps the navigation rail
    /// honest, since it is filtered by permission in the constructor. It used to report
    /// <see cref="IApplicationInfoService.CurrentUserName"/> — the Windows account — which
    /// on a shared terminal named the machine's login rather than whoever was signed in.
    /// </remarks>
    public string CurrentUserName { get; }

    /// <summary>Optional site label shown beside the product name.</summary>
    public string SiteName => _applicationOptions.SiteName;

    /// <summary>Clock source for the title bar, ticked once a second by the view.</summary>
    public DateTime Now
    {
        get => _now;
        private set
        {
            if (SetProperty(ref _now, value))
            {
                OnPropertiesChanged(nameof(CurrentDate), nameof(CurrentTime));
            }
        }
    }

    public string CurrentDate => _now.ToString("ddd, dd MMM yyyy");

    public string CurrentTime => _dateTimeFormatter.FormatTime(_now);

    public ViewModelBase? CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }

    public ShellNavigationItem? SelectedItem
    {
        get => _selectedItem;
        private set => SetProperty(ref _selectedItem, value);
    }

    /// <summary>Collapses the navigation panel to an icon rail. Persisted.</summary>
    public bool IsNavigationCollapsed
    {
        get => _isNavigationCollapsed;
        private set => SetProperty(ref _isNavigationCollapsed, value);
    }

    public ICommand NavigateCommand { get; }

    public ICommand GoBackCommand => _goBackCommand;

    public ICommand GoForwardCommand => _goForwardCommand;

    public ICommand RefreshCommand { get; }

    public ICommand ToggleNavigationCommand { get; }

    public ICommand ToggleThemeCommand { get; }

    public ICommand RefreshStatusCommand { get; }

    /// <summary>Signs the operator out and returns the terminal to the login dialog.</summary>
    public ICommand SignOutCommand { get; }

    /// <summary>
    /// Raised once the operator has been signed out and the shell should close.
    /// </summary>
    /// <remarks>
    /// The ViewModel cannot close a window, so the window subscribes to this and closes
    /// itself. The window also records that the close was a sign-out, which is how
    /// <c>App</c> tells "show the login dialog again" apart from "the operator is finished
    /// and the process should end".
    /// </remarks>
    public event EventHandler? SignOutRequested;

    /// <summary>
    /// Opens the startup module and begins subsystem monitoring. Called once by the
    /// shell after the window is shown, so a slow first probe cannot delay display.
    /// </summary>
    /// <remarks>
    /// The database is prepared in the background from <c>App.OnStartup</c>, and the first
    /// module to open reads from it, so the schema has to be waited for here. Without this
    /// the first launch on a new machine raced the migration and the module opened onto
    /// "no such table". Awaiting the initialiser joins that run rather than starting one.
    /// </remarks>
    public async Task InitializeAsync()
    {
        await _statusService.StartMonitoringAsync().ConfigureAwait(true);
        await _databaseInitializer.InitializeAsync().ConfigureAwait(true);

        var startup = ResolveStartupItem();
        await NavigateToItemAsync(startup).ConfigureAwait(true);
    }

    /// <summary>Advances the title-bar clock. Driven by the view's timer.</summary>
    public void UpdateClock() => Now = DateTime.Now;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _navigationService.Navigated -= OnNavigated;
        _dateTimeFormatter.FormatChanged -= OnFormatChanged;
    }

    /// <summary>
    /// Prefers the module the operator last used, then the configured startup module,
    /// and finally the first item â€” so a renamed or removed module cannot leave the
    /// shell with nothing to show.
    /// </summary>
    private ShellNavigationItem ResolveStartupItem()
    {
        var last = _settingsService.Preferences.LastModule;

        var resolved = Find(last) ?? Find(_applicationOptions.StartupModule) ?? NavigationItems[0];
        return resolved;

        ShellNavigationItem? Find(string? moduleName) =>
            string.IsNullOrWhiteSpace(moduleName)
                ? null
                : NavigationItems.FirstOrDefault(item =>
                    string.Equals(item.Module.ToString(), moduleName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task NavigateToItemAsync(ShellNavigationItem? item)
    {
        if (item is null)
        {
            return;
        }

        await _navigationService.NavigateToAsync(item.ViewModelType).ConfigureAwait(true);
    }

    private async Task GoBackAsync()
    {
        try
        {
            await _navigationService.GoBackAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            OnCommandFailed(ex);
        }
    }

    private async Task GoForwardAsync()
    {
        try
        {
            await _navigationService.GoForwardAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            OnCommandFailed(ex);
        }
    }

    private Task RefreshCurrentAsync() => _navigationService.RefreshAsync();

    /// <summary>
    /// Confirms, signs the operator out, and asks the window to close.
    /// </summary>
    /// <remarks>
    /// Confirmed first because a weighbridge terminal is shared and the button sits next to
    /// the clock: an accidental sign-out mid-weighment costs the operator the form they were
    /// filling in.
    ///
    /// The identity is cleared before the window closes, so nothing that runs during
    /// teardown — a queued save, a health probe — is authorised as the operator who has
    /// just left.
    /// </remarks>
    private async Task SignOutAsync()
    {
        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Sign out?",
            $"{CurrentUserName} will be signed out and the login screen will be shown. Unsaved work on the current page will be lost.",
            "Sign out",
            "Stay signed in").ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        // Persisted here rather than left to the window's Closed handler: the preferences are
        // written by the same service the next session reads them from, and a sign-out is a
        // natural save point.
        await _settingsService.SaveAsync().ConfigureAwait(true);

        _authentication.SignOut();
        _logger.LogInformation("Operator signed out from the shell; returning to the login dialog");

        SignOutRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ToggleNavigation()
    {
        IsNavigationCollapsed = !IsNavigationCollapsed;
        _settingsService.Preferences.IsNavigationCollapsed = IsNavigationCollapsed;
        _ = _settingsService.SaveAsync();
    }

    /// <summary>
    /// Mirrors the navigation service's state onto the shell. Driven by the event rather
    /// than by the command, so Back, Forward and Refresh update the panel highlight too.
    /// </summary>
    private void OnNavigated(object? sender, NavigatedEventArgs e)
    {
        CurrentViewModel = e.Current;

        var item = NavigationItems.FirstOrDefault(candidate => candidate.ViewModelType == e.Current.GetType());

        if (item is not null && !ReferenceEquals(item, SelectedItem))
        {
            foreach (var candidate in NavigationItems)
            {
                candidate.IsSelected = ReferenceEquals(candidate, item);
            }

            SelectedItem = item;

            // Remembered so the next session opens where the operator left off.
            _settingsService.Preferences.LastModule = item.Module.ToString();
        }

        _goBackCommand.NotifyCanExecuteChanged();
        _goForwardCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Last line of defence for a command: the failure is logged and shown in a dialog
    /// rather than escaping to the dispatcher and taking the application down.
    /// </summary>
    private void OnCommandFailed(Exception exception)
    {
        _logger.LogError(exception, "A shell command failed");

        _ = _dialogService.ShowErrorAsync(
            "Something went wrong",
            "The action could not be completed. The details have been written to the log file.",
            exception.ToString());
    }
}
