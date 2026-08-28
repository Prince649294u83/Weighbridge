using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Domain.Enums;
using WeighBridge.Hardware.WeightIndicators;
using Xunit;

namespace WeighBridge.Tests.Hardware;

public sealed class StabilityDetectorTests
{
    [Fact]
    public void StabilityDetector_Passes_Through_Explicitly_Stable_Readings()
    {
        var detector = new StabilityDetector(new WeightIndicatorOptions());
        var reading = new WeightReading(24500m, "kg", true, DateTime.UtcNow, WeightSource.Indicator);

        bool isStable = detector.Evaluate(reading);

        Assert.True(isStable);
    }

    [Fact]
    public void StabilityDetector_Requires_Sufficient_Samples_And_Time()
    {
        var options = new WeightIndicatorOptions
        {
            StabilitySampleCount = 3,
            StabilityToleranceKg = 5.0m,
            StabilityDurationMs = 200,
        };
        var detector = new StabilityDetector(options);
        var baseTime = DateTime.UtcNow;

        // Sample 1
        Assert.False(detector.Evaluate(new WeightReading(10000m, "kg", false, baseTime, WeightSource.Indicator)));

        // Sample 2 (within tolerance, but only 2 samples)
        Assert.False(detector.Evaluate(new WeightReading(10002m, "kg", false, baseTime.AddMilliseconds(50), WeightSource.Indicator)));

        // Sample 3 (3 samples, but elapsed only 100ms < 200ms duration)
        Assert.False(detector.Evaluate(new WeightReading(10001m, "kg", false, baseTime.AddMilliseconds(100), WeightSource.Indicator)));

        // Sample 4 (3+ samples and elapsed 250ms >= 200ms duration)
        Assert.True(detector.Evaluate(new WeightReading(10003m, "kg", false, baseTime.AddMilliseconds(250), WeightSource.Indicator)));
    }

    [Fact]
    public void StabilityDetector_Resets_When_Weight_Fluctuates_Beyond_Tolerance()
    {
        var options = new WeightIndicatorOptions
        {
            StabilitySampleCount = 3,
            StabilityToleranceKg = 5.0m,
            StabilityDurationMs = 200,
        };
        var detector = new StabilityDetector(options);
        var baseTime = DateTime.UtcNow;

        detector.Evaluate(new WeightReading(10000m, "kg", false, baseTime, WeightSource.Indicator));
        detector.Evaluate(new WeightReading(10002m, "kg", false, baseTime.AddMilliseconds(50), WeightSource.Indicator));

        // Jump by 50 kg (exceeds tolerance of 5kg)
        bool jumpStable = detector.Evaluate(new WeightReading(10050m, "kg", false, baseTime.AddMilliseconds(100), WeightSource.Indicator));
        Assert.False(jumpStable);

        // Next sample needs to rebuild sample count and duration
        Assert.False(detector.Evaluate(new WeightReading(10051m, "kg", false, baseTime.AddMilliseconds(150), WeightSource.Indicator)));
        Assert.False(detector.Evaluate(new WeightReading(10050m, "kg", false, baseTime.AddMilliseconds(200), WeightSource.Indicator)));
        Assert.True(detector.Evaluate(new WeightReading(10052m, "kg", false, baseTime.AddMilliseconds(350), WeightSource.Indicator)));
    }
}
