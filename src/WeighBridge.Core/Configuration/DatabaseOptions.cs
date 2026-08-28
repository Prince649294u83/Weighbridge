namespace WeighBridge.Core.Configuration;

/// <summary>
/// Bound to the <c>Database</c> section of <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// SQLite is the shipping engine. The provider is captured as a string so a later
/// module can switch to SQL Server without changing the configuration shape.
/// </remarks>
public sealed class DatabaseOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Database";

    /// <summary>Database engine. Only <c>Sqlite</c> is implemented.</summary>
    public string Provider { get; set; } = "Sqlite";

    /// <summary>
    /// Explicit connection string. When empty, a SQLite connection pointing at
    /// <see cref="FileName"/> inside the application data root is generated.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>SQLite database file name, used when no connection string is given.</summary>
    public string FileName { get; set; } = "weighbridge.db";

    /// <summary>Command timeout applied to every database call.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Applies pending EF Core migrations during startup.</summary>
    public bool ApplyMigrationsOnStartup { get; set; } = true;

    /// <summary>Probes connectivity during startup so the status bar reflects reality.</summary>
    public bool ProbeOnStartup { get; set; } = true;

    /// <summary>Interval of the background connectivity re-check.</summary>
    public int HealthCheckIntervalSeconds { get; set; } = 30;

    /// <summary>Enables EF Core sensitive data logging. Never enable in production.</summary>
    public bool EnableSensitiveDataLogging { get; set; }
}
