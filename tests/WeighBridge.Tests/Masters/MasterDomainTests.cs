using WeighBridge.Domain.Masters;
using Xunit;

namespace WeighBridge.Tests.Masters;

public sealed class MasterDomainTests
{
    [Fact]
    public void Vehicle_NormalisesRegistrationNumber_AndTrimsWhitespace()
    {
        var vehicle = Vehicle.Create("  mh 12  ab 1234  ", remarks: "Fleet #1");

        Assert.Equal("MH12AB1234", vehicle.VehicleNumber);
        Assert.True(vehicle.IsActive);
        Assert.Null(vehicle.VehicleTypeId);
        Assert.Null(vehicle.TareWeightKg);
        Assert.Equal("Fleet #1", vehicle.Remarks);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!@#$%^&*()")]
    public void Vehicle_RejectsInvalidRegistrationNumber(string invalidNumber)
    {
        Assert.Throws<ArgumentException>(() => Vehicle.Create(invalidNumber));
    }

    [Fact]
    public void Vehicle_RejectsNegativeOrExcessiveTareWeight()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Vehicle.Create("MH12AB1234", tareWeightKg: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Vehicle.Create("MH12AB1234", tareWeightKg: 200_001));
    }

    [Fact]
    public void Vehicle_DeactivationAndReactivation_TogglesState()
    {
        var vehicle = Vehicle.Create("KA01AB9999");
        Assert.True(vehicle.IsActive);

        vehicle.Deactivate();
        Assert.False(vehicle.IsActive);

        vehicle.Reactivate();
        Assert.True(vehicle.IsActive);
    }

    [Fact]
    public void Party_TrimsPropertiesAndEnforcesValidation()
    {
        var party = Party.Create("  Acme Steel Industries Ltd  ", code: " ACM01 ", remarks: " Preferred Vendor ");

        Assert.Equal("Acme Steel Industries Ltd", party.Name);
        Assert.Equal("ACM01", party.Code);
        Assert.Equal("Preferred Vendor", party.Remarks);
        Assert.True(party.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Party_RejectsBlankName(string invalidName)
    {
        Assert.Throws<ArgumentException>(() => Party.Create(invalidName));
    }

    [Fact]
    public void Material_TrimsPropertiesAndEnforcesValidation()
    {
        var material = Material.Create("  Iron Ore Pellets  ", code: " IO-PEL ", description: " High grade ");

        Assert.Equal("Iron Ore Pellets", material.Name);
        Assert.Equal("IO-PEL", material.Code);
        Assert.Equal("High grade", material.Description);
        Assert.True(material.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Material_RejectsBlankName(string invalidName)
    {
        Assert.Throws<ArgumentException>(() => Material.Create(invalidName));
    }

    [Fact]
    public void VehicleType_NormalisesAndEnforcesValidation()
    {
        var vt = VehicleType.Create("  10-wheeler tipper  ", description: " Heavy transport ");

        Assert.Equal("10-wheeler tipper", vt.TypeName);
        Assert.Equal("Heavy transport", vt.Description);
        Assert.True(vt.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void VehicleType_RejectsBlankTypeName(string invalidName)
    {
        Assert.Throws<ArgumentException>(() => VehicleType.Create(invalidName));
    }
}
