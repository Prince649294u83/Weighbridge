using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using WeighBridge.App.Controls;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Dialogs;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Security;
using WeighBridge.Core.Threading;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Security;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// User account administration: creating, editing, disabling and deleting the accounts
/// that can sign in, and the roles they hold.
/// </summary>
/// <remarks>
/// Every write is gated by <c>AuthoriseUserManagementAsync</c>, which consults
/// <see cref="IPermissionService"/> rather than relying on the navigation rail hiding this
/// module — a hidden control is not an authorisation check.
/// </remarks>
public sealed class AdministrationViewModel : ModulePlaceholderViewModel
{
    // A factory, not an IUnitOfWork and not a standalone IRepository<User>. The container
    // registers WeighBridgeDbContext as transient, so an injected IRepository<User> and an
    // injected IUnitOfWork each own a separate change tracker: every write was staged on
    // one context and committed on the other, which saves nothing and reports no error.
    // Every masters and weighment service already takes this factory for the same reason.
    private readonly Func<IUnitOfWork> _unitOfWork;
    private readonly IPermissionService _permissions;
    private readonly IAuthenticationService _authentication;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<AdministrationViewModel> _logger;

    private readonly AsyncRelayCommand _refreshCommand;
    private readonly RelayCommand _newUserCommand;
    private readonly AsyncRelayCommand _saveUserCommand;
    private readonly RelayCommand _cancelEditCommand;
    private readonly AsyncRelayCommand<User> _disableUserCommand;
    private readonly AsyncRelayCommand<User> _enableUserCommand;

    private bool _isEditing;
    private long? _editingId;
    private string _formTitle = string.Empty;
    private string _formSaveLabel = "Save";

    private string _formUsername = string.Empty;
    private string _formDisplayName = string.Empty;
    private string _formPassword = string.Empty;
    private Role? _formSelectedRole;
    private bool _formIsActive = true;

    private string _statusMessage = string.Empty;
    private BadgeSeverity _statusSeverity = BadgeSeverity.Neutral;

    public AdministrationViewModel(
        Func<IUnitOfWork> unitOfWork,
        IPermissionService permissions,
        IAuthenticationService authentication,
        IDialogService dialogs,
        IUiDispatcher dispatcher,
        ILogger<AdministrationViewModel> logger)
        : base(
            ApplicationModule.Administration,
            "Administration",
            "User accounts, roles, audit history and database maintenance for supervisors.",
            "Icon.Administration")
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));

        // Password hashing belongs to the authentication service and nowhere else. This
        // module used to hash with its own copy of an unsalted SHA-256, so a password set
        // here and a password checked at sign-in were only equal by coincidence of two
        // duplicated implementations agreeing.
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Users = new ObservableCollection<User>();
        AvailableRoles = new ObservableCollection<Role>(Roles.All);

        // HasPermission is what the interface documents for a button's enabled state; the
        // Authorize call inside each operation is the control that actually stops the write.
        _refreshCommand = new AsyncRelayCommand(RefreshAsync);
        _newUserCommand = new RelayCommand(
            StartNewUser,
            () => !IsEditing && _permissions.HasPermission(Permissions.UsersManage));
        _saveUserCommand = new AsyncRelayCommand(SaveUserAsync, CanSaveUser);
        _cancelEditCommand = new RelayCommand(CancelEdit, () => IsEditing);
        _disableUserCommand = new AsyncRelayCommand<User>(
            DisableUserAsync,
            u => u != null && u.IsActive && _permissions.HasPermission(Permissions.UsersManage));
        _enableUserCommand = new AsyncRelayCommand<User>(
            EnableUserAsync,
            u => u != null && !u.IsActive && _permissions.HasPermission(Permissions.UsersManage));

        _ = RefreshAsync();
    }

    public ObservableCollection<User> Users { get; }
    public ObservableCollection<Role> AvailableRoles { get; }

    public ICommand RefreshCommand => _refreshCommand;
    public ICommand NewUserCommand => _newUserCommand;
    public ICommand SaveUserCommand => _saveUserCommand;
    public ICommand CancelEditCommand => _cancelEditCommand;
    public ICommand DisableUserCommand => _disableUserCommand;
    public ICommand EnableUserCommand => _enableUserCommand;

    public bool IsEditing
    {
        get => _isEditing;
        private set
        {
            if (SetProperty(ref _isEditing, value))
            {
                _newUserCommand.NotifyCanExecuteChanged();
                _cancelEditCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string FormTitle
    {
        get => _formTitle;
        private set => SetProperty(ref _formTitle, value);
    }

    public string FormSaveLabel
    {
        get => _formSaveLabel;
        private set => SetProperty(ref _formSaveLabel, value);
    }

    public string FormUsername
    {
        get => _formUsername;
        set
        {
            if (SetProperty(ref _formUsername, value))
                _saveUserCommand.NotifyCanExecuteChanged();
        }
    }

    public string FormDisplayName
    {
        get => _formDisplayName;
        set
        {
            if (SetProperty(ref _formDisplayName, value))
                _saveUserCommand.NotifyCanExecuteChanged();
        }
    }

    public string FormPassword
    {
        get => _formPassword;
        set
        {
            if (SetProperty(ref _formPassword, value))
                _saveUserCommand.NotifyCanExecuteChanged();
        }
    }

    public Role? FormSelectedRole
    {
        get => _formSelectedRole;
        set
        {
            if (SetProperty(ref _formSelectedRole, value))
                _saveUserCommand.NotifyCanExecuteChanged();
        }
    }

    public bool FormIsActive
    {
        get => _formIsActive;
        set => SetProperty(ref _formIsActive, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public BadgeSeverity StatusSeverity
    {
        get => _statusSeverity;
        private set => SetProperty(ref _statusSeverity, value);
    }

    private User? _selectedUser;
    public User? SelectedUser
    {
        get => _selectedUser;
        set
        {
            if (SetProperty(ref _selectedUser, value) && value != null)
            {
                StartEditUser(value);
            }
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            await using var unitOfWork = _unitOfWork();
            var users = await unitOfWork.Repository<User>().GetAllAsync();
            await _dispatcher.InvokeAsync(() =>
            {
                Users.Clear();
                foreach (var user in users)
                {
                    Users.Add(user);
                }
                
                SetStatus($"Loaded {Users.Count} users", BadgeSeverity.Success);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load users");
            SetStatus("Failed to load users", BadgeSeverity.Danger);
        }
    }

    private void StartNewUser()
    {
        _editingId = null;
        FormTitle = "New User";
        FormSaveLabel = "Create";

        FormUsername = string.Empty;
        FormDisplayName = string.Empty;
        FormPassword = string.Empty;
        FormSelectedRole = Roles.Operator;
        FormIsActive = true;

        IsEditing = true;
    }

    private void StartEditUser(User user)
    {
        _editingId = user.Id;
        FormTitle = $"Edit {user.Username}";
        FormSaveLabel = "Save Changes";

        FormUsername = user.Username;
        FormDisplayName = user.DisplayName;
        FormPassword = string.Empty; // Don't populate password, only update if provided
        FormSelectedRole = Roles.FromName(user.RoleName) ?? Roles.Operator;
        FormIsActive = user.IsActive;

        IsEditing = true;
    }

    private void CancelEdit()
    {
        IsEditing = false;
        _editingId = null;
        SelectedUser = null;
    }

    private bool CanSaveUser()
    {
        if (!_permissions.HasPermission(Permissions.UsersManage))
            return false;

        if (string.IsNullOrWhiteSpace(FormUsername) ||
            string.IsNullOrWhiteSpace(FormDisplayName) ||
            FormSelectedRole == null)
            return false;

        // Password is required for new users
        if (!_editingId.HasValue && string.IsNullOrWhiteSpace(FormPassword))
            return false;

        return true;
    }

    private async Task SaveUserAsync()
    {
        if (!CanSaveUser()) return;
        if (!await AuthoriseUserManagementAsync()) return;

        try
        {
            var username = FormUsername.Trim();
            if (username.Length > User.UsernameMaxLength)
            {
                await _dialogs.ShowWarningAsync("Validation Error", $"Username must be {User.UsernameMaxLength} characters or fewer.");
                return;
            }

            var displayName = FormDisplayName.Trim();
            if (displayName.Length > User.DisplayNameMaxLength)
            {
                await _dialogs.ShowWarningAsync("Validation Error", $"Display Name must be {User.DisplayNameMaxLength} characters or fewer.");
                return;
            }

            // An empty password on an update means "leave it alone"; a password that is
            // being set has to clear the same bar as the first-run administrator's, or the
            // minimum only applies to the one account created before the product is in use.
            if (!string.IsNullOrWhiteSpace(FormPassword) &&
                FormPassword.Length < IAuthenticationService.MinimumPasswordLength)
            {
                await _dialogs.ShowWarningAsync(
                    "Validation Error",
                    $"The password must be at least {IAuthenticationService.MinimumPasswordLength} characters long.");
                return;
            }

            // One unit of work for the whole save, so the duplicate check, the mutation and
            // the commit all run against a single change tracker.
            await using var unitOfWork = _unitOfWork();
            var repository = unitOfWork.Repository<User>();

            // Check if username already exists for another user
            var existingUsers = await repository.FindAsync(u => u.Username.ToLower() == username.ToLower());
            var existingUser = existingUsers.FirstOrDefault();
            if (existingUser != null && existingUser.Id != _editingId)
            {
                await _dialogs.ShowWarningAsync("Validation Error", $"The username '{username}' is already taken.");
                return;
            }

            if (_editingId.HasValue)
            {
                // Update
                var user = await repository.GetByIdAsync(_editingId.Value);
                if (user == null)
                {
                    await _dialogs.ShowErrorAsync("Save Failed", "The user could not be found.");
                    return;
                }

                user.Update(displayName, FormSelectedRole!.Name);
                if (!string.IsNullOrWhiteSpace(FormPassword))
                {
                    user.ChangePassword(_authentication.HashPassword(FormPassword));
                }

                if (FormIsActive != user.IsActive)
                {
                    if (FormIsActive) user.Activate();
                    else user.Deactivate();
                }

                repository.Update(user);
                await unitOfWork.SaveChangesAsync(CancellationToken.None);

                SetStatus($"User '{username}' updated.", BadgeSeverity.Success);
            }
            else
            {
                // Create
                var passwordHash = _authentication.HashPassword(FormPassword!);
                var user = User.Create(username, displayName, passwordHash, FormSelectedRole!.Name);
                
                if (!FormIsActive)
                {
                    user.Deactivate();
                }

                await repository.AddAsync(user);
                await unitOfWork.SaveChangesAsync(CancellationToken.None);

                _logger.LogInformation(
                    "User {Username} created with role {Role} by {Operator}",
                    username, FormSelectedRole!.Name, _permissions.CurrentOperator.UserName);

                SetStatus($"User '{username}' created.", BadgeSeverity.Success);
            }

            CancelEdit();
            await RefreshAsync();
        }
        catch (Exception ex) when (IsDuplicateUsername(ex))
        {
            // The check above passed and the insert still failed, which means another
            // terminal created this username in between - the filtered unique index on
            // User.Username is what stopped it. Reported as the same "already taken" message
            // the pre-check produces: the operator's next action is identical either way, and
            // a SQLite constraint message is not something to put in front of them.
            _logger.LogWarning(ex, "Saving user {Username} lost a race with another terminal", FormUsername);
            await _dialogs.ShowWarningAsync(
                "Validation Error",
                $"The username '{FormUsername?.Trim()}' is already taken.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save user");
            await _dialogs.ShowErrorAsync("Save Failed", "An error occurred while saving the user.", ex.Message);
        }
    }

    /// <summary>
    /// True when <paramref name="exception"/> is the database refusing a duplicate username.
    /// </summary>
    /// <remarks>
    /// Matched on the message rather than on <c>DbUpdateException</c>, so this layer keeps
    /// knowing nothing about Entity Framework. The chain is walked because the provider's
    /// constraint message is the inner exception, not the one thrown.
    /// </remarks>
    private static bool IsDuplicateUsername(Exception exception)
    {
        for (var candidate = exception; candidate is not null; candidate = candidate.InnerException)
        {
            if (candidate.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) &&
                candidate.Message.Contains("Username", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task DisableUserAsync(User? user)
    {
        if (user == null || !user.IsActive) return;
        if (!await AuthoriseUserManagementAsync()) return;

        try
        {
            var confirm = await _dialogs.ShowConfirmationAsync("Disable User", $"Are you sure you want to disable {user.Username}?", "Disable", "Cancel", true);
            if (!confirm) return;

            user.Deactivate();

            // The user came from the Users collection, so it is detached from any live
            // context. Update attaches it as Modified, which is what makes this persist.
            await using var unitOfWork = _unitOfWork();
            unitOfWork.Repository<User>().Update(user);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            _logger.LogInformation("User {Username} disabled by {Operator}",
                user.Username, _permissions.CurrentOperator.UserName);

            SetStatus($"User '{user.Username}' disabled.", BadgeSeverity.Success);
            await RefreshAsync();
            
            _disableUserCommand.NotifyCanExecuteChanged();
            _enableUserCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disable user");
            await _dialogs.ShowErrorAsync("Update Failed", "An error occurred while disabling the user.", ex.Message);
        }
    }

    private async Task EnableUserAsync(User? user)
    {
        if (user == null || user.IsActive) return;
        if (!await AuthoriseUserManagementAsync()) return;

        try
        {
            user.Activate();

            await using var unitOfWork = _unitOfWork();
            unitOfWork.Repository<User>().Update(user);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            _logger.LogInformation("User {Username} enabled by {Operator}",
                user.Username, _permissions.CurrentOperator.UserName);

            SetStatus($"User '{user.Username}' enabled.", BadgeSeverity.Success);
            await RefreshAsync();
            
            _disableUserCommand.NotifyCanExecuteChanged();
            _enableUserCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable user");
            await _dialogs.ShowErrorAsync("Update Failed", "An error occurred while enabling the user.", ex.Message);
        }
    }

    private void SetStatus(string message, BadgeSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
    }

    /// <summary>
    /// The gate for every user-management write.
    /// </summary>
    /// <remarks>
    /// <see cref="IPermissionService"/> was already injected here and never consulted, and
    /// user management is the one write path that does not go through
    /// <c>CommandExecutor</c>, which is where <c>IRequiresPermission</c> is enforced. The
    /// result was that any authenticated operator - a ReadOnly gate terminal included -
    /// could create an Administrator account. Checked inside each operation rather than
    /// only in CanExecute, because a disabled button is a hint and not a control.
    /// </remarks>
    private async Task<bool> AuthoriseUserManagementAsync()
    {
        var authorization = _permissions.Authorize(Permissions.UsersManage);
        if (authorization.IsAuthorized)
        {
            return true;
        }

        var operatorIdentity = _permissions.CurrentOperator;
        _logger.LogWarning(
            "User management denied for {Operator} as {Role}: {Reason}",
            operatorIdentity.UserName, operatorIdentity.Role.Name, authorization.Reason);

        SetStatus(authorization.Reason ?? "You are not permitted to manage users.", BadgeSeverity.Danger);
        await _dialogs.ShowWarningAsync("Not Permitted", authorization.Reason
            ?? "You are not permitted to manage users.");

        return false;
    }
}
