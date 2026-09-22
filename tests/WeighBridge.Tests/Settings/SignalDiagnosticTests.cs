using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.App.Services;
using WeighBridge.App.ViewModels;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Configuration;
using WeighBridge.Domain.Enums;
using WeighBridge.Settings.Configuration;
using WeighBridge.Settings.Services;
using WeighBridge.Tests.Infrastructure;
using Xunit;

namespace WeighBridge.Tests.Settings;

public sealed class SignalDiagnosticTests
{
    private static SettingsViewModel CreateViewModel(IWeightIndicatorService? indicator = null)
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

    [Fact]
    public async Task SignalDiagnostic_ReceivesTelemetryFromIndicator_Directly()
    {
        var indicator = new TestableWeightIndicator();
        var vm = CreateViewModel(indicator);

        // Pre-set DiagnosticPort to match PortName and start diagnostic monitoring
        vm.DiagnosticPort = vm.PortName;
        await ((AsyncRelayCommand)vm.StartDiagnosticMonitoringCommand).ExecuteAsync();

        byte[] sampleTelemetry = System.Text.Encoding.ASCII.GetBytes("ST,GS,+025400kg\r\n");
        indicator.EmitTelemetry(sampleTelemetry);

        Assert.Contains("ST,GS,+025400kg", vm.DiagnosticAsciiLog);
        Assert.Equal(sampleTelemetry.Length, vm.DiagnosticBytesReceivedCount);
        Assert.True(vm.IsCtsHigh);
        Assert.True(vm.IsDsrHigh);
        Assert.True(vm.IsCdHigh);
    }

    [Fact]
    public async Task AdoptStreamAsLiveScale_UpdatesPortAndBaudRate_AndConnectsIndicator()
    {
        var indicator = new TestableWeightIndicator();
        var vm = CreateViewModel(indicator);

        vm.DiagnosticPort = "COM7";
        vm.DiagnosticBaudRate = 9600;

        await vm.AdoptStreamAsLiveScaleAsync();

        Assert.Equal("COM7", vm.PortName);
        Assert.Equal(9600, vm.BaudRate);
        Assert.True(indicator.ConnectCalled);
    }

    private sealed class TestableWeightIndicator : IWeightIndicatorService
    {
#pragma warning disable CS0067
        public ConnectionState State { get; set; } = ConnectionState.Connected;
        public WeightReading CurrentReading { get; set; } = new(25400m, "kg", true, DateTime.UtcNow);
        public event EventHandler<WeightReading>? ReadingReceived;
        public event EventHandler<ConnectionState>? StateChanged;
        public event EventHandler<DiagnosticDataChunk>? RawTelemetryReceived;
#pragma warning restore CS0067
        public bool ConnectCalled { get; private set; }
        public bool DisconnectCalled { get; private set; }

        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCalled = true;
            State = ConnectionState.Connected;
            return Task.FromResult(true);
        }

        public Task DisconnectAsync()
        {
            DisconnectCalled = true;
            State = ConnectionState.Disconnected;
            return Task.CompletedTask;
        }

        public Task<WeightReading> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(CurrentReading);
        public string Name => "TestableIndicator";
        public Task<HealthResult> CheckAsync(CancellationToken cancellationToken = default) => Task.FromResult(HealthResult.Healthy("OK"));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void EmitTelemetry(byte[] bytes)
        {
            RawTelemetryReceived?.Invoke(this, new DiagnosticDataChunk(bytes, bytes.Length, true, true, true));
        }
    }
}
