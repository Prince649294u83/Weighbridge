namespace WeighBridge.Core.Security;

/// <summary>
/// Local sign-in protection: how many wrong passwords are tolerated before the account
/// locks, and for how long.
/// </summary>
public sealed class SecurityOptions
{
    /// <summary>Configuration section these values bind from.</summary>
    public const string SectionName = "Security";

    /// <summary>
    /// Consecutive failed sign-ins allowed for one username before it locks.
    /// </summary>
    /// <remarks>Floored to at least one at resolution; zero would lock on first typo.</remarks>
    public int MaxFailedSignIns { get; set; } = 5;

    /// <summary>
    /// How long a locked username stays locked, in seconds.
    /// </summary>
    /// <remarks>Floored to at least one second; zero would make the lock meaningless.</remarks>
    public int LockoutSeconds { get; set; } = 300;

    /// <summary>Effective attempt limit, never below one.</summary>
    public int EffectiveMaxFailedSignIns => Math.Max(1, MaxFailedSignIns);

    /// <summary>Effective lock duration, never below one second.</summary>
    public TimeSpan EffectiveLockout => TimeSpan.FromSeconds(Math.Max(1, LockoutSeconds));
}
