namespace WeighBridge.Core.Security;

/// <summary>
/// Every permission the application recognises, declared once.
/// </summary>
/// <remarks>
/// <para>
/// One permission per thing a role can be stopped from doing, matching the modules the
/// shell already navigates to. Nothing here describes how a weighment works — a permission
/// is a gate, and the rule behind the gate belongs to the module.
/// </para>
/// <para>
/// Adding a permission means adding a field here and nothing else: <see cref="All"/> is
/// reflected over the fields, so a role editor and the tests both see a new one
/// automatically.
/// </para>
/// </remarks>
public static class Permissions
{
    /// <summary>Weigh a vehicle and record the first weight.</summary>
    public static readonly Permission WeighmentCreate = new(
        "Weighment.Create", "Create weighment", PermissionGroup.Weighment,
        "Record a first or second weight and issue a slip.");

    /// <summary>Change a weighment that has already been recorded.</summary>
    public static readonly Permission WeighmentEdit = new(
        "Weighment.Edit", "Edit weighment", PermissionGroup.Weighment,
        "Correct a recorded weighment. Every change is audited.");

    /// <summary>Cancel or void a weighment.</summary>
    public static readonly Permission WeighmentCancel = new(
        "Weighment.Cancel", "Cancel weighment", PermissionGroup.Weighment,
        "Void a weighment that was recorded in error.");

    /// <summary>Reprint a slip that was already issued.</summary>
    public static readonly Permission WeighmentReprint = new(
        "Weighment.Reprint", "Reprint slip", PermissionGroup.Weighment,
        "Issue a duplicate of a slip that was already printed.");

    /// <summary>Read master data.</summary>
    public static readonly Permission MastersView = new(
        "Masters.View", "View masters", PermissionGroup.Masters,
        "Open vehicle, party, material and rate lists.");

    /// <summary>Create or change master data.</summary>
    public static readonly Permission MastersEdit = new(
        "Masters.Edit", "Edit masters", PermissionGroup.Masters,
        "Add or change vehicles, parties, materials and rates.");

    /// <summary>Remove master data.</summary>
    public static readonly Permission MastersDelete = new(
        "Masters.Delete", "Delete masters", PermissionGroup.Masters,
        "Remove a master record that is no longer in use.");

    /// <summary>Open reports.</summary>
    public static readonly Permission ReportsView = new(
        "Reports.View", "View reports", PermissionGroup.Reports,
        "Run and read the reporting screens.");

    /// <summary>Export a report to a file.</summary>
    public static readonly Permission ReportsExport = new(
        "Reports.Export", "Export reports", PermissionGroup.Reports,
        "Save report output outside the application.");

    /// <summary>Change application settings.</summary>
    public static readonly Permission SettingsEdit = new(
        "Settings.Edit", "Change settings", PermissionGroup.Administration,
        "Change application, hardware and printing settings.");

    /// <summary>Manage operators and what they may do.</summary>
    public static readonly Permission UsersManage = new(
        "Users.Manage", "Manage users", PermissionGroup.Administration,
        "Add operators and change their roles.");

    /// <summary>Read the audit trail.</summary>
    public static readonly Permission AuditView = new(
        "Audit.View", "View audit trail", PermissionGroup.Administration,
        "Read the record of who changed what.");

    /// <summary>Open the diagnostics surface.</summary>
    public static readonly Permission DiagnosticsView = new(
        "Diagnostics.View", "View diagnostics", PermissionGroup.System,
        "Read logs, health status and background task state.");

    /// <summary>
    /// Every declared permission.
    /// </summary>
    /// <remarks>
    /// Reflected over this type's fields rather than hand-listed, because a hand-written
    /// list is the kind of thing that silently loses an entry.
    /// </remarks>
    public static IReadOnlyList<Permission> All { get; } =
    [
        .. typeof(Permissions)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.FieldType == typeof(Permission))
            .Select(field => (Permission)field.GetValue(null)!)
            .OrderBy(permission => permission.Group)
            .ThenBy(permission => permission.Key, StringComparer.Ordinal),
    ];

    /// <summary>Finds a permission by its persisted key.</summary>
    /// <remarks>
    /// Case-insensitive, matching <see cref="Roles.FromName"/>. A stored role may have been
    /// hand-edited, and a case difference returning <see langword="null"/> would silently drop
    /// a permission from that role rather than fail loudly.
    /// </remarks>
    public static Permission? FromKey(string key)
        => All.FirstOrDefault(permission => string.Equals(permission.Key, key, StringComparison.OrdinalIgnoreCase));
}
