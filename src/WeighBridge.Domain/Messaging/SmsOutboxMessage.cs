using WeighBridge.Domain.Common;

namespace WeighBridge.Domain.Messaging;

/// <summary>
/// Durable outbox message entity guaranteeing at-least-once SMS delivery surviving application crashes and restarts.
/// </summary>
public sealed class SmsOutboxMessage : EntityBase, IAggregateRoot
{
    public const int MaxMessageKeyLength = 128;
    public const int MaxRecipientLength = 32;
    public const int MaxContentLength = 1024;
    public const int MaxLastErrorLength = 512;
    public const int MaxProviderLength = 64;
    public const int MaxCorrelationIdLength = 36;

    private SmsOutboxMessage()
    {
        // EF Core materialization
    }

    private SmsOutboxMessage(
        string messageKey,
        long? weighmentId,
        string recipientPhoneNumber,
        string messageContent,
        int maxAttempts,
        string? correlationId)
    {
        MessageKey = messageKey;
        WeighmentId = weighmentId;
        RecipientPhoneNumber = recipientPhoneNumber;
        MessageContent = messageContent;
        Status = SmsOutboxStatus.Pending;
        Attempts = 0;
        MaxAttempts = Math.Max(1, maxAttempts);
        CorrelationId = correlationId;
    }

    /// <summary>Unique deterministic idempotency key (e.g. "WB-42:Completion").</summary>
    public string MessageKey { get; private set; } = string.Empty;

    /// <summary>Associated weighment ID if originating from a weighment transaction.</summary>
    public long? WeighmentId { get; private set; }

    /// <summary>Destination phone number.</summary>
    public string RecipientPhoneNumber { get; private set; } = string.Empty;

    /// <summary>Rendered text content of the SMS message.</summary>
    public string MessageContent { get; private set; } = string.Empty;

    /// <summary>Current delivery status.</summary>
    public SmsOutboxStatus Status { get; private set; }

    /// <summary>Number of delivery attempts made so far.</summary>
    public int Attempts { get; private set; }

    /// <summary>Maximum attempts allowed before routing to DeadLetter.</summary>
    public int MaxAttempts { get; private set; } = 3;

    /// <summary>Timestamp when current delivery lease started, used for crash recovery.</summary>
    public DateTime? SendingSinceUtc { get; private set; }

    /// <summary>Timestamp when next retry attempt is permitted.</summary>
    public DateTime? NextAttemptUtc { get; private set; }

    /// <summary>Timestamp when message was successfully sent.</summary>
    public DateTime? SentAtUtc { get; private set; }

    /// <summary>Last error message encountered during send attempt.</summary>
    public string? LastError { get; private set; }

    /// <summary>Provider identifier that fulfilled delivery.</summary>
    public string? ProviderUsed { get; private set; }

    /// <summary>Correlation ID tying message to business operation.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>
    /// Factory for creating a new outbox message with deterministic idempotency key.
    /// </summary>
    public static SmsOutboxMessage Create(
        string messageKey,
        long? weighmentId,
        string recipientPhoneNumber,
        string messageContent,
        int maxAttempts = 3,
        string? correlationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientPhoneNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageContent);

        return new SmsOutboxMessage(
            messageKey.Trim(),
            weighmentId,
            recipientPhoneNumber.Trim(),
            messageContent.Trim(),
            maxAttempts,
            correlationId);
    }

    /// <summary>
    /// Acquires a delivery lease, setting status to Sending.
    /// </summary>
    public void MarkSending()
    {
        Status = SmsOutboxStatus.Sending;
        SendingSinceUtc = DateTime.UtcNow;
        Attempts++;
    }

    /// <summary>
    /// Marks message as successfully delivered.
    /// </summary>
    public void MarkSent(string providerUsed)
    {
        Status = SmsOutboxStatus.Sent;
        SendingSinceUtc = null;
        SentAtUtc = DateTime.UtcNow;
        ProviderUsed = providerUsed;
        LastError = null;
    }

    /// <summary>
    /// Records a transient failure and schedules exponential backoff retry.
    /// </summary>
    public void RecordTransientFailure(string error, TimeSpan retryDelay)
    {
        SendingSinceUtc = null;
        LastError = Truncate(error, MaxLastErrorLength);

        if (Attempts >= MaxAttempts)
        {
            Status = SmsOutboxStatus.DeadLetter;
            NextAttemptUtc = null;
        }
        else
        {
            Status = SmsOutboxStatus.Failed;
            NextAttemptUtc = DateTime.UtcNow.Add(retryDelay);
        }
    }

    /// <summary>
    /// Immediately marks message as DeadLetter due to a permanent unrecoverable failure.
    /// </summary>
    public void MarkDeadLetter(string error)
    {
        Status = SmsOutboxStatus.DeadLetter;
        SendingSinceUtc = null;
        NextAttemptUtc = null;
        LastError = Truncate(error, MaxLastErrorLength);
    }

    /// <summary>
    /// Recovers a message stuck in Sending status after an application crash.
    /// </summary>
    public void RecoverFromStaleSending()
    {
        if (Status == SmsOutboxStatus.Sending)
        {
            Status = SmsOutboxStatus.Pending;
            SendingSinceUtc = null;
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
        return value[..maxLength];
    }
}
