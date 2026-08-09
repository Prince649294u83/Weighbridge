namespace WeighBridge.Core.Events.Catalog;

/// <summary>
/// Announces the outcome of a print job.
/// </summary>
/// <remarks>
/// Raised for failure as well as success. A failed print is exactly the case a subscriber
/// most needs to hear about — the reprint queue and the audit log both key off it — so
/// the outcome travels in the event rather than the event only being published on
/// success.
/// </remarks>
public sealed class PrintCompletedEvent : ApplicationEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="documentName">Name of the printed document.</param>
    /// <param name="succeeded">True when the job reached the printer.</param>
    /// <param name="printerName">Printer the job was sent to.</param>
    /// <param name="failureReason">Explanation when <paramref name="succeeded"/> is false.</param>
    /// <param name="source">Component that raised the event.</param>
    public PrintCompletedEvent(
        string documentName,
        bool succeeded,
        string? printerName = null,
        string? failureReason = null,
        string? source = null)
        : base(source)
    {
        DocumentName = documentName;
        Succeeded = succeeded;
        PrinterName = printerName;
        FailureReason = failureReason;
    }

    /// <summary>Name of the printed document.</summary>
    public string DocumentName { get; }

    /// <summary>True when the job reached the printer.</summary>
    public bool Succeeded { get; }

    /// <summary>Printer the job was sent to, when known.</summary>
    public string? PrinterName { get; }

    /// <summary>Explanation of the failure, when the job did not succeed.</summary>
    public string? FailureReason { get; }
}
