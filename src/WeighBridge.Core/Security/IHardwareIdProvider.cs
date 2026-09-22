namespace WeighBridge.Core.Security;

/// <summary>
/// Provides a deterministic, hardware-bound machine identifier (e.g. WB-XXXX-XXXX-XXXX-XXXX).
/// </summary>
public interface IHardwareIdProvider
{
    /// <summary>
    /// Computes or retrieves the machine's hardware ID.
    /// </summary>
    string GetHardwareId();
}
