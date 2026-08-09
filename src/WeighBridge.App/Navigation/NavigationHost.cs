using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using WeighBridge.Core.Mvvm;

namespace WeighBridge.App.Navigation;

/// <summary>
/// Content host that turns the shell's current ViewModel into a view.
/// </summary>
/// <remarks>
/// <para>
/// The host is the single place where a ViewModel becomes visual, which is what lets the
/// whole application live in one window: navigation replaces this control's content
/// rather than opening another window.
/// </para>
/// <para>
/// <see cref="ViewLocator"/> is a dependency property rather than something the control
/// looks up, so the shell passes in the instance it was given by the container and no
/// service location happens here.
/// </para>
/// </remarks>
public sealed class NavigationHost : ContentControl
{
    /// <summary>The ViewModel to present. Bound to the shell's current ViewModel.</summary>
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(ViewModelBase),
            typeof(NavigationHost),
            new PropertyMetadata(null, OnViewModelChanged));

    /// <summary>Supplies views for ViewModels. Must be assigned before the first navigation.</summary>
    public static readonly DependencyProperty ViewLocatorProperty =
        DependencyProperty.Register(
            nameof(ViewLocator),
            typeof(IViewLocator),
            typeof(NavigationHost),
            new PropertyMetadata(null, OnViewLocatorChanged));

    /// <summary>Whether a new view fades in. Disabled by default in automated tests.</summary>
    public static readonly DependencyProperty IsTransitionEnabledProperty =
        DependencyProperty.Register(
            nameof(IsTransitionEnabled),
            typeof(bool),
            typeof(NavigationHost),
            new PropertyMetadata(true));

    public ViewModelBase? ViewModel
    {
        get => (ViewModelBase?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public IViewLocator? ViewLocator
    {
        get => (IViewLocator?)GetValue(ViewLocatorProperty);
        set => SetValue(ViewLocatorProperty, value);
    }

    public bool IsTransitionEnabled
    {
        get => (bool)GetValue(IsTransitionEnabledProperty);
        set => SetValue(IsTransitionEnabledProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        => ((NavigationHost)sender).UpdateContent();

    private static void OnViewLocatorChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        // The locator is normally assigned after the binding has already delivered a
        // ViewModel, so re-run resolution once it arrives.
        ((NavigationHost)sender).UpdateContent();
    }

    private void UpdateContent()
    {
        var viewModel = ViewModel;
        var locator = ViewLocator;

        if (viewModel is null)
        {
            Content = null;
            return;
        }

        if (locator is null)
        {
            return;
        }

        Content = locator.Resolve(viewModel);

        if (IsTransitionEnabled)
        {
            PlayTransition();
        }
    }

    /// <summary>
    /// Fades and lifts the new view into place. Short enough that it never delays the
    /// operator, long enough to signal that the page changed.
    /// </summary>
    private void PlayTransition()
    {
        if (Content is not UIElement element)
        {
            return;
        }

        element.Opacity = 0;

        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        element.BeginAnimation(OpacityProperty, fade);
    }
}
