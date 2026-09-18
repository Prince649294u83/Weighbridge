using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Hardware.WeightIndicators;

/// <summary>
/// Evaluates live weight stability using a rolling sample window and delta tolerance over time.
/// </summary>
public sealed class StabilityDetector
{
    private decimal _toleranceKg;
    private int _requiredSampleCount;
    private TimeSpan _requiredDuration;

    private readonly List<WeightReading> _samples = [];
    private DateTime? _stableSinceUtc;

    public StabilityDetector(WeightIndicatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        UpdateOptions(options);
    }

    /// <summary>Updates stability criteria dynamically.</summary>
    public void UpdateOptions(WeightIndicatorOptions options)
    {
        if (options == null) return;
        _toleranceKg = options.StabilityToleranceKg > 0 ? options.StabilityToleranceKg : 5.0m;
        _requiredSampleCount = options.StabilitySampleCount > 0 ? options.StabilitySampleCount : 5;
        _requiredDuration = TimeSpan.FromMilliseconds(options.StabilityDurationMs > 0 ? options.StabilityDurationMs : 1000);
    }

    /// <summary>
    /// Evaluates the reading and returns whether the live weight is stable.
    /// </summary>
    public bool Evaluate(WeightReading reading)
    {
        // If the hardware indicator protocol already explicitly flagged the frame as stable, honor it
        if (reading.IsStable)
        {
            _samples.Clear();
            _stableSinceUtc = reading.TimestampUtc;
            return true;
        }

        _samples.Add(reading);

        // Keep maximum 20 recent samples
        if (_samples.Count > 20)
        {
            _samples.RemoveAt(0);
        }

        if (_samples.Count < _requiredSampleCount)
        {
            _stableSinceUtc = null;
            return false;
        }

        // Check if all recent samples are within tolerance
        var recent = _samples.TakeLast(_requiredSampleCount).ToList();
        decimal min = recent.Min(r => r.Value);
        decimal max = recent.Max(r => r.Value);

        if (max - min <= _toleranceKg)
        {
            _stableSinceUtc ??= recent[0].TimestampUtc;
            var elapsed = reading.TimestampUtc - _stableSinceUtc.Value;
            return elapsed >= _requiredDuration;
        }

        // Fluctuation exceeded tolerance
        _stableSinceUtc = null;
        return false;
    }

    /// <summary>Resets the sample history.</summary>
    public void Reset()
    {
        _samples.Clear();
        _stableSinceUtc = null;
    }
}
