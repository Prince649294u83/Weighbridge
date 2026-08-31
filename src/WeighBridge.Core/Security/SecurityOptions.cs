namespace WeighBridge.Core.Security;

/// <summary>
/// Local security configuration: failed sign-in lockout policy, password age policy, and session inactivity timeout.
/// </summary>
public sealed class SecurityOptions
{
    /// <summary>Configuration section these values bind from.</summary>
    public const string SectionName = "Security";

    /// <summary>
    /// Consecutive failed sign-ins allowed for one username before it locks.
    /// </summary>
    public int MaxFailedSignIns { get; set; } = 5;

    /// <summary>
    /// How long a locked username stays locked, in seconds (default 900s = 15m).
    /// </summary>
    public int LockoutSeconds { get; set; } = 900;

    /// <summary>
    /// Inactivity timeout in minutes before the terminal automatically locks (default 30 min).
    /// </summary>
    public int InactivityTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// Maximum days a password remains valid before mandatory password change is required (default 90 days).
    /// </summary>
    public int RequirePasswordChangeDays { get; set; } = 90;

    /// <summary>Effective attempt limit, never below one.</summary>
    public int EffectiveMaxFailedSignIns => Math.Max(1, MaxFailedSignIns);

    /// <summary>Effective lock duration, never below one second.</summary>
    public TimeSpan EffectiveLockout => TimeSpan.FromSeconds(Math.Max(1, LockoutSeconds));

    /// <summary>Effective inactivity timeout, never below one minute.</summary>
    public TimeSpan EffectiveInactivityTimeout => TimeSpan.FromMinutes(Math.Max(1, InactivityTimeoutMinutes));
}
