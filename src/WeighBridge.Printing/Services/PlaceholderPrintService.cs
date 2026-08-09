using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Printing.Services;

/// <summary>
/// Placeholder print service that accepts no jobs.
/// </summary>
/// <remarks>
/// Module 0.1 defines the printing contract and the status indicator only. Printer
/// enumeration needs Windows-specific APIs that belong to the Printing module, so
/// this build reports "not implemented" rather than guessing.
/// </remarks>
public sealed class PlaceholderPrintService(
    IOptions<PrinterOptions> options,
    ILogger<PlaceholderPrintService> logger) : IPrintService
{
    private readonly PrinterOptions _options = options.Value;
    private readonly ILogger<PlaceholderPrintService> _logger = logger;

    /// <inheritdoc />
    public string Name => "Printer";

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetAvailablePrintersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    /// <inheritdoc />
    public Task<string?> GetDefaultPrinterAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(string.IsNullOrWhiteSpace(_options.DefaultPrinterName)
            ? null
            : _options.DefaultPrinterName);

    /// <inheritdoc />
    public Task<PrintResult> PrintAsync(
        string documentKey,
        IReadOnlyDictionary<string, object?> data,
        string? printerName = null,
        int copies = 1,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Print request for {DocumentKey} was ignored: printing is not implemented in this build",
            documentKey);

        return Task.FromResult(PrintResult.Failure("Printing is not available in this build."));
    }

    /// <inheritdoc />
    public Task<HealthResult> CheckAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_options.Enabled
            ? HealthResult.Unreachable(
                string.IsNullOrWhiteSpace(_options.DefaultPrinterName)
                    ? "No printer configured"
                    : $"{_options.DefaultPrinterName} · driver not implemented in this build")
            : HealthResult.Disabled("Printing disabled in configuration"));
}
