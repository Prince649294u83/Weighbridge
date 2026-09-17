using System.IO;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Settings.Configuration;

namespace WeighBridge.Services.Messaging;

/// <summary>
/// Production SMTP implementation of <see cref="IEmailService"/> with TLS/SSL,
/// DPAPI secret unprotection, and resilient attachment handling.
/// </summary>
public sealed class SmtpEmailService : IEmailService
{
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(
        IOptionsMonitor<EmailOptions> options,
        ILogger<SmtpEmailService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> SendEmailAsync(
        string subject,
        string body,
        IEnumerable<string> recipients,
        byte[]? attachmentBytes = null,
        string? attachmentFileName = null,
        CancellationToken cancellationToken = default)
    {
        var config = _options.CurrentValue;
        if (!config.Enabled)
        {
            _logger.LogInformation("Email sending skipped: Email service is disabled in configuration.");
            return false;
        }

        var recipientList = recipients
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToList();

        if (recipientList.Count == 0)
        {
            _logger.LogWarning("Email sending failed: No valid recipient addresses provided.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(config.SmtpServer))
        {
            _logger.LogError("Email sending failed: SMTP server is not configured.");
            return false;
        }

        try
        {
            using var client = CreateSmtpClient(config);
            using var message = new MailMessage();

            var fromAddress = string.IsNullOrWhiteSpace(config.SenderId) ? "noreply@weighbridge.local" : config.SenderId;
            message.From = string.IsNullOrWhiteSpace(config.SenderName)
                ? new MailAddress(fromAddress)
                : new MailAddress(fromAddress, config.SenderName);

            foreach (var to in recipientList)
            {
                message.To.Add(to);
            }

            message.Subject = subject;
            message.Body = body;
            message.IsBodyHtml = false;

            MemoryStream? stream = null;
            Attachment? attachment = null;
            if (attachmentBytes is { Length: > 0 } && !string.IsNullOrWhiteSpace(attachmentFileName))
            {
                stream = new MemoryStream(attachmentBytes);
                attachment = new Attachment(stream, attachmentFileName);
                message.Attachments.Add(attachment);
            }

            try
            {
                await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Email sent successfully to {Count} recipients with subject: {Subject}", recipientList.Count, subject);
                return true;
            }
            finally
            {
                attachment?.Dispose();
                stream?.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Email sending canceled by user or timeout.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Recipients}: {ErrorMessage}", string.Join(", ", recipientList), ex.Message);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var config = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(config.SmtpServer))
        {
            _logger.LogError("SMTP connection test failed: Server host is empty.");
            return false;
        }

        try
        {
            var port = config.SmtpPort > 0 ? config.SmtpPort : 25;
            using var tcpClient = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            await tcpClient.ConnectAsync(config.SmtpServer, port, cts.Token).ConfigureAwait(false);
            if (tcpClient.Connected)
            {
                _logger.LogInformation("SMTP connection test succeeded for {Server}:{Port}", config.SmtpServer, port);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP connection test failed for {Server}:{Port}: {ErrorMessage}", config.SmtpServer, config.SmtpPort, ex.Message);
            return false;
        }
    }

    private static SmtpClient CreateSmtpClient(EmailOptions config)
    {
        var port = config.SmtpPort > 0 ? config.SmtpPort : 587;
        var client = new SmtpClient(config.SmtpServer, port)
        {
            EnableSsl = config.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 15000
        };

        if (!string.IsNullOrWhiteSpace(config.SenderId) && !string.IsNullOrWhiteSpace(config.Password))
        {
            var rawPassword = SecretProtector.TryUnprotect(config.Password) ?? config.Password;
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(config.SenderId, rawPassword);
        }

        return client;
    }
}
