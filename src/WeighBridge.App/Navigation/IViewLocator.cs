using System.Windows;
using WeighBridge.Core.Mvvm;

namespace WeighBridge.App.Navigation;

/// <summary>
/// Resolves the view that presents a given ViewModel.
/// </summary>
/// <remarks>
/// Keeping the mapping behind an interface means the shell never references a concrete
/// view type, so a module can be added by registering its pair with the container
/// instead of by editing the shell.
/// </remarks>
public interface IViewLocator
{
    /// <summary>
    /// Returns the view for <paramref name="viewModel"/>, with the ViewModel already
    /// assigned as its <see cref="FrameworkElement.DataContext"/>.
    /// </summary>
    FrameworkElement Resolve(ViewModelBase viewModel);
}
