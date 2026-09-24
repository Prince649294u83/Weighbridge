using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Messaging;
using WeighBridge.Core.Printing;
using WeighBridge.Domain.Messaging;
using WeighBridge.Domain.Weighments;

namespace WeighBridge.Services.Messaging;

/// <summary>
/// Durable transactional outbox processor guaranteeing at-least-once SMS delivery
/// surviving crashes, restarts, and temporary network / hardware disconnections.
/// </summary>
public sealed class SmsOutboxProcessor : ISmsService
{
    private static readonly TimeSpan StaleLeaseThreshold = TimeSpan.FromMinutes(5);

    private readonly Func<IUnitOfWork> _unitOfWork;
    private readonly SmsTemplateEngine _templateEngine;
    private readonly SmsRecipientResolver _recipientResolver;
    private readonly IEnumerable<ISmsProvider> _providers;
    private readonly IOptions<SmsOptions> _options;
    private readonly ILogger<SmsOutboxProcessor> _logger;

    public SmsOutboxProcessor(
        Func<IUnitOfWork> unitOfWork,
        SmsTemplateEngine templateEngine,
        SmsRecipientResolver recipientResolver,
        IEnumerable<ISmsProvider> providers,
        IOptions<SmsOptions> options,
        ILogger<SmsOutboxProcessor> logger)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _templateEngine = templateEngine ?? throw new ArgumentNullException(nameof(templateEngine));
        _recipientResolver = recipientResolver ?? throw new ArgumentNullException(nameof(recipientResolver));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string?> QueueWeighmentSmsAsync(
        WeighmentPrintData printData,
        string? overrideRecipient = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(printData);

        var opt = _options.Value;
        if (!opt.Enabled)
        {
            _logger.LogInformation("SMS subsystem is disabled; skipping outbox queue for weighment slip {Slip}", printData.SlipNumber);
            return null;
        }

        var recipient = _recipientResolver.ResolveRecipient(overrideRecipient, null);
        if (string.IsNullOrWhiteSpace(recipient))
        {
            _logger.LogWarning("No recipient phone number resolved for weighment slip {Slip}; skipping SMS queue", printData.SlipNumber);
            return null;
        }

        // Deterministic idempotency key: {SlipNumber}:Completion
        var canonicalSlip = SlipNumbers.Normalise(printData.SlipNumber) ?? printData.SlipNumber;
        var messageKey = $"{canonicalSlip}:Completion";
        var legacyMessageKey = $"WB-{canonicalSlip}:Completion";

        await using var scope = _unitOfWork();
        var repo = scope.Repository<SmsOutboxMessage>();

        // Idempotency check: guard against both modern and legacy keys
        var existing = await repo.FindAsync(m => m.MessageKey == messageKey || m.MessageKey == legacyMessageKey, cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            _logger.LogWarning("SMS outbox message with key {Key} already exists; ignoring duplicate enqueue", existing[0].MessageKey);
            return existing[0].MessageKey;
        }

        var content = _templateEngine.Render(null, printData);
        var message = SmsOutboxMessage.Create(
            messageKey,
            weighmentId: null,
            recipientPhoneNumber: recipient,
            messageContent: content,
            maxAttempts: opt.MaxRetries,
            correlationId: correlationId);

        await repo.AddAsync(message, cancellationToken).ConfigureAwait(false);
        await scope.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Enqueued SMS outbox message {Key} for recipient {Recipient}", messageKey, recipient);
        return messageKey;
    }

    /// <inheritdoc />
    public async Task<int> ProcessOutboxAsync(CancellationToken cancellationToken = default)
    {
        var opt = _options.Value;
        if (!opt.Enabled) return 0;

        int processedCount = 0;

        // 1. Recover stale Sending leases in an isolated unit of work scope
        await RecoverStaleSendingMessagesAsync(cancellationToken).ConfigureAwait(false);

        // 2. Fetch and process pending or due retry messages in an active dispatch scope
        await using var scope = _unitOfWork();
        var repo = scope.Repository<SmsOutboxMessage>();

        var now = DateTime.UtcNow;
        var dueMessages = await repo.QueryAsync(
            m => m.Status == SmsOutboxStatus.Pending ||
                 (m.Status == SmsOutboxStatus.Failed && m.NextAttemptUtc != null && m.NextAttemptUtc <= now),
            q => q.OrderBy(m => m.CreatedAtUtc).Take(20),
            cancellationToken).ConfigureAwait(false);

        if (dueMessages.Count == 0) return 0;

        var activeProvider = ResolveActiveProvider(opt.Provider);
        if (activeProvider == null)
        {
            _logger.LogError("Configured SMS provider {Provider} is not registered in the system", opt.Provider);
            return 0;
        }

        foreach (var message in dueMessages)
        {
            if (cancellationToken.IsCancellationRequested) break;

            message.MarkSending();
            repo.Update(message);
            await scope.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var sendResult = await activeProvider.SendAsync(
                message.RecipientPhoneNumber,
                message.MessageContent,
                cancellationToken).ConfigureAwait(false);

            if (sendResult.Succeeded)
            {
                message.MarkSent(activeProvider.Name);
                repo.Update(message);
                _logger.LogInformation("SMS message {Key} successfully delivered via provider {Provider}",
                    message.MessageKey, activeProvider.Name);
                processedCount++;
            }
            else
            {
                switch (sendResult.ErrorType)
                {
                    case SmsErrorType.Permanent:
                        _logger.LogError("Permanent delivery failure for SMS {Key}: {Error}; routing to DeadLetter",
                            message.MessageKey, sendResult.ErrorMessage);
                        message.MarkDeadLetter(sendResult.ErrorMessage ?? "Permanent failure");
                        repo.Update(message);
                        break;

                    case SmsErrorType.ProviderConfigurationOrAuth:
                        _logger.LogError("SMS Provider Configuration/Auth error for SMS {Key}: {Error}; pausing queue", message.MessageKey, sendResult.ErrorMessage);
                        var configRetryDelay = TimeSpan.FromSeconds(Math.Max(60, opt.RetryDelaySeconds * 2));
                        message.RecordTransientFailure(sendResult.ErrorMessage ?? "Provider Auth Error", configRetryDelay);
                        repo.Update(message);
                        break;

                    case SmsErrorType.Transient:
                    default:
                        // Exponential backoff: baseDelay * 2^(attempts-1)
                        int factor = 1 << Math.Min(6, message.Attempts - 1);
                        var retryDelay = TimeSpan.FromSeconds(opt.RetryDelaySeconds * factor);
                        _logger.LogWarning("Transient delivery failure for SMS {Key} (attempt {Attempt}/{Max}): {Error}; retry in {Delay}s",
                            message.MessageKey, message.Attempts, message.MaxAttempts, sendResult.ErrorMessage, retryDelay.TotalSeconds);
                        message.RecordTransientFailure(sendResult.ErrorMessage ?? "Transient error", retryDelay);
                        repo.Update(message);
                        break;
                }
            }

            await scope.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return processedCount;
    }

    private async Task RecoverStaleSendingMessagesAsync(CancellationToken cancellationToken)
    {
        await using var recoveryScope = _unitOfWork();
        var repo = recoveryScope.Repository<SmsOutboxMessage>();

        var staleThreshold = DateTime.UtcNow - StaleLeaseThreshold;
        var staleMessages = await repo.FindAsync(
            m => m.Status == SmsOutboxStatus.Sending && m.SendingSinceUtc != null && m.SendingSinceUtc < staleThreshold,
            cancellationToken).ConfigureAwait(false);

        if (staleMessages.Count == 0) return;

        foreach (var stale in staleMessages)
        {
            stale.RecoverFromStaleSending();
            repo.Update(stale);
        }

        await recoveryScope.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Recovered {Count} stale sending SMS message(s) back to Pending status", staleMessages.Count);
    }

    private ISmsProvider? ResolveActiveProvider(SmsProviderType providerType)
    {
        var targetName = providerType switch
        {
            SmsProviderType.GsmModem => GsmModemSmsProvider.ProviderName,
            SmsProviderType.HttpGateway => HttpGatewaySmsProvider.ProviderName,
            _ => GsmModemSmsProvider.ProviderName
        };

        return _providers.FirstOrDefault(p => string.Equals(p.Name, targetName, StringComparison.OrdinalIgnoreCase));
    }
}
