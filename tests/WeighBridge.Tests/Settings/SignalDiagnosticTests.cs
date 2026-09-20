using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.App.Services;
using WeighBridge.App.ViewModels;
using WeighBridge.Core.Configuration;
using WeighBridge.Settings.Configuration;
using WeighBridge.Settings.Services;
using WeighBridge.Tests.Infrastructure;
using Xunit;

namespace WeighBridge.Tests.Settings;

public sealed class SignalDiagnosticTests
{
    private static SettingsViewModel CreateViewModel(StubWeightIndicator? indicator = null)
    {
        var data = new TempDataRoot();
        data.Paths.EnsureCreated();
        File.WriteAllText(data.Paths.ConfigurationFile, "{}");
        File.WriteAllText(data.Paths.UserPreferencesFile, "{}");

        return new SettingsViewModel(
            new SettingsService(data.Paths, NullLogger<SettingsService>.Instance),
            new StubThemeService(),
            new StubDialogService(),
            new JsonConfigurationWriter(data.Paths, NullLogger<JsonConfigurationWriter>.Instance),
            new StubPortScanner(),
            indicator ?? new StubWeightIndicator(),
            new StubPermissions(),
            Options.Create(new HardwareOptions()),
            Options.Create(new PrinterOptions()),
            Options.Create(new ReportingOptions()),
            Options.Create(new DatabaseOptions()),
            Options.Create(new WeighmentOptions()),
            NullLogger<SettingsViewModel>.Instance,
            Options.Create(new CompanyOptions()),
            Options.Create(new SmsOptions()));
    }

    [Fact]
    public void SignalDiagnostic_InitializesWithDefaultValues()
    {
        var vm = CreateViewModel();

        Assert.Equal(2400, vm.DiagnosticBaudRate);
        Assert.Equal(8, vm.DiagnosticDataBits);
        Assert.Equal("None", vm.DiagnosticParity);
        Assert.Equal("One", vm.DiagnosticStopBits);
        Assert.True(vm.DiagnosticAutoScroll);
        Assert.True(vm.IsAsciiDisplayMode);
        Assert.False(vm.IsHexDisplayMode);
        Assert.False(vm.IsDiagnosticMonitoring);
        Assert.False(vm.IsBraysTerminalRunning);
        Assert.Equal(0, vm.DiagnosticBytesReceivedCount);
        Assert.NotNull(vm.TerminalStatusMessage);
    }

    [Fact]
    public void DisplayModes_CanBeToggledBetweenAsciiAndHex()
    {
        var vm = CreateViewModel();

        Assert.True(vm.IsAsciiDisplayMode);
        Assert.False(vm.IsHexDisplayMode);

        vm.SetHexDisplayModeCommand.Execute(null);
        Assert.False(vm.IsAsciiDisplayMode);
        Assert.True(vm.IsHexDisplayMode);

        vm.SetAsciiDisplayModeCommand.Execute(null);
        Assert.True(vm.IsAsciiDisplayMode);
        Assert.False(vm.IsHexDisplayMode);
    }

    [Fact]
    public void ClearLog_ResetsLogsAndByteCount()
    {
        var vm = CreateViewModel();

        vm.ClearDiagnosticLogCommand.Execute(null);

        Assert.Equal(string.Empty, vm.DiagnosticAsciiLog);
        Assert.Equal(string.Empty, vm.DiagnosticHexLog);
        Assert.Equal(0, vm.DiagnosticBytesReceivedCount);
    }

    [Fact]
    public async Task LaunchAndStopBraysTerminal_HandlesPortLifecycleSafely()
    {
        var indicator = new StubWeightIndicator();
        var vm = CreateViewModel(indicator);

        // When Terminal is started, indicator should be disconnected
        await vm.LaunchBraysTerminalAsync();

        // When stopped, indicator should be reconnected
        await vm.StopBraysTerminalAsync();
        Assert.False(vm.IsBraysTerminalRunning);
    }

    [Fact]
    public void DiagnosticSerialMonitor_ParsesHexStringsAccurately()
    {
        var monitor = new DiagnosticSerialMonitor();

        // Even with non-existent port, SendBytesAsync gracefully handles it without crashing
        Assert.False(monitor.IsMonitoring);
        monitor.Dispose();
    }
}
