using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Busy;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Security;

namespace WeighBridge.App.Services;

/// <summary>
/// Monitors operator in-app activity and locks the terminal after configured inactivity duration,
/// safely deferring logout during active critical weighing operations.
/// </summary>
public sealed class SessionInactivityService : IDisposable
{
    private readonly IPermissionService _permissionService;
    private readonly IAuthenticationService _authenticationService;
    private readonly IBusyStateService _busyState;
    private readonly IAuditLogger _audit;
    private readonly IOptions<SecurityOptions> _options;
    private readonly ILogger<SessionInactivityService> _logger;
    private readonly DispatcherTimer _timer;

    private DateTime _lastActivityUtc = DateTime.UtcNow;
    private bool _timeoutPending;

    /// <summary>
    /// Raised when inactivity lock triggers and the shell should return to the login dialog.
    /// </summary>
    public event EventHandler? InactivityLockTriggered;

    public SessionInactivityService(
        IPermissionService permissionService,
        IAuthenticationService authenticationService,
        IBusyStateService busyState,
        IAuditLogger audit,
        IOptions<SecurityOptions> options,
        ILogger<SessionInactivityService> logger)
    {
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        _busyState = busyState ?? throw new ArgumentNullException(nameof(busyState));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        _busyState.PropertyChanged += OnBusyStateChanged;
    }

    /// <summary>
    /// Records user interaction (keyboard/mouse/touch) within the application.
    /// </summary>
    public void RecordActivity()
    {
        _lastActivityUtc = DateTime.UtcNow;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        // Only monitor when an authenticated user is signed in
        if (_permissionService.CurrentOperator.Role == Roles.Unauthenticated)
        {
            _timeoutPending = false;
            return;
        }

        var idleDuration = DateTime.UtcNow - _lastActivityUtc;
        var timeout = _options.Value.EffectiveInactivityTimeout;

        if (idleDuration >= timeout)
        {
            if (_busyState.IsBusy)
            {
                if (!_timeoutPending)
                {
                    _logger.LogInformation("Inactivity timeout reached during active operation; deferring terminal lock until operation finishes.");
                    _timeoutPending = true;
                }
            }
            else
            {
                ExecuteLock();
            }
        }
    }

    private void OnBusyStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IBusyStateService.IsBusy) && !_busyState.IsBusy && _timeoutPending)
        {
            _logger.LogInformation("Deferred inactivity timeout triggered upon operation completion.");
            ExecuteLock();
        }
    }

    private void ExecuteLock()
    {
        _timeoutPending = false;
        var operatorName = _permissionService.CurrentOperator.UserName;

        _logger.LogWarning("Operator session for {Operator} locked due to inactivity.", operatorName);
        _audit.Record(AuditActions.InactivityLock, "User", operatorName, "Terminal locked due to operator inactivity.");

        _authenticationService.SignOut();
        _lastActivityUtc = DateTime.UtcNow;
        InactivityLockTriggered?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _timer.Stop();
        _busyState.PropertyChanged -= OnBusyStateChanged;
    }
}
