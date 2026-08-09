namespace WeighBridge.Core.Logging;

/// <summary>
/// Category names for the application's loggers.
/// </summary>
/// <remarks>
/// Constants rather than <c>ILogger&lt;T&gt;</c> type names because these categories are
/// an operational contract: a support engineer filters a field log by
/// <c>WeighBridge.Hardware</c> to see what the indicator did, and that name must not
/// change because a class was renamed.
/// </remarks>
public static class LogCategory
{
    /// <summary>Prefix shared by every application category.</summary>
    public const string Prefix = "WeighBridge";

    /// <summary>General application lifecycle and module activity.</summary>
    public const string Application = Prefix + ".Application";

    /// <summary>Who did what to which record. Retained for compliance, never filtered out.</summary>
    public const string Audit = Prefix + ".Audit";

    /// <summary>Weighbridge indicator, camera and printer traffic.</summary>
    public const string Hardware = Prefix + ".Hardware";

    /// <summary>Database operations, timings and transaction outcomes.</summary>
    public const string Database = Prefix + ".Database";

    /// <summary>Operator interaction: navigation, commands, dialogs.</summary>
    public const string UserInterface = Prefix + ".UI";
}
