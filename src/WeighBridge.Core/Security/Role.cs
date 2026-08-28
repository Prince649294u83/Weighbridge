namespace WeighBridge.Core.Security;

/// <summary>
/// A named set of permissions.
/// </summary>
/// <remarks>
/// Permissions are granted through roles rather than to individuals: a site with four
/// operators on rotating shifts wants to change what operators can do once, not four times.
/// </remarks>
public sealed class Role
{
    private readonly HashSet<string> _permissionKeys;

    /// <summary>Creates a role holding the given permissions.</summary>
    public Role(string name, IEnumerable<Permission> permissions, string description = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(permissions);

        Name = name;
        Description = description;
        _permissionKeys = [.. permissions.Select(permission => permission.Key)];
    }

    /// <summary>Role name, as persisted and displayed.</summary>
    public string Name { get; }

    /// <summary>What the role is for.</summary>
    public string Description { get; }

    /// <summary>The keys this role grants.</summary>
    public IReadOnlyCollection<string> PermissionKeys => _permissionKeys;

    /// <summary>The permissions this role grants, resolved to their descriptors.</summary>
    public IEnumerable<Permission> Permissions
        => Security.Permissions.All.Where(permission => _permissionKeys.Contains(permission.Key));

    /// <summary>Whether this role grants a permission.</summary>
    public bool Grants(Permission permission)
    {
        ArgumentNullException.ThrowIfNull(permission);

        return _permissionKeys.Contains(permission.Key);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({_permissionKeys.Count} permission(s))";
}

/// <summary>
/// The roles the application ships with.
/// </summary>
/// <remarks>
/// Fixed for now. A user management screen that lets a site define its own roles is a later
/// phase; these four cover what a weighbridge actually needs and give the permission checks
/// something real to run against in the meantime.
/// </remarks>
public static class Roles
{
    /// <summary>Everything, including settings and user management.</summary>
    public static readonly Role Administrator = new(
        nameof(Administrator),
        Security.Permissions.All,
        "Full access, including settings, users and diagnostics.");

    /// <summary>
    /// Everything operational, plus the corrections and cancellations an operator is not
    /// trusted with — but not settings or user management.
    /// </summary>
    public static readonly Role Supervisor = new(
        nameof(Supervisor),
        [
            Security.Permissions.WeighmentCreate,
            Security.Permissions.WeighmentEdit,
            Security.Permissions.WeighmentCancel,
            Security.Permissions.WeighmentReprint,
            Security.Permissions.MastersView,
            Security.Permissions.MastersEdit,
            Security.Permissions.MastersDelete,
            Security.Permissions.ReportsView,
            Security.Permissions.ReportsExport,
            Security.Permissions.AuditView,
            Security.Permissions.DiagnosticsView,
        ],
        "Day-to-day supervision: corrections, cancellations, masters and reports.");

    /// <summary>
    /// The shift role: weigh vehicles, print slips, read masters and reports.
    /// </summary>
    /// <remarks>
    /// Deliberately cannot edit or cancel a recorded weighment. That is the control that
    /// makes the audit trail worth keeping.
    /// </remarks>
    public static readonly Role Operator = new(
        nameof(Operator),
        [
            Security.Permissions.WeighmentCreate,
            Security.Permissions.WeighmentReprint,
            Security.Permissions.MastersView,
            Security.Permissions.ReportsView,
        ],
        "Weighs vehicles and issues slips.");

    /// <summary>Look, do not touch — for a gate terminal or a manager's desk.</summary>
    public static readonly Role ReadOnly = new(
        nameof(ReadOnly),
        [
            Security.Permissions.MastersView,
            Security.Permissions.ReportsView,
        ],
        "Can see masters and reports and change nothing.");

    /// <summary>
    /// The state before anyone has signed in: no permissions at all.
    /// </summary>
    /// <remarks>
    /// Not listed in <see cref="All"/> because it is not assignable to an account — it is
    /// what the terminal believes between process start and a successful sign-in. An
    /// unauthenticated session used to carry the configured default role, which meant
    /// whatever that role granted could be executed by nobody in particular; now nothing
    /// executes until somebody authenticates.
    /// </remarks>
    public static readonly Role Unauthenticated = new(
        nameof(Unauthenticated),
        [],
        "Not signed in. Nothing is permitted.");

    /// <summary>Every shipped role, most privileged first.</summary>
    public static IReadOnlyList<Role> All { get; } = [Administrator, Supervisor, Operator, ReadOnly];

    /// <summary>Finds a shipped role by name, case-insensitively.</summary>
    public static Role? FromName(string name)
        => All.FirstOrDefault(role => string.Equals(role.Name, name, StringComparison.OrdinalIgnoreCase));
}
