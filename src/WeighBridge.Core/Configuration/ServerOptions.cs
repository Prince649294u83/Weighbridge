namespace WeighBridge.Core.Configuration;

/// <summary>
/// Bound to the <c>Server</c> section of <c>appsettings.json</c>. Describes the
/// optional central server that multi-weighbridge sites synchronise with.
/// </summary>
public sealed class ServerOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Server";

    /// <summary>Enables server synchronisation.</summary>
    public bool Enabled { get; set; }

    /// <summary>Base address of the central API.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>API key used for authentication.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Identifier of this terminal within the estate.</summary>
    public string TerminalId { get; set; } = string.Empty;

    /// <summary>How often pending records are pushed, in seconds.</summary>
    public int SyncIntervalSeconds { get; set; } = 300;

    /// <summary>Per-request timeout, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Interval of the background reachability re-check, in seconds.</summary>
    public int HealthCheckIntervalSeconds { get; set; } = 60;
}
