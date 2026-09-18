using System.Windows.Input;
using Microsoft.Extensions.Logging;
using WeighBridge.App.Controls;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Navigation;
using WeighBridge.Core.Threading;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;

namespace WeighBridge.App.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly IRepository<Weighment> _weighments;
    private readonly IWeightIndicatorService _indicator;
    private readonly IUiDispatcher _dispatcher;
    private readonly INavigationService _navigationService;
    private readonly ILogger<DashboardViewModel> _logger;
    private readonly AsyncRelayCommand _refreshCommand;

    private int _todaysCompleted;
    private int _currentlyWaiting;

    private string _liveWeight = "0";
    private string _liveUnit = "Kg";
    private string _indicatorStatus = "Disconnected";
    private BadgeSeverity _indicatorSeverity = BadgeSeverity.Danger;
    private bool _isIndicatorStable;

    public DashboardViewModel(
        IRepository<Weighment> weighments,
        IWeightIndicatorService indicator,
        IUiDispatcher dispatcher,
        INavigationService navigationService,
        ILogger<DashboardViewModel> logger)
    {
        _weighments = weighments ?? throw new ArgumentNullException(nameof(weighments));
        _indicator = indicator ?? throw new ArgumentNullException(nameof(indicator));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _refreshCommand = new AsyncRelayCommand(RefreshAsync);

        OpenVehicleEntryCommand = new AsyncRelayCommand(() => _navigationService.NavigateToAsync<VehicleEntryViewModel>());
        OpenReportsCommand = new AsyncRelayCommand(() => _navigationService.NavigateToAsync<ReportsViewModel>());
    }

    public new string Title => "Dashboard";
    public new string Description => "Live weighbridge status and daily totals.";

    public int TodaysCompleted
    {
        get => _todaysCompleted;
        private set => SetProperty(ref _todaysCompleted, value);
    }

    public int CurrentlyWaiting
    {
        get => _currentlyWaiting;
        private set => SetProperty(ref _currentlyWaiting, value);
    }

    public string LiveWeight
    {
        get => _liveWeight;
        private set => SetProperty(ref _liveWeight, value);
    }

    public string LiveUnit
    {
        get => _liveUnit;
        private set => SetProperty(ref _liveUnit, string.Equals(value, "kg", StringComparison.OrdinalIgnoreCase) ? "Kg" : value);
    }

    public string IndicatorStatus
    {
        get => _indicatorStatus;
        private set => SetProperty(ref _indicatorStatus, value);
    }

    public BadgeSeverity IndicatorSeverity
    {
        get => _indicatorSeverity;
        private set => SetProperty(ref _indicatorSeverity, value);
    }

    public bool IsIndicatorStable
    {
        get => _isIndicatorStable;
        private set => SetProperty(ref _isIndicatorStable, value);
    }

    public ICommand RefreshCommand => _refreshCommand;
    public ICommand OpenVehicleEntryCommand { get; }
    public ICommand OpenReportsCommand { get; }

    /// <remarks>
    /// The indicator is subscribed to here and released in <see cref="OnNavigatedFromAsync"/>,
    /// not in the constructor. A ViewModel is transient and the indicator is a singleton that
    /// raises a reading every 250 ms, so a subscription taken in the constructor outlives the
    /// visit: the event's delegate keeps the instance — and the repository's DbContext with it
    /// — alive for the rest of the process, and every past visit goes on handling every
    /// reading. The <c>-=</c> before each <c>+=</c> makes a repeat activation idempotent.
    /// </remarks>
    public override async Task OnNavigatedToAsync(NavigationContext context)
    {
        _indicator.ReadingReceived -= OnReadingReceived;
        _indicator.ReadingReceived += OnReadingReceived;
        _indicator.StateChanged -= OnStateChanged;
        _indicator.StateChanged += OnStateChanged;

        await RefreshAsync().ConfigureAwait(false);
    }

    public override Task OnNavigatedFromAsync()
    {
        _indicator.ReadingReceived -= OnReadingReceived;
        _indicator.StateChanged -= OnStateChanged;
        return Task.CompletedTask;
    }

    private async Task RefreshAsync()
    {
        try
        {
            var today = DateTime.Today.ToUniversalTime();
            var tomorrow = today.AddDays(1);

            var completed = await _weighments.CountAsync(w => 
                w.Status == WeighmentStatus.Completed && 
                w.CompletedAtUtc >= today && w.CompletedAtUtc < tomorrow, CancellationToken.None).ConfigureAwait(false);

            var waiting = await _weighments.CountAsync(w => 
                w.Status == WeighmentStatus.AwaitingSecondWeight, CancellationToken.None).ConfigureAwait(false);

            _dispatcher.Post(() =>
            {
                TodaysCompleted = completed;
                CurrentlyWaiting = waiting;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh dashboard stats.");
        }
    }

    private void OnReadingReceived(object? sender, WeightReading reading)
    {
        _dispatcher.Post(() =>
        {
            var displayVal = reading.Value < 0m ? 0m : reading.Value;
            LiveWeight = displayVal.ToString("0.##");
            LiveUnit = string.IsNullOrWhiteSpace(reading.Unit) ? "Kg" : (string.Equals(reading.Unit, "kg", StringComparison.OrdinalIgnoreCase) ? "Kg" : reading.Unit);
            IsIndicatorStable = reading.IsStable;

            if (reading.IsZero || reading.IsNegative || displayVal == 0m)
            {
                IndicatorStatus = "ZERO";
                IndicatorSeverity = BadgeSeverity.Neutral;
            }
            else if (reading.IsStable)
            {
                IndicatorStatus = "STABLE";
                IndicatorSeverity = BadgeSeverity.Success;
            }
            else
            {
                IndicatorStatus = "UNSTABLE";
                IndicatorSeverity = BadgeSeverity.Warning;
            }
        });
    }

    private void OnStateChanged(object? sender, ConnectionState state)
    {
        _dispatcher.Post(() =>
        {
            if (state != ConnectionState.Connected)
            {
                IndicatorStatus = "DISCONNECTED";
                IndicatorSeverity = BadgeSeverity.Danger;
                IsIndicatorStable = false;
            }
        });
    }
}
