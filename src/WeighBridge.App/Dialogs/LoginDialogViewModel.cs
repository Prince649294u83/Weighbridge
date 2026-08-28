using System.Threading.Tasks;
using System.Windows.Input;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Security;

namespace WeighBridge.App.Dialogs;

/// <summary>
/// What the dialog collected when the operator submitted it. The password never becomes a
/// bindable property: a <see cref="System.Windows.Controls.PasswordBox"/> deliberately
/// refuses to expose its content to the binding system, and this dialog does not undo that.
/// </summary>
public sealed record LoginSubmission(string Password, string Confirmation);

/// <summary>
/// Backs the modal dialog that stands between startup and the shell.
/// </summary>
/// <remarks>
/// Has two modes. Normally it authenticates. When the database holds no user accounts it
/// collects the first administrator instead, because the application seeds no account and
/// therefore ships with no password: there is nothing to sign in as until someone is
/// appointed. <see cref="InitializeAsync"/> settles the mode before the window is shown, so
/// the title, prompt and fields never change under the operator's hands.
/// </remarks>
public sealed class LoginDialogViewModel : ObservableObject
{
    private readonly IAuthenticationService _authenticationService;

    private string _username = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isInitialSetup;

    public LoginDialogViewModel(IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));

        SubmitCommand = new AsyncRelayCommand<LoginSubmission>(ExecuteSubmitAsync, CanExecuteSubmit);
    }

    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value))
            {
                ((AsyncRelayCommand<LoginSubmission>)SubmitCommand).NotifyCanExecuteChanged();
                ErrorMessage = string.Empty;
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                // Raised here rather than at each assignment: the message was previously
                // cleared in three places and the banner's visibility only followed in one
                // of them, so a corrected username left the old error on screen.
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>True when this dialog is appointing the first administrator, not signing in.</summary>
    public bool IsInitialSetup
    {
        get => _isInitialSetup;
        private set
        {
            if (SetProperty(ref _isInitialSetup, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(HeaderText));
                OnPropertyChanged(nameof(PromptText));
                OnPropertyChanged(nameof(SubmitLabel));
            }
        }
    }

    /// <summary>
    /// The window's title, and the only thing that distinguishes the two modes to anything
    /// outside the process - including the automated smoke scripts, which must not type a
    /// sign-in into a setup form and report it as a successful login.
    /// </summary>
    public string WindowTitle => IsInitialSetup ? "Administrator setup" : "Login";

    public string HeaderText => IsInitialSetup ? "Create the administrator account" : "System Login";

    public string PromptText => IsInitialSetup
        ? $"This installation has no accounts yet. Choose the administrator's username and a password of at least {IAuthenticationService.MinimumPasswordLength} characters. Keep it safe: it cannot be recovered."
        : "Please authenticate to access the application.";

    public string SubmitLabel => IsInitialSetup ? "Create Account" : "Login";

    public ICommand SubmitCommand { get; }

    public Action? RequestClose { get; set; }

    public bool IsAuthenticated { get; private set; }

    /// <summary>
    /// Decides which mode the dialog opens in. Awaited by the caller before the window is
    /// constructed, so this is the one place that touches the database.
    /// </summary>
    public async Task InitializeAsync()
    {
        IsInitialSetup = await _authenticationService.RequiresInitialSetupAsync().ConfigureAwait(true);
    }

    private bool CanExecuteSubmit(LoginSubmission? submission)
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(submission?.Password))
        {
            return false;
        }

        return !IsInitialSetup || !string.IsNullOrWhiteSpace(submission.Confirmation);
    }

    private async Task ExecuteSubmitAsync(LoginSubmission? submission)
    {
        if (submission is null || string.IsNullOrWhiteSpace(submission.Password))
        {
            return;
        }

        ErrorMessage = string.Empty;

        var success = IsInitialSetup
            ? await CreateAdministratorAsync(submission).ConfigureAwait(true)
            : await _authenticationService.AuthenticateAsync(Username, submission.Password).ConfigureAwait(true);

        if (success)
        {
            IsAuthenticated = true;
            RequestClose?.Invoke();
        }
        else if (!HasError)
        {
            ErrorMessage = IsInitialSetup
                ? "The administrator account could not be created."
                : "Invalid username or password";
        }
    }

    private async Task<bool> CreateAdministratorAsync(LoginSubmission submission)
    {
        // The service enforces both of these again. These two checks exist to name the
        // problem to the operator, not to be the control.
        if (submission.Password.Length < IAuthenticationService.MinimumPasswordLength)
        {
            ErrorMessage = $"The password must be at least {IAuthenticationService.MinimumPasswordLength} characters long.";
            return false;
        }

        if (!string.Equals(submission.Password, submission.Confirmation, StringComparison.Ordinal))
        {
            ErrorMessage = "The passwords do not match.";
            return false;
        }

        return await _authenticationService
            .CreateInitialAdministratorAsync(Username.Trim(), submission.Password)
            .ConfigureAwait(true);
    }
}
