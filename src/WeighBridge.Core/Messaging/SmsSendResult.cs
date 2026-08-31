namespace WeighBridge.Core.Messaging;

/// <summary>
/// Error classification for SMS delivery attempts.
/// </summary>
public enum SmsErrorType
{
    None = 0,

    /// <summary>Temporary network timeout, carrier busy, or 5xx server error. Eligible for retry.</summary>
    Transient = 1,

    /// <summary>Invalid phone number syntax, bad request payload, or dead-end condition. Immediately dead-lettered.</summary>
    Permanent = 2,

    /// <summary>Authentication or gateway misconfiguration (HTTP 401/403, bad API key). Pauses queue/alerts rather than dead-lettering message.</summary>
    ProviderConfigurationOrAuth = 3
}

/// <summary>
/// Detailed result of an SMS delivery attempt by an <see cref="ISmsProvider"/>.
/// </summary>
public sealed record SmsSendResult
{
    public bool Succeeded { get; init; }
    public string? ProviderMessageId { get; init; }
    public string? ErrorMessage { get; init; }
    public SmsErrorType ErrorType { get; init; }

    public static SmsSendResult Success(string? providerMessageId = null) =>
        new() { Succeeded = true, ProviderMessageId = providerMessageId, ErrorType = SmsErrorType.None };

    public static SmsSendResult TransientFailure(string error) =>
        new() { Succeeded = false, ErrorMessage = error, ErrorType = SmsErrorType.Transient };

    public static SmsSendResult PermanentFailure(string error) =>
        new() { Succeeded = false, ErrorMessage = error, ErrorType = SmsErrorType.Permanent };

    public static SmsSendResult ConfigurationFailure(string error) =>
        new() { Succeeded = false, ErrorMessage = error, ErrorType = SmsErrorType.ProviderConfigurationOrAuth };
}
