using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;

namespace WeighBridge.Tests.Weighments;

public sealed class InputSettingsRuntimeConsumerTests
{
    [Fact]
    public async Task InputSettings_ManualTare_On()
    {
        using var harness = new WeighmentHarness(Options.Create(new WeighmentOptions { ManualTareEntry = true }));
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12MTON01" });

        var recorded = await harness.Service.RecordFirstWeightAsync(open.Id, 10_000m, WeightSource.Manual);

        Assert.Equal(WeighmentStatus.AwaitingSecondWeight, recorded.Status);
        Assert.Equal(10_000m, recorded.FirstWeight?.Kilograms);
        Assert.Equal(WeightSource.Manual, recorded.FirstWeight?.Source);
    }

    [Fact]
    public async Task InputSettings_ManualTare_Off()
    {
        using var harness = new WeighmentHarness(Options.Create(new WeighmentOptions { ManualTareEntry = false }));
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12MTOFF1" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.RecordFirstWeightAsync(open.Id, 10_000m, WeightSource.Manual));

        Assert.Contains("Manual weight entry is disabled", ex.Message);
        var reloaded = await harness.Service.GetAsync(open.Id);
        Assert.Equal(WeighmentStatus.Created, reloaded!.Status);
        Assert.Null(reloaded.FirstWeight);
    }

    [Fact]
    public async Task InputSettings_AutoTare_On()
    {
        using var harness = new WeighmentHarness(Options.Create(new WeighmentOptions
        {
            OnlySingleEntry = true,
            AutoTareWeight = true
        }));
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12ATON01" });

        var completed = await harness.Service.RecordSingleEntryWeightAsync(
            new RecordSingleEntryWeightRequest(open.Id, 25_000m, WeightSource.Indicator, 10_000m));

        Assert.Equal(WeighmentStatus.Completed, completed.Status);
        Assert.Equal(25_000m, completed.Gross?.Kilograms);
        Assert.Equal(10_000m, completed.Tare?.Kilograms);
        Assert.Equal(WeightSource.MasterTare, completed.Tare?.Source);
        Assert.Equal(15_000m, completed.NetWeightKg);
    }

    [Fact]
    public async Task InputSettings_MasterTare_DoesNotOverwriteLiveReading_OrMutateHistory()
    {
        using var harness = new WeighmentHarness(Options.Create(new WeighmentOptions
        {
            OnlySingleEntry = true,
            AutoTareWeight = true
        }));
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12SAFE01" });

        var completed = await harness.Service.RecordSingleEntryWeightAsync(
            new RecordSingleEntryWeightRequest(open.Id, 30_000m, WeightSource.Indicator, 10_000m));

        Assert.Equal(30_000m, completed.Gross?.Kilograms);
        Assert.Equal(WeightSource.Indicator, completed.Gross?.Source);
        Assert.Equal(10_000m, completed.Tare?.Kilograms);
        Assert.Equal(WeightSource.MasterTare, completed.Tare?.Source);

        var reloaded = await harness.Service.GetAsync(open.Id);
        Assert.Equal(30_000m, reloaded!.Gross?.Kilograms);
        Assert.Equal(10_000m, reloaded.Tare?.Kilograms);
    }

    [Fact]
    public async Task InputSettings_AutoTare_Off()
    {
        using var harness = new WeighmentHarness(Options.Create(new WeighmentOptions
        {
            OnlySingleEntry = true,
            AutoTareWeight = false
        }));
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12ATOFF1" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.RecordSingleEntryWeightAsync(
                new RecordSingleEntryWeightRequest(open.Id, 25_000m, WeightSource.Indicator, 10_000m)));

        Assert.Contains("Auto Tare Weight", ex.Message);
        var reloaded = await harness.Service.GetAsync(open.Id);
        Assert.Equal(WeighmentStatus.Created, reloaded!.Status);
    }

    [Fact]
    public async Task InputSettings_SecondCharges_On()
    {
        using var harness = new WeighmentHarness(Options.Create(new WeighmentOptions { SecondEntryCharges = true }));
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12SCON01", Charges = 100m });
        await harness.Service.RecordFirstWeightAsync(open.Id, 20_000m, WeightSource.Indicator);

        var completed = await harness.Service.RecordSecondWeightAsync(new RecordSecondWeightRequest(
            open.Id,
            8_000m,
            WeightSource.Indicator,
            SecondCharges: 50m));

        Assert.Equal(WeighmentStatus.Completed, completed.Status);
        Assert.Equal(100m, completed.Charges);
        Assert.Equal(50m, completed.SecondCharges);
        Assert.Equal(150m, completed.Charges + completed.SecondCharges);
    }

    [Fact]
    public async Task InputSettings_SecondCharges_Off()
    {
        using var harness = new WeighmentHarness(Options.Create(new WeighmentOptions { SecondEntryCharges = false }));
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12SCOFF1", Charges = 100m });
        await harness.Service.RecordFirstWeightAsync(open.Id, 20_000m, WeightSource.Indicator);

        var completed = await harness.Service.RecordSecondWeightAsync(new RecordSecondWeightRequest(
            open.Id,
            8_000m,
            WeightSource.Indicator,
            SecondCharges: 50m));

        Assert.Equal(WeighmentStatus.Completed, completed.Status);
        Assert.Equal(100m, completed.Charges);
        Assert.Equal(0m, completed.SecondCharges);
    }

    [Fact]
    public async Task InputSettings_SingleEntry_On()
    {
        using var harness = new WeighmentHarness(Options.Create(new WeighmentOptions
        {
            OnlySingleEntry = true,
            AutoTareWeight = true
        }));
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12SION01" });

        var completed = await harness.Service.RecordSingleEntryWeightAsync(
            new RecordSingleEntryWeightRequest(open.Id, 28_000m, WeightSource.Indicator, 11_000m));

        Assert.Equal(WeighmentStatus.Completed, completed.Status);
        Assert.Empty(await harness.Service.GetAwaitingSecondWeightAsync());
        Assert.Equal(17_000m, completed.NetWeightKg);
    }

    [Fact]
    public async Task InputSettings_SingleEntry_Off()
    {
        using var harness = new WeighmentHarness(Options.Create(new WeighmentOptions { OnlySingleEntry = false }));
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12SIOFF1" });

        var first = await harness.Service.RecordFirstWeightAsync(open.Id, 28_000m, WeightSource.Indicator);

        Assert.Equal(WeighmentStatus.AwaitingSecondWeight, first.Status);
        Assert.Single(await harness.Service.GetAwaitingSecondWeightAsync());
    }

    [Fact]
    public async Task InputSettings_ZeroNet_On()
    {
        using var harness = new WeighmentHarness(Options.Create(new WeighmentOptions { AllowZeroNetWeight = true }));
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12ZNON01" });
        await harness.Service.RecordFirstWeightAsync(open.Id, 10_000m, WeightSource.Indicator);

        var completed = await harness.Service.RecordSecondWeightAsync(open.Id, 10_000m, WeightSource.Indicator);

        Assert.Equal(WeighmentStatus.Completed, completed.Status);
        Assert.Equal(0m, completed.NetWeightKg);
    }

    [Fact]
    public async Task InputSettings_ZeroNet_Off()
    {
        using var harness = new WeighmentHarness(Options.Create(new WeighmentOptions { AllowZeroNetWeight = false }));
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12ZNOFF1" });
        await harness.Service.RecordFirstWeightAsync(open.Id, 10_000m, WeightSource.Indicator);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.RecordSecondWeightAsync(open.Id, 10_000m, WeightSource.Indicator));

        Assert.Contains("Zero net weight is disallowed", ex.Message);
    }

    [Fact]
    public async Task InputSettings_ChargesMandatory_On()
    {
        using var harness = new WeighmentHarness(Options.Create(new WeighmentOptions { ChargesMandatory = true }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12CMON01", Charges = 0m }));

        Assert.Contains("charges are mandatory", ex.Message);
        Assert.Empty(await harness.Service.GetRecentAsync());
    }

    [Fact]
    public async Task InputSettings_ChargesMandatory_Off()
    {
        using var harness = new WeighmentHarness(Options.Create(new WeighmentOptions { ChargesMandatory = false }));

        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12CMOFF1", Charges = 0m });

        Assert.Equal(0m, open.Charges);
        Assert.Equal(WeighmentStatus.Created, open.Status);
    }

    [Fact]
    public async Task InputSettings_MinimumCharges()
    {
        using var harness = new WeighmentHarness(Options.Create(new WeighmentOptions { MinimumCharges = 150m }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12MIN001", Charges = 100m }));

        Assert.Contains("at least 150", ex.Message);
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12MIN002", Charges = 150m });
        Assert.Equal(150m, open.Charges);
    }

    [Fact]
    public async Task InputSettings_UnitBags()
    {
        using var enabledHarness = new WeighmentHarness(Options.Create(new WeighmentOptions { UnitBagsWeightColumn = true }));
        var enabled = await enabledHarness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = "MH12BAGON1",
            NumberOfBags = 10,
            BagWeightKg = 2m
        });
        Assert.Equal(10, enabled.NumberOfBags);
        Assert.Equal(2m, enabled.BagWeightKg);
        Assert.Equal(20m, enabled.TotalBagWeightKg);

        using var disabledHarness = new WeighmentHarness(Options.Create(new WeighmentOptions { UnitBagsWeightColumn = false }));
        var disabled = await disabledHarness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = "MH12BAGOFF",
            NumberOfBags = 10,
            BagWeightKg = 2m
        });
        Assert.Null(disabled.NumberOfBags);
        Assert.Null(disabled.BagWeightKg);
        Assert.Equal(0m, disabled.TotalBagWeightKg);
    }

    [Fact]
    public void InputSettings_WeightHold()
    {
        var options = new WeighmentOptions { WeightHold = true };

        Assert.True(options.WeightHold);
    }

    [Fact]
    public void InputSettings_AutoUpdateTare()
    {
        var options = new WeighmentOptions { AutoUpdateTareWeight = true };

        Assert.True(options.AutoUpdateTareWeight);
    }
}
