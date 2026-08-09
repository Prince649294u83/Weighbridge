using WeighBridge.Core.Mvvm;
using WeighBridge.Domain.Enums;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// Base class for the module placeholders that stand in until each module is built.
/// </summary>
/// <remarks>
/// Every placeholder carries the identity its real implementation will have - module
/// key, title, description, icon - so replacing one is a matter of adding behaviour to
/// a ViewModel that the shell already knows how to navigate to and label.
/// </remarks>
public abstract class ModulePlaceholderViewModel : ViewModelBase
{
    protected ModulePlaceholderViewModel(ApplicationModule module, string title, string description, string iconKey)
    {
        Module = module;
        Title = title;
        Description = description;
        IconKey = iconKey;
    }

    /// <summary>Which module this placeholder stands in for.</summary>
    public ApplicationModule Module { get; }

    /// <summary>Resource key of the module's Segoe Fluent glyph.</summary>
    public string IconKey { get; }

    /// <summary>Status text shown in place of the module's real content.</summary>
    public string Status => "Coming Soon";
}
