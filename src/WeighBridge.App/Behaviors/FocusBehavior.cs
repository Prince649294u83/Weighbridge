using System.Windows;
using System.Windows.Input;

namespace WeighBridge.App.Behaviors;

/// <summary>
/// Attached behaviours for keyboard focus.
/// </summary>
public static class FocusBehavior
{
    /// <summary>
    /// Moves keyboard focus to the element once it is loaded. Used to put the caret in
    /// the first field of a page or dialog without any code in the View.
    /// </summary>
    public static readonly DependencyProperty FocusOnLoadProperty =
        DependencyProperty.RegisterAttached(
            "FocusOnLoad",
            typeof(bool),
            typeof(FocusBehavior),
            new PropertyMetadata(false, OnFocusOnLoadChanged));

    public static bool GetFocusOnLoad(DependencyObject element)
        => (bool)element.GetValue(FocusOnLoadProperty);

    public static void SetFocusOnLoad(DependencyObject element, bool value)
        => element.SetValue(FocusOnLoadProperty, value);

    private static void OnFocusOnLoadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.Loaded -= OnLoaded;

        if (e.NewValue is true)
        {
            element.Loaded += OnLoaded;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        // Loaded fires before the visual tree is fully arranged for templated
        // controls, so queue the focus behind rendering at Input priority.
        element.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() => Keyboard.Focus(element)));
    }
}
