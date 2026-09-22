using WeighBridge.Hardware.WeightIndicators;
using Xunit;

namespace WeighBridge.Tests.Hardware;

public sealed class FastHardwarePortDetectorTests
{
    [Theory]
    [InlineData("COM1", 1)]
    [InlineData("COM2", 2)]
    [InlineData("COM10", 10)]
    [InlineData("COM25", 25)]
    [InlineData("ttyUSB0", 0)]
    [InlineData("INVALID", int.MaxValue)]
    public void PortNumber_ExtractsTrailingDigits_Correctly(string portName, int expectedNumber)
    {
        int result = FastHardwarePortDetector.PortNumber(portName);
        Assert.Equal(expectedNumber, result);
    }

    [Fact]
    public void PrioritizePorts_SortsNumerically_WhenPortsHaveEqualPriority()
    {
        string[] raw = ["COM88", "COM22", "COM11", "COM99"];
        var sorted = FastHardwarePortDetector.PrioritizePorts(raw);

        // When no registry priority overrides exist, numerical sort must be maintained
        Assert.Equal("COM11", sorted[0]);
        Assert.Equal("COM22", sorted[1]);
        Assert.Equal("COM88", sorted[2]);
        Assert.Equal("COM99", sorted[3]);
    }

    [Fact]
    public void PrioritizePorts_PrioritizesDetectedUsbPorts_AheadOfGenericPorts()
    {
        var detected = FastHardwarePortDetector.DetectAllPorts();
        var usbPort = detected.FirstOrDefault(d => d.Priority >= 50);

        if (usbPort != null)
        {
            string[] raw = ["COM99", usbPort.PortName];
            var sorted = FastHardwarePortDetector.PrioritizePorts(raw);

            // Detected USB device should be sorted first ahead of generic COM99
            Assert.Equal(usbPort.PortName, sorted[0]);
            Assert.Equal("COM99", sorted[1]);
        }
    }

    [Fact]
    public void PrioritizePorts_HandlesNullAndEmpty_WithoutThrowing()
    {
        var emptyResult = FastHardwarePortDetector.PrioritizePorts([]);
        Assert.Empty(emptyResult);

        var whitespaceResult = FastHardwarePortDetector.PrioritizePorts(["", "   ", null!]);
        Assert.Empty(whitespaceResult);
    }

    [Fact]
    public void DetectAllPorts_ExecutesSafelyOnWindows()
    {
        var discovered = FastHardwarePortDetector.DetectAllPorts();
        Assert.NotNull(discovered);
        // Every discovered port must have a valid PortName
        Assert.All(discovered, p => Assert.False(string.IsNullOrWhiteSpace(p.PortName)));
    }
}
