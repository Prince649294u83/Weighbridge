using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WeighBridge.Hardware.WeightIndicators;

/// <summary>
/// Hardware identification information for a detected serial COM port.
/// </summary>
public sealed record DiscoveredPortInfo(
    string PortName,
    string HardwareDescription,
    string ChipsetFamily,
    string? Vid,
    string? Pid,
    int Priority);

/// <summary>
/// Ultra-fast Windows PnP registry detector for serial hardware.
/// Queries the Windows Kernel Device Map (<c>SERIALCOMM</c>) and USB PnP Enumerator
/// in &lt;2ms, bypassing the slow 1200ms+ WMI layer.
/// Prioritizes known industrial weighbridge USB-UART dongles (Megawin MA112, CH340, FTDI, CP210x, Prolific).
/// </summary>
public static class FastHardwarePortDetector
{
    private static readonly (string Substring, string Chipset, string Description, int Priority)[] KnownChipsets =
    [
        ("VID_0E6A&PID_0122", "Megawin MA112", "Megawin MA112 USB-UART Bridge (Weighbridge Digitizer)", 100),
        ("VID_1A86&PID_7523", "WCH CH340", "WCH CH340 USB-Serial Converter (Yaohua/Avery Compatible)", 90),
        ("VID_1A86&PID_55D4", "WCH CH343", "WCH CH343/CH9102 USB-Serial High-Speed Dongle", 85),
        ("VID_0403&PID_6001", "FTDI FT232R", "FTDI USB-RS232 Serial Port", 80),
        ("VID_0403&PID_6015", "FTDI FT-X", "FTDI FT-X Series USB-UART", 78),
        ("VID_10C4&PID_EA60", "Silicon Labs CP210x", "Silicon Labs CP210x USB-to-UART Bridge", 75),
        ("VID_067B&PID_2303", "Prolific PL2303", "Prolific USB-to-Serial Comm Port", 70),
    ];

    /// <summary>
    /// Reorders a list of port names so that physical and USB-to-serial weighbridge adapters
    /// appear first, ordered by industrial relevance, followed by standard ports sorted numerically.
    /// </summary>
    public static IReadOnlyList<string> PrioritizePorts(IEnumerable<string> portNames)
    {
        var inputList = portNames
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (inputList.Count <= 1 || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return [.. inputList.OrderBy(PortNumber).ThenBy(p => p, StringComparer.OrdinalIgnoreCase)];
        }

        try
        {
            var detected = DetectAllPorts();
            var priorityMap = detected.ToDictionary(d => d.PortName, d => d.Priority, StringComparer.OrdinalIgnoreCase);

            return [.. inputList
                .OrderByDescending(p => priorityMap.TryGetValue(p, out var prio) ? prio : 10)
                .ThenBy(PortNumber)
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)];
        }
        catch
        {
            // Safety fallback to numerical order if registry access fails
            return [.. inputList.OrderBy(PortNumber).ThenBy(p => p, StringComparer.OrdinalIgnoreCase)];
        }
    }

    /// <summary>
    /// Scans the Windows Registry for all registered active COM ports and classifies their hardware chipset.
    /// </summary>
    public static IReadOnlyList<DiscoveredPortInfo> DetectAllPorts()
    {
        var results = new List<DiscoveredPortInfo>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return results;
        }

        try
        {
            // 1. Enumerate all active serial communication devices from Kernel DeviceMap
            using var serialCommKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (serialCommKey != null)
            {
                var valueNames = serialCommKey.GetValueNames();
                foreach (var name in valueNames)
                {
                    var portObj = serialCommKey.GetValue(name);
                    if (portObj is string portName && !string.IsNullOrWhiteSpace(portName))
                    {
                        var info = ResolvePortInfo(portName.Trim(), name);
                        results.Add(info);
                    }
                }
            }

            // 2. Scan USB Enum tree for USB serial ports that might provide rich hardware descriptions
            using var usbKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usbKey != null)
            {
                foreach (var vidPidKeyName in usbKey.GetSubKeyNames())
                {
                    using var vidPidKey = usbKey.OpenSubKey(vidPidKeyName);
                    if (vidPidKey == null) continue;

                    foreach (var instanceName in vidPidKey.GetSubKeyNames())
                    {
                        using var instanceKey = vidPidKey.OpenSubKey(instanceName);
                        if (instanceKey == null) continue;

                        using var devParamsKey = instanceKey.OpenSubKey("Device Parameters");
                        if (devParamsKey != null)
                        {
                            var portVal = devParamsKey.GetValue("PortName") as string;
                            if (!string.IsNullOrWhiteSpace(portVal))
                            {
                                string friendlyName = instanceKey.GetValue("FriendlyName") as string
                                    ?? instanceKey.GetValue("DeviceDesc") as string
                                    ?? vidPidKeyName;

                                // Extract VID / PID
                                string? vid = null;
                                string? pid = null;
                                var upperVidPid = vidPidKeyName.ToUpperInvariant();
                                int vidIdx = upperVidPid.IndexOf("VID_", StringComparison.Ordinal);
                                if (vidIdx >= 0 && vidIdx + 8 <= upperVidPid.Length)
                                {
                                    vid = upperVidPid.Substring(vidIdx + 4, 4);
                                }
                                int pidIdx = upperVidPid.IndexOf("PID_", StringComparison.Ordinal);
                                if (pidIdx >= 0 && pidIdx + 8 <= upperVidPid.Length)
                                {
                                    pid = upperVidPid.Substring(pidIdx + 4, 4);
                                }

                                int priority = 50; // default USB priority
                                string chipset = "Generic USB-Serial";

                                foreach (var known in KnownChipsets)
                                {
                                    if (upperVidPid.Contains(known.Substring, StringComparison.OrdinalIgnoreCase))
                                    {
                                        priority = known.Priority;
                                        chipset = known.Chipset;
                                        friendlyName = known.Description;
                                        break;
                                    }
                                }

                                // Update or add entry
                                int existingIdx = results.FindIndex(r => r.PortName.Equals(portVal, StringComparison.OrdinalIgnoreCase));
                                var enriched = new DiscoveredPortInfo(portVal, friendlyName, chipset, vid, pid, priority);
                                if (existingIdx >= 0)
                                {
                                    if (results[existingIdx].Priority < priority)
                                    {
                                        results[existingIdx] = enriched;
                                    }
                                }
                                else
                                {
                                    results.Add(enriched);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Best effort hardware inspection; registry errors must never crash the service
        }

        return results;
    }

    /// <summary>
    /// Trailing numeric extraction for sorting COM ports numerically (COM2 before COM10).
    /// </summary>
    public static int PortNumber(string name)
    {
        var digits = new string([.. name.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit)]);
        return int.TryParse(digits, out var number) ? number : int.MaxValue;
    }

    private static DiscoveredPortInfo ResolvePortInfo(string portName, string deviceMapName)
    {
        string desc = "Serial Communications Port";
        string chipset = "Standard Serial / Motherboard UART";
        int priority = 10;

        var lower = deviceMapName.ToLowerInvariant();
        if (lower.Contains("usb") || lower.Contains("vcp") || lower.Contains("silab") || lower.Contains("prolific") || lower.Contains("wch"))
        {
            priority = 50;
            chipset = "USB Serial Adapter";
            desc = $"USB Serial Port ({deviceMapName})";
        }
        else if (lower.Contains("bth") || lower.Contains("bluetooth"))
        {
            priority = 5;
            chipset = "Bluetooth Serial Modem";
            desc = "Bluetooth Serial Device";
        }

        return new DiscoveredPortInfo(portName, desc, chipset, null, null, priority);
    }
}
