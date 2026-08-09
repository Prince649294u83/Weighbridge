using Microsoft.Extensions.Logging;
using WeighBridge.Core.Application;

namespace WeighBridge.Core.Logging;

/// <summary>
/// General application lifecycle and module activity.
/// </summary>
/// <remarks>
/// Each category is a distinct type so a constructor can ask for exactly the one it needs
/// and the container supplies it — a hardware service takes <see cref="HardwareLogger"/>
/// and cannot silently write into the audit trail. The behaviour lives entirely in
/// <see cref="CategoryLoggerBase"/>; these only name their category.
/// </remarks>
public sealed class ApplicationLogger(ILoggerFactory loggerFactory, IApplicationInfoService applicationInfo)
    : CategoryLoggerBase(LogCategory.Application, loggerFactory, applicationInfo);

/// <summary>
/// Weighbridge indicator, camera and printer traffic.
/// </summary>
/// <remarks>
/// Kept apart from the application log because hardware is the usual suspect when a site
/// reports a problem, and a support engineer needs to read the device conversation without
/// the rest of the application's chatter interleaved.
/// </remarks>
public sealed class HardwareLogger(ILoggerFactory loggerFactory, IApplicationInfoService applicationInfo)
    : CategoryLoggerBase(LogCategory.Hardware, loggerFactory, applicationInfo);

/// <summary>Database operations, timings and transaction outcomes.</summary>
public sealed class DatabaseLogger(ILoggerFactory loggerFactory, IApplicationInfoService applicationInfo)
    : CategoryLoggerBase(LogCategory.Database, loggerFactory, applicationInfo);

/// <summary>
/// Operator interaction: navigation, commands and dialogs.
/// </summary>
/// <remarks>
/// The named methods write at debug level by design. They answer "what did the operator
/// actually do before this went wrong", which is invaluable during a support call and pure
/// noise otherwise — so the category is off in normal running and turned on when a site
/// reports a problem.
/// </remarks>
public sealed class UIInteractionLogger(ILoggerFactory loggerFactory, IApplicationInfoService applicationInfo)
    : CategoryLoggerBase(LogCategory.UserInterface, loggerFactory, applicationInfo)
{
    /// <summary>Records a navigation between pages.</summary>
    public void Navigated(string from, string to)
        => Write(LogLevel.Debug, null, "Navigated from {From} to {To}", [from, to]);

    /// <summary>Records a command the operator invoked.</summary>
    public void CommandInvoked(string command, string? module = null)
        => Write(LogLevel.Debug, null, "Command {Command} invoked in {Module}", [command, module ?? "(unknown)"]);

    /// <summary>Records a dialog being shown.</summary>
    public void DialogShown(string dialog, string? title = null)
        => Write(LogLevel.Debug, null, "Dialog {Dialog} shown: {Title}", [dialog, title ?? string.Empty]);

    /// <summary>Records how the operator answered a dialog.</summary>
    public void DialogClosed(string dialog, string result)
        => Write(LogLevel.Debug, null, "Dialog {Dialog} closed with {Result}", [dialog, result]);
}
