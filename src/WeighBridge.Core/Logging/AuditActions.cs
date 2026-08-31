namespace WeighBridge.Core.Logging;

/// <summary>
/// Authoritative catalog of standardized audit actions across all application subsystems.
/// </summary>
public static class AuditActions
{
    // Authentication & Security
    public const string Login = "Login";
    public const string LoginFailed = "LoginFailed";
    public const string Logout = "Logout";
    public const string InactivityLock = "InactivityLock";
    public const string PasswordChanged = "PasswordChanged";
    public const string UnlockUser = "UnlockUser";

    // Weighment Transactions
    public const string WeighmentCreated = "WeighmentCreated";
    public const string FirstWeightRecorded = "FirstWeightRecorded";
    public const string SecondWeightRecorded = "SecondWeightRecorded";
    public const string WeighmentCompleted = "WeighmentCompleted";
    public const string WeighmentCancelled = "WeighmentCancelled";

    // Printing & Reporting
    public const string TicketPrinted = "TicketPrinted";
    public const string Reprint = "Reprint";
    public const string ReportExported = "ReportExported";

    // Messaging
    public const string SmsEnqueued = "SmsEnqueued";
    public const string SmsSent = "SmsSent";
    public const string SmsFailed = "SmsFailed";

    // Administration & Configuration
    public const string SettingsChanged = "SettingsChanged";
    public const string UserCreated = "UserCreated";
    public const string UserDisabled = "UserDisabled";
    public const string RoleChanged = "RoleChanged";
}

/// <summary>
/// Authoritative canonical audit outcomes.
/// </summary>
public static class AuditOutcomes
{
    public const string Success = "SUCCESS";
    public const string Failure = "FAILURE";
    public const string Denied = "DENIED";
}
