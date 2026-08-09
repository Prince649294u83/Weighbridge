using WeighBridge.Domain.Enums;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// Slip reprint placeholder. Will let an operator find a completed weighment and
/// reissue its slip, with every reprint recorded.
/// </summary>
public sealed class DuplicateSlipViewModel : ModulePlaceholderViewModel
{
    public DuplicateSlipViewModel()
        : base(
            ApplicationModule.DuplicateSlip,
            "Duplicate Slip",
            "Find a completed weighment and reissue its slip, with every reprint recorded for audit.",
            "Icon.DuplicateSlip")
    {
    }
}
