using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Domain.Enums;
using WeighBridge.Hardware.WeightIndicators;
using Xunit;

namespace WeighBridge.Tests.Hardware;

public sealed class WeightIndicatorServiceLifecycleTests
{
    private sealed class FakeSerialTransport : ISerialPortTransport
    {
        private readonly Queue<byte[]> _incomingChunks = new();
        private readonly SemaphoreSlim _dataAvailable = new(0);

        public bool IsOpen { get; private set; }
        public string PortName => Options.PortName;
        public WeightIndicatorOptions Options { get; set; }
        public bool FailOnOpen { get; set; }
        public int OpenCount { get; private set; }
        public int CloseCount { get; private set; }

        public FakeSerialTransport(WeightIndicatorOptions options)
        {
            Options = options;
        }

        public Task OpenAsync(CancellationToken cancellationToken = default)
        {
            OpenCount++;
            if (FailOnOpen)
            {
                throw new InvalidOperationException($"Failed to open {Options.PortName}");
            }
            IsOpen = true;
            return Task.CompletedTask;
        }

        public Task CloseAsync()
        {
            CloseCount++;
            IsOpen = false;
            _dataAvailable.Release();
            return Task.CompletedTask;
        }

        public void EnqueueBytes(byte[] bytes)
        {
            _incomingChunks.Enqueue(bytes);
            _dataAvailable.Release();
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!IsOpen)
            {
                throw new InvalidOperationException("Port is closed.");
            }

            while (_incomingChunks.Count == 0 && IsOpen && !cancellationToken.IsCancellationRequested)
            {
                await _dataAvailable.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!IsOpen || cancellationToken.IsCancellationRequested)
            {
                return 0;
            }

            if (_incomingChunks.TryDequeue(out var chunk))
            {
                chunk.AsSpan().CopyTo(buffer.Span);
                return chunk.Length;
            }

            return 0;
        }

        public ValueTask DisposeAsync()
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task ConnectAsync_Awaits_Initial_Connection_And_Reports_True_When_Open_Succeeds()
    {
        var options = new HardwareOptions
        {
            WeightIndicator = new WeightIndicatorOptions
            {
                Enabled = true,
                DriverType = "Serial",
                PortName = "COM3",
                BaudRate = 2400
            }
        };

        var transport = new FakeSerialTransport(options.WeightIndicator);
        var service = new WeightIndicatorService(
            Options.Create(options),
            transport,
            new DelimitedFrameExtractor(),
            new GenericAsciiProtocolParser(),
            NullLogger<WeightIndicatorService>.Instance);

        bool connected = await service.ConnectAsync();

        Assert.True(connected);
        Assert.Equal(ConnectionState.Connected, service.State);
        Assert.Equal(1, transport.OpenCount);

        await service.DisconnectAsync();
        Assert.Equal(ConnectionState.Disconnected, service.State);
    }

    [Fact]
    public async Task ConnectAsync_Reports_False_When_Open_Fails_Without_Throwing_To_Caller()
    {
        var options = new HardwareOptions
        {
            WeightIndicator = new WeightIndicatorOptions
            {
                Enabled = true,
                DriverType = "Serial",
                PortName = "COM1",
                BaudRate = 2400,
                AutoReconnect = false // Don't loop in test
            }
        };

        var transport = new FakeSerialTransport(options.WeightIndicator) { FailOnOpen = true };
        var service = new WeightIndicatorService(
            Options.Create(options),
            transport,
            new DelimitedFrameExtractor(),
            new GenericAsciiProtocolParser(),
            NullLogger<WeightIndicatorService>.Instance);

        bool connected = await service.ConnectAsync();

        Assert.False(connected);
        Assert.Equal(ConnectionState.Disconnected, service.State);

        await service.DisconnectAsync();
    }

    [Fact]
    public async Task Service_Emits_ReadingReceived_When_Live_Frames_Are_Parsed()
    {
        var options = new HardwareOptions
        {
            WeightIndicator = new WeightIndicatorOptions
            {
                Enabled = true,
                DriverType = "Serial",
                PortName = "COM3",
                BaudRate = 2400,
                StabilitySampleCount = 1,
                StabilityDurationMs = 0
            }
        };

        var transport = new FakeSerialTransport(options.WeightIndicator);
        var service = new WeightIndicatorService(
            Options.Create(options),
            transport,
            new DelimitedFrameExtractor(),
            new GenericAsciiProtocolParser(),
            NullLogger<WeightIndicatorService>.Instance);

        var readings = new List<WeightReading>();
        service.ReadingReceived += (_, r) => readings.Add(r);

        await service.ConnectAsync();

        // Enqueue live bracket frame: [0001450\0
        var frameBytes = new byte[] { 0x5B, 0x30, 0x30, 0x30, 0x31, 0x34, 0x35, 0x30, 0x00 };
        transport.EnqueueBytes(frameBytes);

        // Allow worker to process
        await Task.Delay(100);

        Assert.NotEmpty(readings);
        Assert.Equal(1450m, readings[0].Value);
        Assert.Equal("kg", readings[0].Unit);
        Assert.Equal(WeightSource.Indicator, readings[0].Source);

        await service.DisconnectAsync();
    }

    [Fact]
    public async Task Dynamic_Reconfiguration_Reopens_Transport_With_New_Options()
    {
        var options = new HardwareOptions
        {
            WeightIndicator = new WeightIndicatorOptions
            {
                Enabled = true,
                DriverType = "Serial",
                PortName = "COM1",
                BaudRate = 2400
            }
        };

        var transport = new FakeSerialTransport(options.WeightIndicator);
        var service = new WeightIndicatorService(
            Options.Create(options),
            transport,
            new DelimitedFrameExtractor(),
            new GenericAsciiProtocolParser(),
            NullLogger<WeightIndicatorService>.Instance);

        await service.ConnectAsync();
        Assert.Equal("COM1", transport.PortName);

        await service.DisconnectAsync();

        // Mutate options to COM3 (simulating Settings ApplyToOptions)
        options.WeightIndicator.PortName = "COM3";

        await service.ConnectAsync();
        Assert.Equal("COM3", transport.PortName);
        Assert.Equal(ConnectionState.Connected, service.State);

        await service.DisconnectAsync();
    }
}
