using System.Windows;

namespace WeighBridge.App.Services;

/// <summary>
/// Restores and records the main window's size, position and maximised state.
/// </summary>
/// <remarks>
/// WPF-specific, so it lives here rather than in <c>WeighBridge.Core</c>; the state it
/// reads and writes is the framework-agnostic <c>WindowPlacement</c> preference.
/// </remarks>
public interface IWindowPlacementService
{
    /// <summary>
    /// Applies the persisted placement to <paramref name="window"/> and starts tracking
    /// its changes. Call before the window is shown.
    /// </summary>
    /// <remarks>
    /// Called once per session, not once per process: signing out closes the shell and the
    /// next sign-in supplies a new one. A second call replaces the tracked window.
    /// </remarks>
    void Attach(Window window);

    /// <summary>Captures the current placement and persists it.</summary>
    Task SaveAsync();
}
