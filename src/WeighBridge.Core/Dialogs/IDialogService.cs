namespace WeighBridge.Core.Dialogs;

/// <summary>
/// Shows modern, custom-styled dialogs. The default <c>MessageBox</c> is never used
/// anywhere in the application.
/// </summary>
public interface IDialogService
{
    /// <summary>Shows an informational dialog with a single acknowledge button.</summary>
    Task ShowInformationAsync(string title, string message, string? details = null);

    /// <summary>Shows a success dialog with a single acknowledge button.</summary>
    Task ShowSuccessAsync(string title, string message, string? details = null);

    /// <summary>Shows a warning dialog with a single acknowledge button.</summary>
    Task ShowWarningAsync(string title, string message, string? details = null);

    /// <summary>Shows an error dialog with an optional expandable technical detail area.</summary>
    Task ShowErrorAsync(string title, string message, string? details = null);

    /// <summary>
    /// Asks the operator to confirm an action.
    /// </summary>
    /// <returns><c>true</c> when the affirmative button was chosen.</returns>
    Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string confirmText = "Yes",
        string cancelText = "No",
        bool isDestructive = false);

    /// <summary>
    /// Runs <paramref name="operation"/> behind a modal indeterminate loading overlay.
    /// </summary>
    Task ShowLoadingAsync(string message, Func<Task> operation);

    /// <summary>
    /// Runs <paramref name="operation"/> behind a modal progress dialog, handing it a
    /// reporter it can use to publish percentage and status updates.
    /// </summary>
    Task ShowProgressAsync(string title, Func<IProgressReporter, Task> operation, bool isCancellable = false);

    /// <summary>
    /// Shows a modal login dialog. Returns true if the user authenticated successfully.
    /// </summary>
    Task<bool> ShowLoginAsync();
}
