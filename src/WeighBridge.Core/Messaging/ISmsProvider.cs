using WeighBridge.Core.Printing;

namespace WeighBridge.Core.Messaging;

/// <summary>
/// Pluggable transport provider responsible for physical SMS transmission.
/// </summary>
public interface ISmsProvider
{
    /// <summary>Unique name of the provider (e.g. "GsmModem", "HttpGateway").</summary>
    string Name { get; }

    /// <summary>
    /// Transmits an SMS message to the specified recipient phone number.
    /// </summary>
    Task<SmsSendResult> SendAsync(
        string recipientPhoneNumber,
        string messageContent,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Application service orchestrating the durable SMS outbox lifecycle.
/// </summary>
public interface ISmsService
{
    /// <summary>
    /// Enqueues an SMS notification for a completed weighment using canonical output data and recipient resolution.
    /// </summary>
    Task<string?> QueueWeighmentSmsAsync(
        WeighmentPrintData printData,
        string? explicitRecipient = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes due messages in the outbox (pending, failed-retry, and recovering stale sending leases).
    /// </summary>
    Task<int> ProcessOutboxAsync(CancellationToken cancellationToken = default);
}
