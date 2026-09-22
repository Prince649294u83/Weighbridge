using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using WeighBridge.Core.Security;

namespace WeighBridge.Services.Security;

/// <summary>
/// Computes an immutable, hardware-bound machine identifier in the format WB-XXXX-XXXX-XXXX-XXXX.
/// </summary>
public sealed class HardwareIdProvider : IHardwareIdProvider
{
    private string? _cachedHardwareId;

    public string GetHardwareId()
    {
        if (_cachedHardwareId is not null)
        {
            return _cachedHardwareId;
        }

        var sb = new StringBuilder();

        // 1. Windows MachineGuid from Registry if available
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var guid = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null)?.ToString();
                if (!string.IsNullOrWhiteSpace(guid))
                {
                    sb.Append(guid);
                }
            }
            catch
            {
                // Fallback to hardware parameters
            }
        }

        // 2. Machine & processor properties
        sb.Append(Environment.MachineName);
        sb.Append(Environment.ProcessorCount);

        // 3. Primary Network Interface Physical (MAC) Address
        try
        {
            var mac = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(n => n.GetPhysicalAddress().ToString())
                .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));

            if (!string.IsNullOrWhiteSpace(mac))
            {
                sb.Append(mac);
            }
        }
        catch
        {
            // Defensive
        }

        var rawBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = SHA256.HashData(rawBytes);
        var hex = Convert.ToHexString(hash);

        // Format as WB-XXXX-XXXX-XXXX-XXXX (first 16 hex chars)
        _cachedHardwareId = $"WB-{hex[..4]}-{hex.Substring(4, 4)}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}";
        return _cachedHardwareId;
    }
}
