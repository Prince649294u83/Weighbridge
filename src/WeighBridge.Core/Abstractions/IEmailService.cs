namespace WeighBridge.Core.Abstractions;

/// <summary>
/// Service contract for sending automated emails and weighment reports.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email with optional file attachment (e.g. weighment slip PDF).
    /// </summary>
    Task<bool> SendEmailAsync(
        string subject,
        string body,
        IEnumerable<string> recipients,
        byte[]? attachmentBytes = null,
        string? attachmentFileName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests SMTP server connectivity and authentication using current settings.
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
