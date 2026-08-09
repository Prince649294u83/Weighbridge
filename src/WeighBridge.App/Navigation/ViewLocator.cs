using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Windows;
using WeighBridge.Core.Mvvm;

namespace WeighBridge.App.Navigation;

/// <summary>
/// Maps a ViewModel to its view by naming convention, then builds the view through the
/// container.
/// </summary>
/// <remarks>
/// <para>
/// The convention is <c>WeighBridge.App.ViewModels.FooViewModel</c> →
/// <c>WeighBridge.App.Views.FooView</c>. A module that needs to break the convention can
/// register an explicit pair with <see cref="Register{TViewModel, TView}"/> during
/// startup, and that registration wins.
/// </para>
/// <para>
/// Resolution goes through <see cref="ActivatorUtilities"/> rather than
/// <c>Activator.CreateInstance</c> so that a view needing a service - a chart renderer,
/// say - can still take it in its constructor. Views themselves stay logic-free; this is
/// only about construction.
/// </para>
/// </remarks>
public sealed class ViewLocator : IViewLocator
{
    private const string ViewModelSuffix = "ViewModel";
    private const string ViewSuffix = "View";
    private const string ViewModelNamespaceSegment = ".ViewModels.";
    private const string ViewNamespaceSegment = ".Views.";

    private readonly IServiceProvider _services;
    private readonly ILogger<ViewLocator> _logger;

    /// <summary>Explicit and previously resolved mappings, cached to avoid repeat reflection.</summary>
    private readonly ConcurrentDictionary<Type, Type> _mappings = new();

    public ViewLocator(IServiceProvider services, ILogger<ViewLocator> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <summary>
    /// Registers an explicit ViewModel-to-view mapping, overriding the naming convention.
    /// </summary>
    public void Register<TViewModel, TView>()
        where TViewModel : ViewModelBase
        where TView : FrameworkElement
        => _mappings[typeof(TViewModel)] = typeof(TView);

    /// <inheritdoc />
    public FrameworkElement Resolve(ViewModelBase viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        var viewModelType = viewModel.GetType();
        var viewType = _mappings.GetOrAdd(viewModelType, ResolveViewType);

        var view = (FrameworkElement)ActivatorUtilities.GetServiceOrCreateInstance(_services, viewType);
        view.DataContext = viewModel;

        _logger.LogDebug("Resolved {View} for {ViewModel}", viewType.Name, viewModelType.Name);

        return view;
    }

    /// <summary>Applies the naming convention, throwing a diagnosable error if it fails.</summary>
    private static Type ResolveViewType(Type viewModelType)
    {
        var viewModelName = viewModelType.FullName
            ?? throw new InvalidOperationException($"{viewModelType.Name} has no full name and cannot be mapped to a view.");

        if (!viewModelName.EndsWith(ViewModelSuffix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{viewModelName}' does not end in '{ViewModelSuffix}'. Register an explicit view mapping for it.");
        }

        var viewName = viewModelName[..^ViewModelSuffix.Length] + ViewSuffix;

        viewName = viewName.Replace(
            ViewModelNamespaceSegment,
            ViewNamespaceSegment,
            StringComparison.Ordinal);

        var viewType = viewModelType.Assembly.GetType(viewName, throwOnError: false)
            ?? throw new InvalidOperationException(
                $"No view named '{viewName}' was found for '{viewModelName}'. " +
                $"Create it, or register an explicit mapping with {nameof(ViewLocator)}.{nameof(Register)}.");

        if (!typeof(FrameworkElement).IsAssignableFrom(viewType))
        {
            throw new InvalidOperationException($"'{viewName}' must derive from {nameof(FrameworkElement)}.");
        }

        return viewType;
    }
}
