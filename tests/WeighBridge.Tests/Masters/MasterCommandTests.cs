using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Events.Catalog;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Masters;
using WeighBridge.Services.Masters;
using Xunit;

namespace WeighBridge.Tests.Masters;

public sealed class MasterCommandTests
{
    [Fact]
    public async Task CreateVehicleCommand_RequiresPermission()
    {
        using var harness = new MasterHarness();
        harness.SignInAs(Roles.ReadOnly);

        var cmd = new CreateVehicleCommand(harness.VehicleService, new CreateVehicleRequest("MH12AB1234"));
        var result = await harness.Executor.ExecuteAsync(cmd);

        Assert.Equal(CommandOutcome.Denied, result.Outcome);
    }

    [Fact]
    public async Task CreateVehicleCommand_ValidationFails_ForInvalidInput()
    {
        using var harness = new MasterHarness();
        harness.SignInAs(Roles.Administrator);

        var cmd = new CreateVehicleCommand(harness.VehicleService, new CreateVehicleRequest(
            "",
            TareWeightKg: -500));

        var result = await harness.Executor.ExecuteAsync(cmd);

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.NotNull(result.Validation);
        Assert.Contains(result.Validation.Blocking, f => f.PropertyName == nameof(CreateVehicleRequest.VehicleNumber));
        Assert.Contains(result.Validation.Blocking, f => f.PropertyName == nameof(CreateVehicleRequest.TareWeightKg));
    }

    [Fact]
    public async Task CreateVehicleCommand_PublishesEvent_AndSucceeds()
    {
        using var harness = new MasterHarness();
        harness.SignInAs(Roles.Administrator);

        var eventsReceived = new List<VehicleCreatedEvent>();
        using var sub = harness.Events.Subscribe<VehicleCreatedEvent>(e => eventsReceived.Add(e));

        var cmd = new CreateVehicleCommand(harness.VehicleService, new CreateVehicleRequest("GJ01XY7777", TareWeightKg: 8500m));
        var result = await harness.Executor.ExecuteAsync(cmd);

        Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
        Assert.NotNull(result.Value);
        Assert.Equal("GJ01XY7777", result.Value.VehicleNumber);

        Assert.Single(eventsReceived);
        Assert.Equal(result.Value.Id, eventsReceived[0].Id);
        Assert.Equal("GJ01XY7777", eventsReceived[0].VehicleNumber);
    }

    [Fact]
    public async Task DeactivateAndReactivateVehicle_RequiresMastersDeleteAndEdit()
    {
        using var harness = new MasterHarness();
        harness.SignInAs(Roles.Administrator);

        var v = await harness.VehicleService.CreateAsync(new CreateVehicleRequest("RJ14AB3333"));

        // Operator without delete permission cannot deactivate
        harness.SignInAs(Roles.Operator);
        var deactResult = await harness.Executor.ExecuteAsync(new DeactivateVehicleCommand(harness.VehicleService, v.Id));
        Assert.Equal(CommandOutcome.Denied, deactResult.Outcome);

        // Supervisor with delete permission can deactivate
        harness.SignInAs(Roles.Supervisor);
        var deactSuccess = await harness.Executor.ExecuteAsync(new DeactivateVehicleCommand(harness.VehicleService, v.Id));
        Assert.Equal(CommandOutcome.Succeeded, deactSuccess.Outcome);

        // Reactivate
        var reactSuccess = await harness.Executor.ExecuteAsync(new ReactivateVehicleCommand(harness.VehicleService, v.Id));
        Assert.Equal(CommandOutcome.Succeeded, reactSuccess.Outcome);
    }

    [Fact]
    public async Task PartyAndMaterialCommands_SucceedWithAuditAndEvents()
    {
        using var harness = new MasterHarness();
        harness.SignInAs(Roles.Administrator);

        var partyEvents = new List<PartyCreatedEvent>();
        using var pSub = harness.Events.Subscribe<PartyCreatedEvent>(e => partyEvents.Add(e));

        var materialEvents = new List<MaterialCreatedEvent>();
        using var mSub = harness.Events.Subscribe<MaterialCreatedEvent>(e => materialEvents.Add(e));

        var pCmd = new CreatePartyCommand(harness.PartyService, new CreatePartyRequest("Apex Logistics", Code: "APX"));
        var pResult = await harness.Executor.ExecuteAsync(pCmd);
        Assert.Equal(CommandOutcome.Succeeded, pResult.Outcome);
        Assert.Single(partyEvents);

        var mCmd = new CreateMaterialCommand(harness.MaterialService, new CreateMaterialRequest("Bauxite", Code: "BAX"));
        var mResult = await harness.Executor.ExecuteAsync(mCmd);
        Assert.Equal(CommandOutcome.Succeeded, mResult.Outcome);
        Assert.Single(materialEvents);
    }
}
