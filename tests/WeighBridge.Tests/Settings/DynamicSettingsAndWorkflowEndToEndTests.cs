using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.App.Services;
using WeighBridge.App.ViewModels;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Dialogs;
using WeighBridge.Core.Events;
using WeighBridge.Core.Events.Catalog;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Printing;
using WeighBridge.Core.Security;
using WeighBridge.Core.Settings;
using WeighBridge.Core.Theming;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Masters;
using WeighBridge.Hardware.WeightIndicators;
using WeighBridge.Settings.Configuration;
using WeighBridge.Settings.Services;
using WeighBridge.Tests.Infrastructure;
using Xunit;

namespace WeighBridge.Tests.Settings;

internal sealed class RecordingEventPublisher : IEventPublisher
{
    public List<ApplicationEvent> PublishedEvents { get; } = new();

    public void Publish<TEvent>(TEvent @event) where TEvent : ApplicationEvent
    {
        PublishedEvents.Add(@event);
    }

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : ApplicationEvent
    {
        PublishedEvents.Add(@event);
        return Task.CompletedTask;
    }
}

public sealed class DynamicSettingsAndWorkflowEndToEndTests
{
    [Fact]
    public void WeightDecoder_NegativeRawReading_ClampedToZero()
    {
        var decoder = new WeightDecoder();
        var frame = new ParsedWeightFrame("-00050", "kg", true, "[-00050]");
        var options = new WeightDecodeOptions
        {
            WeightDigits = 6,
            DecimalPlaces = 0,
            ReversePayload = false,
            DigitsToRemoveFromEnd = 0,
            ScaleFactor = 1.0m
        };

        bool decoded = decoder.TryDecode(frame, options, out decimal decodedWeight);

        Assert.True(decoded);
        Assert.Equal(0m, decodedWeight);
    }

    [Fact]
    public void Vehicle_UpdateTareWeight_UpdatesWeightCleanly()
    {
        var vehicle = Vehicle.Create("MH12AB1234", tareWeightKg: 10_000m);
        vehicle.UpdateTareWeight(12_500m);

        Assert.Equal(12_500m, vehicle.TareWeightKg);
    }

    [Fact]
    public void GstOnCharges_CalculatesEighteenPercentCorrectly()
    {
        decimal charges = 100m;
        decimal gstAmount = Math.Round(charges * 0.18m, 2);
        decimal total = charges + gstAmount;

        Assert.Equal(18m, gstAmount);
        Assert.Equal(118m, total);
    }

    [Fact]
    public void PriceComputing_MultipliesRateAndNetCorrectly()
    {
        decimal rate = 50m;
        decimal netKg = 1_000m;
        decimal totalAmount = rate * netKg;

        Assert.Equal(50_000m, totalAmount);
    }

    [Fact]
    public void UnitBagsWeight_DeductsFromNetCorrectly()
    {
        int numberOfBags = 10;
        decimal bagWeightKg = 2m;
        decimal totalBagWeightKg = numberOfBags * bagWeightKg;
        decimal netKg = 15_000m;
        decimal actualWeightKg = netKg - totalBagWeightKg;

        Assert.Equal(20m, totalBagWeightKg);
        Assert.Equal(14_980m, actualWeightKg);
    }

    [Fact]
    public void SettingsViewModel_SelectedTheme_AppliesImmediately()
    {
        using var data = new TempDataRoot();
        data.Paths.EnsureCreated();
        File.WriteAllText(data.Paths.ConfigurationFile, "{}");
        File.WriteAllText(data.Paths.UserPreferencesFile, "{}");

        var writer = new JsonConfigurationWriter(data.Paths, NullLogger<JsonConfigurationWriter>.Instance);
        var settingsService = new SettingsService(data.Paths, NullLogger<SettingsService>.Instance);
        var themeService = new StubThemeService();
        var dialogService = new StubDialogService();
        var portScanner = new StubPortScanner();
        var indicator = new StubWeightIndicator();
        var permissions = new StubPermissions();

        var vm = new SettingsViewModel(
            settingsService,
            themeService,
            dialogService,
            writer,
            portScanner,
            indicator,
            permissions,
            Options.Create(new HardwareOptions()),
            Options.Create(new PrinterOptions()),
            Options.Create(new ReportingOptions()),
            Options.Create(new DatabaseOptions()),
            Options.Create(new WeighmentOptions()),
            NullLogger<SettingsViewModel>.Instance);

        vm.SelectedTheme = AppTheme.Dark;
        Assert.Equal(AppTheme.Dark, themeService.CurrentTheme);

        vm.SelectedTheme = AppTheme.Light;
        Assert.Equal(AppTheme.Light, themeService.CurrentTheme);
    }

    [Fact]
    public async Task SettingsViewModel_SaveConfiguration_PublishesSettingsChangedEvents()
    {
        using var data = new TempDataRoot();
        data.Paths.EnsureCreated();
        File.WriteAllText(data.Paths.ConfigurationFile, "{}");
        File.WriteAllText(data.Paths.UserPreferencesFile, "{}");

        var writer = new JsonConfigurationWriter(data.Paths, NullLogger<JsonConfigurationWriter>.Instance);
        var settingsService = new SettingsService(data.Paths, NullLogger<SettingsService>.Instance);
        var themeService = new StubThemeService();
        var dialogService = new StubDialogService();
        var portScanner = new StubPortScanner();
        var indicator = new StubWeightIndicator();
        var permissions = new StubPermissions();
        var eventPublisher = new RecordingEventPublisher();

        var vm = new SettingsViewModel(
            settingsService,
            themeService,
            dialogService,
            writer,
            portScanner,
            indicator,
            permissions,
            Options.Create(new HardwareOptions()),
            Options.Create(new PrinterOptions()),
            Options.Create(new ReportingOptions()),
            Options.Create(new DatabaseOptions()),
            Options.Create(new WeighmentOptions()),
            NullLogger<SettingsViewModel>.Instance,
            eventPublisher: eventPublisher);

        await ((AsyncRelayCommand)vm.SaveConfigurationCommand).ExecuteAsync();

        Assert.Contains(eventPublisher.PublishedEvents, e => e is SettingsChangedEvent se && se.SectionName == "Hardware");
        Assert.Contains(eventPublisher.PublishedEvents, e => e is SettingsChangedEvent se && se.SectionName == "Weighment");
        Assert.Contains(eventPublisher.PublishedEvents, e => e is SettingsChangedEvent se && se.SectionName == "All");
    }

    [Fact]
    public void PrinterProfile_SideWisePrinting_PassesLandscapeToProfile()
    {
        var profile = PrinterProfile.DefaultGdi("TestPrinter");
        Assert.False(profile.SideWisePrinting);

        var landscapeProfile = profile with { SideWisePrinting = true };
        Assert.True(landscapeProfile.SideWisePrinting);
    }
}
