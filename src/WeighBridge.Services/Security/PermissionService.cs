using System.ComponentModel;
using WeighBridge.Core.Application;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Security;

namespace WeighBridge.Services.Security;

/// <summary>
/// Answers permission questions for the operator currently at the terminal.
/// </summary>
/// <remarks>
/// <para>
/// The identity is whoever signed in at the login dialog, and the role is the one their
/// account carries. Before that — the window between the process starting and a successful
/// sign-in — the role is <see cref="Roles.Unauthenticated"/>, which grants nothing. An
/// unauthenticated session used to carry a configured default role; whatever that role
/// granted could then be executed by nobody in particular, which is exactly what a
/// permission system must not allow.
/// </para>
/// <para>
/// No WPF anywhere: a test constructs this with a stub application-info service and asserts
/// on denials without a dispatcher.
/// </para>
/// </remarks>
public sealed class PermissionService : IPermissionService
{
    private readonly IApplicationLogger _logger;
    private readonly SignedInOperator _signedInOperator;
    private readonly string _windowsUserName;
    private readonly object _gate = new();

    private OperatorIdentity? _current;

    /// <summary>Creates the service with no authority at all, until someone signs in.</summary>
    public PermissionService(
        IApplicationInfoService applicationInfo,
        IApplicationLogger logger,
        SignedInOperator signedInOperator)
    {
        ArgumentNullException.ThrowIfNull(applicationInfo);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(signedInOperator);

        _logger = logger;
        _signedInOperator = signedInOperator;

        var userName = applicationInfo.CurrentUserName;
        _windowsUserName = userName;

        // The Windows account names who is sitting at the terminal; it grants them nothing.
        _current = new OperatorIdentity(userName, userName, Roles.Unauthenticated);

        _logger.Information(
            "Permission service started for {User}; unauthenticated until sign-in", userName);
    }

    /// <inheritdoc />
    public OperatorIdentity CurrentOperator
    {
        get
        {
            lock (_gate)
            {
                return _current ?? new OperatorIdentity("unknown", "unknown", Roles.Unauthenticated);
            }
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc />
    public event EventHandler<OperatorChangedEventArgs>? OperatorChanged;

    /// <inheritdoc />
    public bool HasPermission(Permission permission)
    {
        ArgumentNullException.ThrowIfNull(permission);

        return CurrentOperator.Role.Grants(permission);
    }

    /// <inheritdoc />
    public bool HasAllPermissions(params Permission[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var role = CurrentOperator.Role;

        return permissions.All(role.Grants);
    }

    /// <inheritdoc />
    public bool HasAnyPermission(params Permission[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var role = CurrentOperator.Role;

        return permissions.Any(role.Grants);
    }

    /// <inheritdoc />
    public AuthorizationResult Authorize(Permission permission)
    {
        ArgumentNullException.ThrowIfNull(permission);

        var operatorIdentity = CurrentOperator;

        if (operatorIdentity.Role.Grants(permission))
        {
            return AuthorizationResult.Allowed;
        }

        // Logged here rather than left to the caller: a run of denials is the signal that
        // someone is working around a control, and it has to be in the log even when the
        // caller decided to say nothing to the operator.
        _logger.Warning(
            "Permission denied: {User} as {Role} lacks {Permission}",
            operatorIdentity.UserName,
            operatorIdentity.Role.Name,
            permission.Key);

        return AuthorizationResult.Denied(permission);
    }

    /// <inheritdoc />
    public AuthorizationResult Authorize(object candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        // Anything that declares no requirement is allowed. A command is not made
        // privileged by forgetting to say so, and the pipeline logs what it ran regardless.
        return candidate is IRequiresPermission { RequiredPermission: { } required }
            ? Authorize(required)
            : AuthorizationResult.Allowed;
    }

    /// <inheritdoc />
    public void SetOperator(OperatorIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        OperatorIdentity previous;

        lock (_gate)
        {
            if (_current == identity)
            {
                return;
            }

            previous = _current ?? new OperatorIdentity("unknown", "unknown", Roles.Unauthenticated);
            _current = identity;
        }

        // Published before the log write below, so that write - and every one after it -
        // names the operator who is signed in now. Every log and audit entry used to carry
        // the Windows account the terminal runs under instead, which on a terminal several
        // operators share meant the audit trail attributed every action to the same name.
        _signedInOperator.UserName = identity.UserName;

        // Outside the lock: a handler that asked a permission question back would deadlock.
        _logger.Information(
            "Operator changed from {Previous} to {Current} as {Role}",
            previous.UserName,
            identity.UserName,
            identity.Role.Name);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentOperator)));
        OperatorChanged?.Invoke(this, new OperatorChangedEventArgs(previous, identity));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Rebuilds the identity the constructor built rather than nulling <c>_current</c>: the
    /// terminal is still in front of someone, and the audit trail should say which Windows
    /// account it happened on. The role is what changes, and it changes to the one that
    /// grants nothing - so every <see cref="Authorize(Permission)"/> after this denies, and
    /// the shell that gets rebuilt for the next sign-in offers no privileged module.
    /// </remarks>
    public void SignOut() =>
        SetOperator(new OperatorIdentity(_windowsUserName, _windowsUserName, Roles.Unauthenticated));
}
