namespace WeighBridge.Core.Abstractions;

/// <summary>
/// Controller interface for the weight indicator simulator.
/// </summary>
public interface IWeightIndicatorSimulator
{
    /// <summary>Sets the simulated live weight and stability flag.</summary>
    void SetWeight(decimal weightKg, bool isStable);

    /// <summary>Simulates a hardware disconnect event.</summary>
    void SimulateDisconnect();

    /// <summary>Simulates hardware reconnection.</summary>
    void SimulateReconnect();

    /// <summary>Toggles unstable noise jitter around the target weight.</summary>
    void SetJitter(bool enabled, decimal amplitudeKg = 20m);
}
