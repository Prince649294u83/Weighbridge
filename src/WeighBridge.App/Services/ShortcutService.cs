using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Configuration;

namespace WeighBridge.App.Services;

/// <summary>
/// Native Windows shell shortcut creator ensuring zero external installer dependencies.
/// </summary>
public static class ShortcutService
{
    public static void EnsureDesktopShortcut(IOptions<WeighmentOptions> options, ILogger? logger)
    {
        EnsureDesktopShortcut(options?.Value?.AutoApplicationShortcut == true, logger);
    }

    public static void EnsureDesktopShortcut(bool enabled, ILogger? logger)
    {
        if (!enabled)
        {
            return;
        }

        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var shortcutPath = Path.Combine(desktopPath, "Smart WeighBridge.lnk");
            if (File.Exists(shortcutPath))
            {
                return;
            }

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                return;
            }

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = exePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
            shortcut.Description = "Smart WeighBridge Management System";
            shortcut.Save();

            logger?.LogInformation("Created desktop shortcut at {ShortcutPath}", shortcutPath);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to create desktop application shortcut");
        }
    }
}
