using System.Windows;
using System.Windows.Controls;

namespace WeighBridge.App.Controls;

/// <summary>
/// Indeterminate progress indicator with an optional caption.
/// </summary>
/// <remarks>
/// The animation lives in the control template and is gated on
/// <see cref="IsActive"/> so that a collapsed spinner is not burning a render thread
/// animation while the operator is on another page.
/// </remarks>
public sealed class LoadingSpinner : Control
{
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(
            nameof(IsActive),
            typeof(bool),
            typeof(LoadingSpinner),
            new PropertyMetadata(true));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(
            nameof(Message),
            typeof(string),
            typeof(LoadingSpinner),
            new PropertyMetadata(string.Empty));

    /// <summary>Diameter of the spinning ring, in device-independent pixels.</summary>
    public static readonly DependencyProperty DiameterProperty =
        DependencyProperty.Register(
            nameof(Diameter),
            typeof(double),
            typeof(LoadingSpinner),
            new PropertyMetadata(32d));

    static LoadingSpinner()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(LoadingSpinner),
            new FrameworkPropertyMetadata(typeof(LoadingSpinner)));
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public double Diameter
    {
        get => (double)GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }
}
