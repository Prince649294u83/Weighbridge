namespace WeighBridge.Core.Security;

/// <summary>
/// Area of the application a permission belongs to, used to group them on a role screen.
/// </summary>
public enum PermissionGroup
{
    /// <summary>Weighing a vehicle and issuing a slip.</summary>
    Weighment = 0,

    /// <summary>Vehicles, parties, materials, rates.</summary>
    Masters = 1,

    /// <summary>Viewing, exporting and printing reports.</summary>
    Reports = 2,

    /// <summary>Settings, users, hardware configuration.</summary>
    Administration = 3,

    /// <summary>Diagnostics, logs, background tasks.</summary>
    System = 4,
}

/// <summary>
/// One thing an operator may or may not be allowed to do.
/// </summary>
/// <remarks>
/// <para>
/// A descriptor rather than a bare string. Every permission in the application is declared
/// once in <see cref="Permissions"/> and referred to by that field afterwards, so a typo is
/// a compile error instead of a silent grant — a misspelled <c>"CanEditVehcile"</c> in a
/// string comparison denies nothing and nobody notices until an audit.
/// </para>
/// <para>
/// <see cref="Key"/> is the only part that is persisted. Name and description are display
/// text and may be reworded without invalidating a stored role.
/// </para>
/// </remarks>
/// <param name="Key">Stable identifier, persisted with a role. Never reword this.</param>
/// <param name="Name">Short display text for a role editor.</param>
/// <param name="Group">Area this permission belongs to.</param>
/// <param name="Description">What granting it allows.</param>
public sealed record Permission(
    string Key,
    string Name,
    PermissionGroup Group,
    string Description = "")
{
    /// <inheritdoc />
    public override string ToString() => Key;
}
