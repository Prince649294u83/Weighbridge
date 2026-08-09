using WeighBridge.Domain.Enums;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// Reporting placeholder. Will provide date-ranged and party-wise weighment reports
/// with print and export output.
/// </summary>
public sealed class ReportsViewModel : ModulePlaceholderViewModel
{
    public ReportsViewModel()
        : base(
            ApplicationModule.Reports,
            "Reports",
            "Date-ranged, party-wise and material-wise weighment reports, ready to print or export.",
            "Icon.Reports")
    {
    }
}
