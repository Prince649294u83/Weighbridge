using WeighBridge.Domain.Enums;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// Reference data placeholder. Will manage the vehicles, parties, materials and
/// transporters that weighments are recorded against.
/// </summary>
public sealed class MastersViewModel : ModulePlaceholderViewModel
{
    public MastersViewModel()
        : base(
            ApplicationModule.Masters,
            "Masters",
            "Maintain the vehicles, parties, materials and transporters that weighments refer to.",
            "Icon.Masters")
    {
    }
}
