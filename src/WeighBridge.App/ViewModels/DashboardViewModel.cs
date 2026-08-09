using WeighBridge.Domain.Enums;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// Landing page placeholder. Will show the weighbridge's live status, today's totals
/// and shortcuts to the common operations.
/// </summary>
public sealed class DashboardViewModel : ModulePlaceholderViewModel
{
    public DashboardViewModel()
        : base(
            ApplicationModule.Dashboard,
            "Dashboard",
            "Live weighbridge status, daily totals and shortcuts to the operations you run most.",
            "Icon.Dashboard")
    {
    }
}
