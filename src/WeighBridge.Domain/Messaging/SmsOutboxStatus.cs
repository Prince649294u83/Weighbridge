namespace WeighBridge.Domain.Messaging;

/// <summary>
/// Authoritative delivery states for an SMS outbox message.
/// </summary>
public enum SmsOutboxStatus
{
    /// <summary>Queued and awaiting delivery.</summary>
    Pending = 0,

    /// <summary>Currently being processed by a delivery provider.</summary>
    Sending = 1,

    /// <summary>Successfully sent and confirmed by provider.</summary>
    Sent = 2,

    /// <summary>Encountered a transient failure; scheduled for retry.</summary>
    Failed = 3,

    /// <summary>Permanently failed or exceeded max retries; requires manual intervention.</summary>
    DeadLetter = 4
}
