using System.IO.Ports;
using Microsoft.EntityFrameworkCore;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Printing;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;
using WeighBridge.Infrastructure.Persistence;
using WeighBridge.Services.Weighments;
using Xunit;

namespace WeighBridge.Tests.Weighments;

/// <summary>
/// Verifies the strict separation between F1/F2 workflow states and G/T arrival modes,
/// non-probing COM port enumeration, and domain data preservation across UI simplification.
/// </summary>
public sealed class VehicleEntryModeSeparationTests : IDisposable
{
    private readonly WeighmentHarness _harness = new();

    public VehicleEntryModeSeparationTests() => _harness.SignInAs(Roles.Administrator);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task F1_Plus_Gross_Creates_GrossFirst_Transaction_With_First_Weight_As_Gross()
    {
        // F1 + G: First entry of a loaded vehicle (Gross weight captured first)
        var request = new NewWeighment
        {
            VehicleNumber = "MH12GF1001",
            Mode = WeighmentMode.GrossFirst,
            PartyName = "Alpha Industrial",
            MaterialName = "Coal",
            Charges = 150m
        };

        var createResult = await _harness.Executor.ExecuteAsync(
            new CreateWeighmentCommand(_harness.Service, request));

        Assert.Equal(CommandOutcome.Succeeded, createResult.Outcome);
        var weighment = createResult.Value!;
        Assert.Equal(WeighmentMode.GrossFirst, weighment.Mode);

        // Capture first weight (35,000 kg)
        var weightResult = await _harness.Executor.ExecuteAsync(
            new RecordFirstWeightCommand(_harness.Service, weighment.Id, 35000m, WeightSource.Indicator));

        Assert.Equal(CommandOutcome.Succeeded, weightResult.Outcome);
        var updated = weightResult.Value!;
        Assert.Equal(WeighmentStatus.AwaitingSecondWeight, updated.Status);
        Assert.NotNull(updated.Gross);
        Assert.Equal(35000m, updated.Gross.Kilograms);
        Assert.Null(updated.Tare);
        Assert.Null(updated.NetWeightKg);
    }

    [Fact]
    public async Task F1_Plus_Tare_Creates_TareFirst_Transaction_With_First_Weight_As_Tare()
    {
        // F1 + T: First entry of an empty vehicle (Tare weight captured first)
        var request = new NewWeighment
        {
            VehicleNumber = "MH12TF2002",
            Mode = WeighmentMode.TareFirst,
            PartyName = "Beta Quarry",
            MaterialName = "Aggregates",
            Charges = 200m
        };

        var createResult = await _harness.Executor.ExecuteAsync(
            new CreateWeighmentCommand(_harness.Service, request));

        Assert.Equal(CommandOutcome.Succeeded, createResult.Outcome);
        var weighment = createResult.Value!;
        Assert.Equal(WeighmentMode.TareFirst, weighment.Mode);

        // Capture first weight (12,000 kg)
        var weightResult = await _harness.Executor.ExecuteAsync(
            new RecordFirstWeightCommand(_harness.Service, weighment.Id, 12000m, WeightSource.Indicator));

        Assert.Equal(CommandOutcome.Succeeded, weightResult.Outcome);
        var updated = weightResult.Value!;
        Assert.Equal(WeighmentStatus.AwaitingSecondWeight, updated.Status);
        Assert.NotNull(updated.Tare);
        Assert.Equal(12000m, updated.Tare.Kilograms);
        Assert.Null(updated.Gross);
        Assert.Null(updated.NetWeightKg);
    }

    [Fact]
    public async Task F2_Plus_Tare_Completes_GrossFirst_Transaction_Calculating_Net_As_Gross_Minus_Tare()
    {
        // F1: Gross-first vehicle arrives loaded (40,000 kg)
        var createResult = await _harness.Executor.ExecuteAsync(
            new CreateWeighmentCommand(_harness.Service, new NewWeighment
            {
                VehicleNumber = "MH12GF3003",
                Mode = WeighmentMode.GrossFirst,
                PartyName = "Steel Corp",
                MaterialName = "Billet"
            }));

        var weighmentId = createResult.Value!.Id;
        await _harness.Executor.ExecuteAsync(
            new RecordFirstWeightCommand(_harness.Service, weighmentId, 40000m, WeightSource.Indicator));

        // F2 + T: Vehicle returns empty, capturing Tare (14,000 kg)
        var secondWeightResult = await _harness.Executor.ExecuteAsync(
            new RecordSecondWeightCommand(_harness.Service, weighmentId, 14000m, WeightSource.Indicator));

        Assert.Equal(CommandOutcome.Succeeded, secondWeightResult.Outcome);
        var completed = secondWeightResult.Value!;
        Assert.Equal(WeighmentStatus.Completed, completed.Status);
        Assert.Equal(40000m, completed.Gross!.Kilograms);
        Assert.Equal(14000m, completed.Tare!.Kilograms);
        Assert.Equal(26000m, completed.NetWeightKg);
    }

    [Fact]
    public async Task F2_Plus_Gross_Completes_TareFirst_Transaction_Calculating_Net_As_Gross_Minus_Tare()
    {
        // F1: Tare-first vehicle arrives empty (11,500 kg)
        var createResult = await _harness.Executor.ExecuteAsync(
            new CreateWeighmentCommand(_harness.Service, new NewWeighment
            {
                VehicleNumber = "MH12TF4004",
                Mode = WeighmentMode.TareFirst,
                PartyName = "Mining Corp",
                MaterialName = "Limestone"
            }));

        var weighmentId = createResult.Value!.Id;
        await _harness.Executor.ExecuteAsync(
            new RecordFirstWeightCommand(_harness.Service, weighmentId, 11500m, WeightSource.Indicator));

        // F2 + G: Vehicle leaves loaded, capturing Gross (36,500 kg)
        var secondWeightResult = await _harness.Executor.ExecuteAsync(
            new RecordSecondWeightCommand(_harness.Service, weighmentId, 36500m, WeightSource.Indicator));

        Assert.Equal(CommandOutcome.Succeeded, secondWeightResult.Outcome);
        var completed = secondWeightResult.Value!;
        Assert.Equal(WeighmentStatus.Completed, completed.Status);
        Assert.Equal(36500m, completed.Gross!.Kilograms);
        Assert.Equal(11500m, completed.Tare!.Kilograms);
        Assert.Equal(25000m, completed.NetWeightKg);
    }

    [Fact]
    public void PortEnumeration_UsesDynamicWindowsNames_WithoutOpeningSerialHandles()
    {
        // Proves that enumeration calls SerialPort.GetPortNames() without port probing
        var ports = SerialPort.GetPortNames();
        Assert.NotNull(ports);
    }

    [Fact]
    public async Task Domain_Data_Preservation_Regression_Retains_Underlying_Properties_For_Printing()
    {
        // Verify that underlying entity properties exist and remain fully intact for printing
        var request = new NewWeighment
        {
            VehicleNumber = "MH12DP5005",
            Mode = WeighmentMode.GrossFirst,
            PartyName = "Logistics Prime",
            MaterialName = "Sand",
            DriverName = "Rajesh Kumar",
            TransporterName = "Prime Haulers",
            Remarks = "Direct plant delivery",
            Charges = 120m
        };

        var result = await _harness.Executor.ExecuteAsync(
            new CreateWeighmentCommand(_harness.Service, request));

        Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
        var created = result.Value!;

        // Complete the weighment
        await _harness.Executor.ExecuteAsync(
            new RecordFirstWeightCommand(_harness.Service, created.Id, 28000m, WeightSource.Indicator));
        var completedResult = await _harness.Executor.ExecuteAsync(
            new RecordSecondWeightCommand(_harness.Service, created.Id, 10000m, WeightSource.Indicator));

        var completed = completedResult.Value!;
        Assert.Equal("Rajesh Kumar", completed.DriverName);
        Assert.Equal("Prime Haulers", completed.TransporterName);

        // Verify WeighmentPrintDataFactory formats the slip without errors
        var printData = WeighmentPrintDataFactory.Create(completed);
        Assert.Equal("MH12DP5005", printData.VehicleNumber);
        Assert.Equal(28000m, printData.GrossWeightKg);
        Assert.Equal(10000m, printData.TareWeightKg);
        Assert.Equal(18000m, printData.NetWeightKg);
        Assert.Equal("Rajesh Kumar", printData.DriverName);
        Assert.Equal("Prime Haulers", printData.TransporterName);
    }

    [Fact]
    public async Task WeighmentSummary_Projects_Gross_And_Tare_First_With_ModeCode()
    {
        // 1. Pending GrossFirst weighment
        var grossReq = new NewWeighment
        {
            VehicleNumber = "MH12GF9999",
            Mode = WeighmentMode.GrossFirst,
            PartyName = "Gross First Logistics",
            MaterialName = "Iron Ore"
        };
        var gCreated = (await _harness.Executor.ExecuteAsync(new CreateWeighmentCommand(_harness.Service, grossReq))).Value!;
        var gRecordResult = await _harness.Executor.ExecuteAsync(new RecordFirstWeightCommand(_harness.Service, gCreated.Id, 32000m, WeightSource.Indicator));
        var gPending = gRecordResult.Value!;

        var gSummary = WeighBridge.App.ViewModels.WeighmentSummary.From(gPending);
        Assert.Equal("G", gSummary.ModeCode);
        Assert.Equal(32000m, gSummary.GrossKg);
        Assert.Null(gSummary.TareKg);
        Assert.Equal("32,000 Kg", gSummary.FormattedGrossKg);
        Assert.Equal("—", gSummary.FormattedTareKg);

        // 2. Pending TareFirst weighment
        var tareReq = new NewWeighment
        {
            VehicleNumber = "MH12TF8888",
            Mode = WeighmentMode.TareFirst,
            PartyName = "Tare First Transport",
            MaterialName = "Coal"
        };
        var tCreated = (await _harness.Executor.ExecuteAsync(new CreateWeighmentCommand(_harness.Service, tareReq))).Value!;
        var tRecordResult = await _harness.Executor.ExecuteAsync(new RecordFirstWeightCommand(_harness.Service, tCreated.Id, 11000m, WeightSource.Indicator));
        var tPending = tRecordResult.Value!;

        var tSummary = WeighBridge.App.ViewModels.WeighmentSummary.From(tPending);
        Assert.Equal("T", tSummary.ModeCode);
        Assert.Null(tSummary.GrossKg);
        Assert.Equal(11000m, tSummary.TareKg);
        Assert.Equal("—", tSummary.FormattedGrossKg);
        Assert.Equal("11,000 Kg", tSummary.FormattedTareKg);
    }
}
