namespace WeighBridge.Core.Security;

/// <summary>
/// The answer to "may this operator do this?".
/// </summary>
/// <remarks>
/// A result rather than a thrown exception or a bare <c>bool</c>. A denial is a normal
/// outcome that has to be told apart from invalid data and from a failed operation: the
/// operator needs "you are not allowed to cancel a weighment, ask your supervisor", which is
/// a different sentence from "the tare weight is missing" and a different sentence again
/// from "the database did not respond".
/// </remarks>
public sealed class AuthorizationResult
{
    private static readonly AuthorizationResult AllowedInstance = new(true, null, null);

    private AuthorizationResult(bool isAuthorized, Permission? missing, string? reason)
    {
        IsAuthorized = isAuthorized;
        MissingPermission = missing;
        Reason = reason;
    }

    /// <summary>An allowed result.</summary>
    public static AuthorizationResult Allowed => AllowedInstance;

    /// <summary>True when the operation may proceed.</summary>
    public bool IsAuthorized { get; }

    /// <summary>The permission that was missing, when the denial was about one.</summary>
    public Permission? MissingPermission { get; }

    /// <summary>What to tell the operator. Set only on a denial.</summary>
    public string? Reason { get; }

    /// <summary>Denies for a missing permission.</summary>
    public static AuthorizationResult Denied(Permission permission, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(permission);

        return new AuthorizationResult(
            false,
            permission,
            reason ?? $"You do not have permission to {permission.Name.ToLowerInvariant()}.");
    }

    /// <summary>Denies for a reason that is not a single missing permission.</summary>
    public static AuthorizationResult Denied(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new AuthorizationResult(false, null, reason);
    }

    /// <inheritdoc />
    public override string ToString() => IsAuthorized ? "Allowed" : $"Denied: {Reason}";
}
