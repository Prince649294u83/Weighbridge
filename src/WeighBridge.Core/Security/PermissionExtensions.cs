using WeighBridge.Core.Security;

namespace WeighBridge.Core.Security;

/// <summary>
/// Convenient extension methods for permission enforcement.
/// </summary>
public static class PermissionExtensions
{
    /// <summary>
    /// Verifies that the current operator holds the required permission, throwing an
    /// <see cref="UnauthorizedAccessException"/> if denied.
    /// </summary>
    public static void Ensure(this IPermissionService permissions, Permission permission)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(permission);

        var authorization = permissions.Authorize(permission);
        if (!authorization.IsAuthorized)
        {
            throw new UnauthorizedAccessException(
                authorization.Reason ?? $"Operator lacks required permission: {permission.Name} ({permission.Key}).");
        }
    }
}
