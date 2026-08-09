using WeighBridge.Domain.Enums;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// Weighment capture placeholder. Will host the first and second weight workflow,
/// the live indicator reading and slip printing.
/// </summary>
public sealed class VehicleEntryViewModel : ModulePlaceholderViewModel
{
    public VehicleEntryViewModel()
        : base(
            ApplicationModule.VehicleEntry,
            "Vehicle Entry",
            "Capture gross and tare weights, read the indicator live and print the weighment slip.",
            "Icon.VehicleEntry")
    {
    }
}
