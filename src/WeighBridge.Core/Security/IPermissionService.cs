using System.ComponentModel;

namespace WeighBridge.Core.Security;

/// <summary>
/// Who is using the terminal.
/// </summary>
/// <remarks>
/// Not a database record: a projection of one. It is set from the account that signed in at
/// the login dialog, and before anyone has signed in it names the Windows account the shell
/// is running under. <see cref="Role"/> is what the permission checks actually consult.
/// </remarks>
/// <param name="UserName">Account name, as it appears in the log and the audit trail.</param>
/// <param name="DisplayName">Name to show in the shell.</param>
/// <param name="Role">The role whose permissions apply.</param>
public sealed record OperatorIdentity(string UserName, string DisplayName, Role Role)
{
    /// <inheritdoc />
    public override string ToString() => $"{DisplayName} ({Role.Name})";
}

/// <summary>
/// Answers whether the current operator may do something.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately small. It knows who is signed in and what their role grants, and nothing
/// about how they proved it — so the login screen that arrives later changes the
/// implementation and not a single caller.
/// </para>
/// <para>
/// Raises change notification so a menu item's enabled state can bind straight to a
/// permission check and re-evaluate when the operator changes.
/// </para>
/// </remarks>
public interface IPermissionService : INotifyPropertyChanged
{
    /// <summary>Who is currently using the terminal.</summary>
    OperatorIdentity CurrentOperator { get; }

    /// <summary>Raised when the signed-in operator or their role changes.</summary>
    event EventHandler<OperatorChangedEventArgs>? OperatorChanged;

    /// <summary>Whether the current operator holds a permission.</summary>
    bool HasPermission(Permission permission);

    /// <summary>Whether the current operator holds every one of these permissions.</summary>
    bool HasAllPermissions(params Permission[] permissions);

    /// <summary>Whether the current operator holds at least one of these permissions.</summary>
    bool HasAnyPermission(params Permission[] permissions);

    /// <summary>
    /// Checks a permission and explains a denial.
    /// </summary>
    /// <remarks>
    /// What the command pipeline calls. <see cref="HasPermission"/> is for binding a
    /// button's enabled state, where there is nowhere to put a reason.
    /// </remarks>
    AuthorizationResult Authorize(Permission permission);

    /// <summary>
    /// Checks the permission something declares through <see cref="IRequiresPermission"/>,
    /// allowing anything that declares none.
    /// </summary>
    AuthorizationResult Authorize(object candidate);

    /// <summary>
    /// Changes who is signed in.
    /// </summary>
    /// <remarks>
    /// Called by <see cref="IAuthenticationService"/> when a sign-in succeeds. The
    /// implementation also publishes the new name to <see cref="SignedInOperator"/>, so the
    /// log and the audit trail name the operator rather than the Windows account.
    /// </remarks>
    void SetOperator(OperatorIdentity identity);

    /// <summary>
    /// Returns the terminal to the state it was in before anyone signed in: the Windows
    /// account is named, and <see cref="Roles.Unauthenticated"/> grants nothing.
    /// </summary>
    /// <remarks>
    /// Not <c>SetOperator(someUnauthenticatedIdentity)</c> from the caller, because the
    /// caller would have to invent the identity - and an identity invented with the wrong
    /// role is a signed-out session that can still execute permissions. The service that
    /// built the pre-login identity is the one that can rebuild it.
    /// </remarks>
    void SignOut();
}

/// <summary>Carries the operator before and after a change.</summary>
public sealed class OperatorChangedEventArgs(OperatorIdentity previous, OperatorIdentity current) : EventArgs
{
    /// <summary>Who was signed in before.</summary>
    public OperatorIdentity Previous { get; } = previous;

    /// <summary>Who is signed in now.</summary>
    public OperatorIdentity Current { get; } = current;
}

/// <summary>
/// Implemented by a command that may only be run by an operator holding a given permission.
/// </summary>
/// <remarks>
/// Metadata rather than a check: the command declares what it needs and the pipeline
/// enforces it, so a command cannot forget to ask and no command contains an
/// <c>if (!allowed) return</c> of its own.
/// </remarks>
public interface IRequiresPermission
{
    /// <summary>The permission required, or <c>null</c> when anyone may run this.</summary>
    Permission? RequiredPermission { get; }
}
