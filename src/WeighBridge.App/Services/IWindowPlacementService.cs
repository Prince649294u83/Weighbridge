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
    /// its changes. Call once, before the window is shown.
    /// </summary>
    void Attach(Window window);

    /// <summary>Captures the current placement and persists it.</summary>
    Task SaveAsync();
}
