using WeighBridge.Domain.Enums;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// Administration placeholder. Will host user accounts, roles, audit trails and the
/// database maintenance tasks that only a supervisor should reach.
/// </summary>
public sealed class AdministrationViewModel : ModulePlaceholderViewModel
{
    public AdministrationViewModel()
        : base(
            ApplicationModule.Administration,
            "Administration",
            "User accounts, roles, audit history and database maintenance for supervisors.",
            "Icon.Administration")
    {
    }
}
