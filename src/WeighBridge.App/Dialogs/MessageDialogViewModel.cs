using WeighBridge.Core.Mvvm;

namespace WeighBridge.App.Dialogs;

/// <summary>
/// Presentation state for <see cref="MessageDialog"/>.
/// </summary>
public sealed class MessageDialogViewModel : ObservableObject
{
    public MessageDialogViewModel(
        string title,
        string message,
        DialogSeverity severity,
        string? details = null,
        string confirmText = "OK",
        string? cancelText = null,
        bool isDestructive = false)
    {
        Title = title;
        Message = message;
        Severity = severity;
        Details = details;
        ConfirmText = confirmText;
        CancelText = cancelText;
        IsDestructive = isDestructive;
    }

    /// <summary>Dialog heading.</summary>
    public string Title { get; }

    /// <summary>Primary body text, written for an operator rather than a developer.</summary>
    public string Message { get; }

    /// <summary>Optional technical detail, hidden behind a disclosure toggle.</summary>
    public string? Details { get; }

    /// <summary>Controls the accent colour and glyph.</summary>
    public DialogSeverity Severity { get; }

    /// <summary>Label of the affirmative button.</summary>
    public string ConfirmText { get; }

    /// <summary>Label of the dismiss button. <c>null</c> hides it (acknowledge-only dialogs).</summary>
    public string? CancelText { get; }

    /// <summary>Renders the affirmative button in the danger role.</summary>
    public bool IsDestructive { get; }

    /// <summary>True when there is technical detail to disclose.</summary>
    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);

    /// <summary>True when the dialog offers a second, dismissive button.</summary>
    public bool HasCancel => !string.IsNullOrWhiteSpace(CancelText);
}
