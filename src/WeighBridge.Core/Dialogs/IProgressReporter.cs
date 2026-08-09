namespace WeighBridge.Core.Dialogs;

/// <summary>
/// Handed to long-running operations so they can publish progress to a progress
/// dialog without referencing WPF.
/// </summary>
public interface IProgressReporter
{
    /// <summary>Signalled when the operator cancels a cancellable operation.</summary>
    CancellationToken CancellationToken { get; }

    /// <summary>Updates the primary status line.</summary>
    void ReportStatus(string status);

    /// <summary>Updates the completion percentage (0-100). Values are clamped.</summary>
    void ReportProgress(double percentage);

    /// <summary>Updates both the percentage and the status line in one call.</summary>
    void Report(double percentage, string status);

    /// <summary>Switches the dialog to an indeterminate (marquee) indicator.</summary>
    void ReportIndeterminate(string status);
}
