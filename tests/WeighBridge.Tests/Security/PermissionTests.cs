using WeighBridge.Core.Security;

namespace WeighBridge.Tests.Security;

/// <summary>
/// Covers the permission framework: role definitions, authorization, operator
/// identity switching and the declarative IRequiresPermission facet.
/// </summary>
public sealed class PermissionTests
{
    [Fact]
    public void AllPermissions_ContainsEveryDeclaredPermission()
    {
        var all = Permissions.All;

        Assert.Equal(13, all.Count);
        Assert.Contains(Permissions.WeighmentCreate, all);
        Assert.Contains(Permissions.DiagnosticsView, all);
    }

    [Fact]
    public void Permissions_AreGrouped()
    {
        var byGroup = Permissions.All.GroupBy(p => p.Group).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(4, byGroup[PermissionGroup.Weighment]);
        Assert.Equal(3, byGroup[PermissionGroup.Masters]);
        Assert.Equal(2, byGroup[PermissionGroup.Reports]);
        Assert.Equal(3, byGroup[PermissionGroup.Administration]);
        Assert.Equal(1, byGroup[PermissionGroup.System]);
    }

    [Fact]
    public void FromKey_FindsPermissionByKey()
    {
        var permission = Permissions.FromKey("Weighment.Create");

        Assert.Equal(Permissions.WeighmentCreate, permission);
    }

    [Fact]
    public void FromKey_CaseInsensitive()
    {
        // A stored role may have been hand-edited. A case difference must not silently drop
        // the permission from that role.
        Assert.Equal(Permissions.WeighmentCreate, Permissions.FromKey("weighment.CREATE"));
    }

    [Fact]
    public void FromKey_EveryDeclaredKeyRoundTrips()
    {
        // Guards the reflection in All and the lookup together: a permission added later is
        // findable by the key that gets persisted for it, without touching this test.
        Assert.All(Permissions.All, permission =>
            Assert.Equal(permission, Permissions.FromKey(permission.Key)));
    }

    [Fact]
    public void FromKey_UnknownKey_ReturnsNull()
    {
        Assert.Null(Permissions.FromKey("Weighment.LaunchMissile"));
    }

    [Fact]
    public void Role_Administrator_GrantsEveryPermission()
    {
        var role = Roles.Administrator;

        Assert.All(Permissions.All, permission => Assert.True(role.Grants(permission)));
    }

    [Fact]
    public void Role_Operator_CannotEditOrCancel()
    {
        var role = Roles.Operator;

        Assert.True(role.Grants(Permissions.WeighmentCreate));
        Assert.False(role.Grants(Permissions.WeighmentEdit));
        Assert.False(role.Grants(Permissions.WeighmentCancel));
        Assert.False(role.Grants(Permissions.UsersManage));
    }

    [Fact]
    public void Role_ReadOnly_CannotWriteAnything()
    {
        var role = Roles.ReadOnly;

        Assert.True(role.Grants(Permissions.MastersView));
        Assert.True(role.Grants(Permissions.ReportsView));
        Assert.False(role.Grants(Permissions.WeighmentCreate));
        Assert.False(role.Grants(Permissions.MastersEdit));
    }

    [Fact]
    public void FromName_FindsRoleByName()
    {
        Assert.Equal(Roles.Supervisor, Roles.FromName("Supervisor"));
    }

    [Fact]
    public void FromName_CaseInsensitive()
    {
        Assert.Equal(Roles.Operator, Roles.FromName("operator"));
    }

    [Fact]
    public void FromName_UnknownName_ReturnsNull()
    {
        Assert.Null(Roles.FromName("Guest"));
    }

    [Fact]
    public void AuthorizationResult_AllowedIsAuthorized()
    {
        var result = AuthorizationResult.Allowed;

        Assert.True(result.IsAuthorized);
        Assert.Null(result.MissingPermission);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void AuthorizationResult_Denied_CarriesReasonAndPermission()
    {
        var result = AuthorizationResult.Denied(Permissions.WeighmentEdit);

        Assert.False(result.IsAuthorized);
        Assert.Equal(Permissions.WeighmentEdit, result.MissingPermission);
        Assert.Contains("edit weighment", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthorizationResult_DeniedWithCustomReason()
    {
        var result = AuthorizationResult.Denied("Operator is locked out.");

        Assert.False(result.IsAuthorized);
        Assert.Null(result.MissingPermission);
        Assert.Equal("Operator is locked out.", result.Reason);
    }
}
