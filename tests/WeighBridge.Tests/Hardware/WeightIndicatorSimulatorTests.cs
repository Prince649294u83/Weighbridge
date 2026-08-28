using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Domain.Enums;
using WeighBridge.Hardware.WeightIndicators;
using Xunit;

namespace WeighBridge.Tests.Hardware;

public sealed class WeightIndicatorSimulatorTests
{
    [Fact]
    public async Task Simulator_Lifecycle_And_Reading_Emits_Simulator_Source()
    {
        var hardwareOptions = Options.Create(new HardwareOptions
        {
            WeightIndicator = new WeightIndicatorOptions
            {
                Enabled = true,
                PollIntervalMilliseconds = 50,
                Unit = "kg",
            },
        });

        var simulator = new WeightIndicatorSimulator(hardwareOptions, NullLogger<WeightIndicatorSimulator>.Instance);

        Assert.Equal(ConnectionState.Disconnected, simulator.State);

        var receivedReadings = new List<WeightReading>();
        simulator.ReadingReceived += (_, r) => receivedReadings.Add(r);

        bool connected = await simulator.ConnectAsync();
        Assert.True(connected);
        Assert.Equal(ConnectionState.Connected, simulator.State);

        simulator.SetWeight(32500m, isStable: true);

        await Task.Delay(150);

        Assert.NotEmpty(receivedReadings);
        var last = receivedReadings.Last();
        Assert.Equal(32500m, last.Value);
        Assert.True(last.IsStable);
        Assert.Equal(WeightSource.Simulator, last.Source); // Verified provenance

        // Simulate Disconnect
        simulator.SimulateDisconnect();
        Assert.Equal(ConnectionState.Disconnected, simulator.State);

        // Simulate Reconnect
        simulator.SimulateReconnect();
        Assert.Equal(ConnectionState.Connected, simulator.State);

        await simulator.DisconnectAsync();
        Assert.Equal(ConnectionState.Disconnected, simulator.State);
    }
}
