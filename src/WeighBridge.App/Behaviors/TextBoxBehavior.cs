using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WeighBridge.App.Behaviors;

/// <summary>
/// Attached behaviours for text input.
/// </summary>
/// <remarks>
/// Implemented as attached properties rather than Blend interaction triggers so the
/// application depends on nothing outside the approved stack. Attach from XAML:
/// <c>&lt;TextBox behaviors:TextBoxBehavior.SelectAllOnFocus="True" /&gt;</c>.
/// </remarks>
public static class TextBoxBehavior
{
    /// <summary>
    /// Selects the whole value when the box receives focus, so an operator retyping a
    /// vehicle number overwrites it instead of appending to it.
    /// </summary>
    public static readonly DependencyProperty SelectAllOnFocusProperty =
        DependencyProperty.RegisterAttached(
            "SelectAllOnFocus",
            typeof(bool),
            typeof(TextBoxBehavior),
            new PropertyMetadata(false, OnSelectAllOnFocusChanged));

    public static bool GetSelectAllOnFocus(DependencyObject element)
        => (bool)element.GetValue(SelectAllOnFocusProperty);

    public static void SetSelectAllOnFocus(DependencyObject element, bool value)
        => element.SetValue(SelectAllOnFocusProperty, value);

    private static void OnSelectAllOnFocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox)
        {
            return;
        }

        // Detach unconditionally first: the property can be re-set on a recycled
        // container, and double subscription would select twice per focus.
        textBox.GotKeyboardFocus -= OnGotKeyboardFocus;
        textBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;

        if (e.NewValue is true)
        {
            textBox.GotKeyboardFocus += OnGotKeyboardFocus;
            textBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        }
    }

    private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.IsKeyboardFocusWithin)
        {
            return;
        }

        // A click would normally place the caret and clear the selection made above,
        // so take the focus here and swallow the click.
        textBox.Focus();
        e.Handled = true;
    }
}
