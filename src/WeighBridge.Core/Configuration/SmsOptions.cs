namespace WeighBridge.Core.Configuration;

/// <summary>
/// Delivery provider type for SMS notifications.
/// </summary>
public enum SmsProviderType
{
    /// <summary>Hardware GSM Modem connected to dedicated serial COM port.</summary>
    GsmModem,

    /// <summary>HTTP/REST Webhook Gateway provider.</summary>
    HttpGateway
}

/// <summary>
/// Configuration options for SMS messaging subsystem.
/// </summary>
public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    /// <summary>Whether SMS dispatching is globally enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Active provider implementation to use.</summary>
    public SmsProviderType Provider { get; set; } = SmsProviderType.GsmModem;

    /// <summary>COM port for GSM Modem (e.g. "COM4"). Explicitly configured; no scanning.</summary>
    public string GsmPortName { get; set; } = "COM4";

    /// <summary>Baud rate for GSM Modem (e.g. 9600 or 115200).</summary>
    public int GsmBaudRate { get; set; } = 9600;

    /// <summary>Optional PIN for GSM SIM card, protected via DPAPI.</summary>
    public string? GsmPin { get; set; }

    /// <summary>HTTP Gateway endpoint URL (e.g. "https://api.sms-gateway.com/v1/send").</summary>
    public string HttpEndpointUrl { get; set; } = string.Empty;

    /// <summary>HTTP Gateway API key / token, protected via DPAPI and injected at header layer.</summary>
    public string? HttpApiKey { get; set; }

    /// <summary>Maximum retry attempts before routing to DeadLetter (default 3).</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base retry delay in seconds for exponential backoff (default 30s).</summary>
    public int RetryDelaySeconds { get; set; } = 30;

    /// <summary>Default recipient phone number when party has no registered mobile number.</summary>
    public string? DefaultRecipient { get; set; }

    /// <summary>Automatically enqueue SMS outbox message upon weighment completion.</summary>
    public bool AutoSendOnCompletion { get; set; } = true;
}
