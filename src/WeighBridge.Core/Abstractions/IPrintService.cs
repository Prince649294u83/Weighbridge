namespace WeighBridge.Core.Abstractions;

/// <summary>
/// Outcome of a print request.
/// </summary>
/// <param name="Succeeded">True when the job was accepted by the spooler.</param>
/// <param name="Message">Human-readable outcome for logging and dialogs.</param>
/// <param name="JobId">Spooler job identifier, when one was assigned.</param>
public sealed record PrintResult(bool Succeeded, string Message, string? JobId = null)
{
    /// <summary>Creates a successful result.</summary>
    public static PrintResult Success(string message, string? jobId = null) => new(true, message, jobId);

    /// <summary>Creates a failed result.</summary>
    public static PrintResult Failure(string message) => new(false, message);
}

/// <summary>
/// Prints weighment slips and other documents.
/// </summary>
/// <remarks>
/// Module 0.1 registers a placeholder that reports printer availability without
/// producing output. The Printing module replaces the implementation only.
/// </remarks>
public interface IPrintService : IHealthCheck
{
    /// <summary>Names of the printers installed on this machine.</summary>
    Task<IReadOnlyList<string>> GetAvailablePrintersAsync(CancellationToken cancellationToken = default);

    /// <summary>The printer that will be used when no explicit name is supplied.</summary>
    Task<string?> GetDefaultPrinterAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Prints a document identified by <paramref name="documentKey"/> using the supplied
    /// data. Concrete document types arrive with the Printing module.
    /// </summary>
    Task<PrintResult> PrintAsync(
        string documentKey,
        IReadOnlyDictionary<string, object?> data,
        string? printerName = null,
        int copies = 1,
        CancellationToken cancellationToken = default);
}
