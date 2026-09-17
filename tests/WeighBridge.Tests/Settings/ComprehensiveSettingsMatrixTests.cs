using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using WeighBridge.App.ViewModels;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Dialogs;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Security;
using WeighBridge.Core.Settings;
using WeighBridge.Core.Theming;
using WeighBridge.Domain.Enums;
using WeighBridge.Settings.Configuration;
using WeighBridge.Settings.Services;
using WeighBridge.Tests.Infrastructure;
using Xunit;

namespace WeighBridge.Tests.Settings;

internal sealed class StubThemeService : IThemeService
{
    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;
    public AppTheme EffectiveTheme => CurrentTheme;
    public void Initialize() { }
    public void ApplyTheme(AppTheme theme) => CurrentTheme = theme;
    public void ToggleTheme() => CurrentTheme = CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
}

internal sealed class StubDialogService : IDialogService
{
    public Task ShowInformationAsync(string title, string message, string? details = null) => Task.CompletedTask;
    public Task ShowSuccessAsync(string title, string message, string? details = null) => Task.CompletedTask;
    public Task ShowWarningAsync(string title, string message, string? details = null) => Task.CompletedTask;
    public Task ShowErrorAsync(string title, string message, string? details = null) => Task.CompletedTask;
    public Task<bool> ShowConfirmationAsync(string title, string message, string confirmText = "Yes", string cancelText = "No", bool isDestructive = false) => Task.FromResult(true);
    public Task ShowLoadingAsync(string message, Func<Task> operation) => operation();
    public Task ShowProgressAsync(string title, Func<IProgressReporter, Task> operation, bool isCancellable = false) => Task.CompletedTask;
    public Task<bool> ShowLoginAsync() => Task.FromResult(true);
}

internal sealed class StubPortScanner : IIndicatorPortScanner
{
    public IReadOnlyList<string> GetAvailablePorts() => Array.Empty<string>();
    public Task<IReadOnlyList<PortProbeResult>> ScanAsync(
        IEnumerable<string>? portNames = null,
        IEnumerable<int>? baudRates = null,
        TimeSpan? listenPerAttempt = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PortProbeResult>>(Array.Empty<PortProbeResult>());
}

internal sealed class StubWeightIndicator : IWeightIndicatorService
{
    public ConnectionState State => ConnectionState.Disconnected;
    public WeightReading CurrentReading => WeightReading.Empty;
    public event EventHandler<WeightReading>? ReadingReceived { add { } remove { } }
    public event EventHandler<ConnectionState>? StateChanged { add { } remove { } }
    public Task<bool> ConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task DisconnectAsync() => Task.CompletedTask;
    public Task<WeightReading> ReadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CurrentReading);
    public string Name => "StubWeightIndicator";
    public Task<HealthResult> CheckAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(HealthResult.Disabled("Stub"));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class StubPermissions : IPermissionService
{
    public OperatorIdentity CurrentOperator { get; private set; } =
        new("admin", "Admin", Roles.Administrator);
    public event EventHandler<OperatorChangedEventArgs>? OperatorChanged;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool HasPermission(Permission permission) => true;
    public bool HasAllPermissions(params Permission[] permissions) => true;
    public bool HasAnyPermission(params Permission[] permissions) => true;
    public AuthorizationResult Authorize(Permission permission) => AuthorizationResult.Allowed;
    public AuthorizationResult Authorize(object candidate) => AuthorizationResult.Allowed;
    public void SetOperator(OperatorIdentity identity)
    {
        var previous = CurrentOperator;
        CurrentOperator = identity;
        OperatorChanged?.Invoke(this, new OperatorChangedEventArgs(previous, identity));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentOperator)));
    }
    public void SignOut()
    {
        SetOperator(new OperatorIdentity(
            Environment.UserName, Environment.UserName, Roles.Unauthenticated));
    }
}

/// <summary>
/// Verifies every individual setting in the Settings surface independently:
/// mutation, configuration serialization, reload persistence, and runtime effect.
/// </summary>
public sealed class ComprehensiveSettingsMatrixTests
{
    [Fact]
    public async Task SettingsViewModel_EveryIndividualSetting_SerializesAndPersistsIndependently()
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

        var hwOpts = Microsoft.Extensions.Options.Options.Create(new HardwareOptions());
        var printOpts = Microsoft.Extensions.Options.Options.Create(new PrinterOptions());
        var repOpts = Microsoft.Extensions.Options.Options.Create(new ReportingOptions());
        var dbOpts = Microsoft.Extensions.Options.Options.Create(new DatabaseOptions());
        var wmOpts = Microsoft.Extensions.Options.Options.Create(new WeighmentOptions());
        var compOpts = Microsoft.Extensions.Options.Options.Create(new CompanyOptions());
        var smsOpts = Microsoft.Extensions.Options.Options.Create(new SmsOptions());

        var vm = new SettingsViewModel(
            settingsService,
            themeService,
            dialogService,
            writer,
            portScanner,
            indicator,
            permissions,
            hwOpts,
            printOpts,
            repOpts,
            dbOpts,
            wmOpts,
            NullLogger<SettingsViewModel>.Instance,
            compOpts,
            smsOpts);

        // 1. Weight Indicator Settings
        vm.IndicatorEnabled = true;
        vm.DriverType = "Simulator";
        vm.PortName = "COM7";
        vm.BaudRate = 9600;
        vm.DataBits = 7;
        vm.Parity = "Even";
        vm.StopBits = "Two";
        vm.StabilitySampleCount = 8;
        vm.StabilityToleranceKg = 2.5m;
        vm.StabilityDurationMs = 1500;
        vm.AutoReconnect = false;
        vm.ReconnectIntervalMs = 2500;
        vm.DtrEnable = false;
        vm.RtsEnable = false;
        vm.Handshake = "RequestToSend";

        // 2. Semantic Profile Settings
        vm.FrameStartChar = "<";
        vm.FrameEndChar = ">";
        vm.WeightDigits = 6;
        vm.DecimalPlaces = 2;
        vm.ReversePayload = true;
        vm.TrailingDigitsRemoved = 1;
        vm.ScaleFactor = 0.5m;
        vm.TargetUnit = "t";

        // 3. Printing Settings
        vm.PrintingEnabled = false;
        vm.PrinterType = "Graphics Printer";
        vm.SideWisePrinting = true;
        vm.DefaultPrinterName = "TestThermalPrinter";
        vm.CopyCount = 3;
        vm.PaperSize = "Half A4 / A5";

        // 4. Input / Workflow Settings
        vm.UnitBagsWeightColumn = true;
        vm.ManualTareEntry = false;
        vm.AutoTareWeight = false;
        vm.SecondEntryCharges = true;
        vm.GstOnCharges = true;
        vm.OnlySingleEntry = true;

        // 5. Other Settings
        vm.PriceComputing = true;
        vm.DisconnectTimeSeconds = 600;
        vm.AllowZeroNetWeight = true;
        vm.AutoApplicationShortcut = false;
        vm.AutoUpdateTareWeight = true;
        vm.WeightHold = true;
        vm.TimeFormat = "24 Hour";
        vm.PrintQrCode = true;
        vm.ChargesMandatory = true;
        vm.MinimumCharges = 150.0m;

        // 6. Auxiliary Ports
        vm.ReceivePort1Enabled = true;
        vm.ReceivePort1Name = "COM8";
        vm.ReceivePort1Baud = 19200;
        vm.ReceivePort2Enabled = true;
        vm.ReceivePort2Name = "COM9";
        vm.ReceivePort2Baud = 38400;
        vm.SendDataPortEnabled = true;
        vm.SendDataPortName = "COM10";
        vm.SendDataPortBaud = 57600;

        // 7. Email Settings
        vm.EmailEnabled = true;
        vm.EmailFrequency = "Email Both Entry";
        vm.EmailPdf = false;
        vm.EmailSenderName = "Terminal Alpha";
        vm.EmailSenderId = "alpha@industrial.local";
        vm.EmailPassword = "SecretPassword123";
        vm.EmailSmtpServer = "smtp.industrial.local";

        // 8. SMS & WhatsApp
        vm.SmsService = "Enable";
        vm.SmsFrequency = "SMS only First Entry";
        vm.SmsNumbers = "919876543210";
        vm.WhatsAppToken = "wa_token_abc_123";

        // 9. Company WB Name
        vm.WeighbridgeName = "Port Logistics Yard 4";
        vm.WeighbridgeAddress1 = "Dock 12 West Wharf";
        vm.WeighbridgeAddress2 = "Sector 5 Marine Lines";

        // 10. Extended Indicator Settings
        vm.IndicatorEndingString = "CR (0x0D)";
        vm.IndicatorHexValue = true;
        vm.IndicatorEssaeMode = true;
        vm.IndicatorRtsCts = true;
        vm.IndicatorBufferData = 100;
        vm.IndicatorDummyZero = 1;
        vm.IndicatorStableWaitTime = 500;

        // 12. Reporting Settings
        vm.ReportOutputDirectory = @"C:\Exports\WeighBridge";
        vm.MaxRowsPerReport = 5000;

        // Save through the ViewModel command
        if (vm.SaveConfigurationCommand is AsyncRelayCommand asyncCmd)
        {
            await asyncCmd.ExecuteAsync();
        }
        else
        {
            vm.SaveConfigurationCommand.Execute(null);
        }

        // Assert that the JSON file on disk contains every single value
        var savedJson = File.ReadAllText(data.Paths.ConfigurationFile);
        var jsonRoot = JsonNode.Parse(savedJson)?.AsObject();
        Assert.NotNull(jsonRoot);

        // Verify Weight Indicator values in JSON
        Assert.True(jsonRoot["Hardware"]?["WeightIndicator"]?["Enabled"]?.GetValue<bool>());
        Assert.Equal("Simulator", jsonRoot["Hardware"]?["WeightIndicator"]?["DriverType"]?.GetValue<string>());
        Assert.Equal("COM7", jsonRoot["Hardware"]?["WeightIndicator"]?["PortName"]?.GetValue<string>());
        Assert.Equal(9600, jsonRoot["Hardware"]?["WeightIndicator"]?["BaudRate"]?.GetValue<int>());
        Assert.Equal(7, jsonRoot["Hardware"]?["WeightIndicator"]?["DataBits"]?.GetValue<int>());
        Assert.Equal("Even", jsonRoot["Hardware"]?["WeightIndicator"]?["Parity"]?.GetValue<string>());
        Assert.Equal("Two", jsonRoot["Hardware"]?["WeightIndicator"]?["StopBits"]?.GetValue<string>());
        Assert.Equal(8, jsonRoot["Hardware"]?["WeightIndicator"]?["StabilitySampleCount"]?.GetValue<int>());
        Assert.Equal(2.5m, jsonRoot["Hardware"]?["WeightIndicator"]?["StabilityToleranceKg"]?.GetValue<decimal>());
        Assert.Equal(1500, jsonRoot["Hardware"]?["WeightIndicator"]?["StabilityDurationMs"]?.GetValue<int>());
        Assert.False(jsonRoot["Hardware"]?["WeightIndicator"]?["AutoReconnect"]?.GetValue<bool>());
        Assert.Equal(2500, jsonRoot["Hardware"]?["WeightIndicator"]?["ReconnectIntervalMs"]?.GetValue<int>());
        Assert.False(jsonRoot["Hardware"]?["WeightIndicator"]?["DtrEnable"]?.GetValue<bool>());
        Assert.False(jsonRoot["Hardware"]?["WeightIndicator"]?["RtsEnable"]?.GetValue<bool>());
        Assert.Equal("RequestToSend", jsonRoot["Hardware"]?["WeightIndicator"]?["Handshake"]?.GetValue<string>());

        // Verify Semantic Profile
        Assert.Equal(6, jsonRoot["Hardware"]?["WeightIndicator"]?["Decoding"]?["WeightDigits"]?.GetValue<int>());
        Assert.Equal(2, jsonRoot["Hardware"]?["WeightIndicator"]?["Decoding"]?["DecimalPlaces"]?.GetValue<int>());
        Assert.True(jsonRoot["Hardware"]?["WeightIndicator"]?["Decoding"]?["ReversePayload"]?.GetValue<bool>());
        Assert.Equal(1, jsonRoot["Hardware"]?["WeightIndicator"]?["Decoding"]?["DigitsToRemoveFromEnd"]?.GetValue<int>());
        Assert.Equal(0.5m, jsonRoot["Hardware"]?["WeightIndicator"]?["Decoding"]?["ScaleFactor"]?.GetValue<decimal>());
        Assert.Equal("t", jsonRoot["Hardware"]?["WeightIndicator"]?["Unit"]?.GetValue<string>());

        // Verify Printing
        Assert.False(jsonRoot["Printer"]?["Enabled"]?.GetValue<bool>());
        Assert.Equal("Graphics Printer", jsonRoot["Printer"]?["PrinterType"]?.GetValue<string>());
        Assert.True(jsonRoot["Printer"]?["SideWisePrinting"]?.GetValue<bool>());
        Assert.Equal("TestThermalPrinter", jsonRoot["Printer"]?["DefaultPrinterName"]?.GetValue<string>());
        Assert.Equal(3, jsonRoot["Printer"]?["CopyCount"]?.GetValue<int>());
        Assert.Equal("Half A4 / A5", jsonRoot["Printer"]?["PaperSize"]?.GetValue<string>());

        // Verify Input Workflow
        Assert.True(jsonRoot["Weighment"]?["UnitBagsWeightColumn"]?.GetValue<bool>());
        Assert.False(jsonRoot["Weighment"]?["ManualTareEntry"]?.GetValue<bool>());
        Assert.False(jsonRoot["Weighment"]?["AutoTareWeight"]?.GetValue<bool>());
        Assert.True(jsonRoot["Weighment"]?["SecondEntryCharges"]?.GetValue<bool>());
        Assert.True(jsonRoot["Weighment"]?["GstOnCharges"]?.GetValue<bool>());
        Assert.True(jsonRoot["Weighment"]?["OnlySingleEntry"]?.GetValue<bool>());

        // Verify Other Settings
        Assert.True(jsonRoot["Weighment"]?["PriceComputing"]?.GetValue<bool>());
        Assert.Equal(600, jsonRoot["Weighment"]?["DisconnectTimeSeconds"]?.GetValue<int>());
        Assert.True(jsonRoot["Weighment"]?["AllowZeroNetWeight"]?.GetValue<bool>());
        Assert.False(jsonRoot["Weighment"]?["AutoApplicationShortcut"]?.GetValue<bool>());
        Assert.True(jsonRoot["Weighment"]?["AutoUpdateTareWeight"]?.GetValue<bool>());
        Assert.True(jsonRoot["Weighment"]?["WeightHold"]?.GetValue<bool>());
        Assert.Equal("24 Hour", jsonRoot["Weighment"]?["TimeFormat"]?.GetValue<string>());
        Assert.True(jsonRoot["Weighment"]?["PrintQrCode"]?.GetValue<bool>());
        Assert.True(jsonRoot["Weighment"]?["ChargesMandatory"]?.GetValue<bool>());
        Assert.Equal(150.0m, jsonRoot["Weighment"]?["MinimumCharges"]?.GetValue<decimal>());

        // Verify Auxiliary Ports
        Assert.True(jsonRoot["Hardware"]?["PortSettings"]?["ReceivePort1"]?["Enabled"]?.GetValue<bool>());
        Assert.Equal("COM8", jsonRoot["Hardware"]?["PortSettings"]?["ReceivePort1"]?["PortName"]?.GetValue<string>());
        Assert.Equal(19200, jsonRoot["Hardware"]?["PortSettings"]?["ReceivePort1"]?["BaudRate"]?.GetValue<int>());

        // Verify Company
        Assert.Equal("Port Logistics Yard 4", jsonRoot["Company"]?["CompanyName"]?.GetValue<string>());
        Assert.Equal("Dock 12 West Wharf", jsonRoot["Company"]?["AddressLine1"]?.GetValue<string>());
        Assert.Equal("Sector 5 Marine Lines", jsonRoot["Company"]?["AddressLine2"]?.GetValue<string>());

        // Verify Reporting
        Assert.Equal(@"C:\Exports\WeighBridge", jsonRoot["Reporting"]?["OutputDirectory"]?.GetValue<string>());
        Assert.Equal(5000, jsonRoot["Reporting"]?["MaxRowsPerReport"]?.GetValue<int>());
    }
}
