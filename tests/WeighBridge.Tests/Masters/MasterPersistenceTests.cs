using Microsoft.EntityFrameworkCore;
using WeighBridge.Core.Abstractions;
using WeighBridge.Domain.Masters;
using Xunit;

namespace WeighBridge.Tests.Masters;

public sealed class MasterPersistenceTests
{
    [Fact]
    public async Task Vehicle_PersistsAndLoadsWithTareWeightConversion()
    {
        using var harness = new MasterHarness();

        var vt = await harness.VehicleTypeService.CreateAsync(new CreateVehicleTypeRequest("16-WHEELER"));
        var vehicle = await harness.VehicleService.CreateAsync(new CreateVehicleRequest(
            "MH12AB1234",
            vt.Id,
            TareWeightKg: 12500.5m,
            Remarks: "Unit #42"));

        await using var context = harness.CreateContext();
        var reloaded = await context.Set<Vehicle>().FirstOrDefaultAsync(v => v.Id == vehicle.Id);

        Assert.NotNull(reloaded);
        Assert.Equal("MH12AB1234", reloaded.VehicleNumber);
        Assert.Equal(vt.Id, reloaded.VehicleTypeId);
        Assert.Equal(12500.5m, reloaded.TareWeightKg);
        Assert.Equal("Unit #42", reloaded.Remarks);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task Vehicle_FilteredUniqueIndex_PreventsDuplicateActiveRegistrations()
    {
        using var harness = new MasterHarness();

        await harness.VehicleService.CreateAsync(new CreateVehicleRequest("KA01AB1111"));

        // Attempting to create duplicate active vehicle throws
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.VehicleService.CreateAsync(new CreateVehicleRequest("KA 01 AB 1111")));
    }

    [Fact]
    public async Task Vehicle_Deactivation_AllowsReusingRegistration()
    {
        using var harness = new MasterHarness();

        var v1 = await harness.VehicleService.CreateAsync(new CreateVehicleRequest("DL01XY9999"));
        await harness.VehicleService.DeactivateAsync(v1.Id);

        // Now creating a new active vehicle with the same registration should succeed
        var v2 = await harness.VehicleService.CreateAsync(new CreateVehicleRequest("DL01XY9999"));
        Assert.NotEqual(v1.Id, v2.Id);
        Assert.True(v2.IsActive);
    }

    [Fact]
    public async Task Party_CRUD_AndUniqueConstraint()
    {
        using var harness = new MasterHarness();

        var party = await harness.PartyService.CreateAsync(new CreatePartyRequest(
            "Tata Steel Limited",
            Code: "TSL",
            ContactNumber: "022-12345678",
            Email: "dispatch@tatasteel.com"));

        Assert.True(party.Id > 0);

        // Duplicate active party name throws
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.PartyService.CreateAsync(new CreatePartyRequest("Tata Steel Limited")));

        // Update party
        var updated = await harness.PartyService.UpdateAsync(new UpdatePartyRequest(
            party.Id,
            "Tata Steel Ltd",
            Code: "TSL01",
            Remarks: "Updated branch"));

        Assert.Equal("Tata Steel Ltd", updated.Name);
        Assert.Equal("TSL01", updated.Code);

        // Deactivate and Reactivate
        await harness.PartyService.DeactivateAsync(party.Id);
        var deactivated = await harness.PartyService.GetByIdAsync(party.Id);
        Assert.NotNull(deactivated);
        Assert.False(deactivated.IsActive);

        await harness.PartyService.ReactivateAsync(party.Id);
        var reactivated = await harness.PartyService.GetByIdAsync(party.Id);
        Assert.NotNull(reactivated);
        Assert.True(reactivated.IsActive);
    }

    [Fact]
    public async Task Material_CRUD_AndSearch()
    {
        using var harness = new MasterHarness();

        await harness.MaterialService.CreateAsync(new CreateMaterialRequest("Coal Grade A", "C-01"));
        await harness.MaterialService.CreateAsync(new CreateMaterialRequest("Iron Ore", "IO-01"));
        var m3 = await harness.MaterialService.CreateAsync(new CreateMaterialRequest("Coal Grade B", "C-02"));

        await harness.MaterialService.DeactivateAsync(m3.Id);

        // Search active only
        var activeCoal = await harness.MaterialService.SearchAsync("Coal", includeInactive: false);
        Assert.Single(activeCoal);
        Assert.Equal("Coal Grade A", activeCoal[0].Name);

        // Search including inactive
        var allCoal = await harness.MaterialService.SearchAsync("Coal", includeInactive: true);
        Assert.Equal(2, allCoal.Count);
    }
}
