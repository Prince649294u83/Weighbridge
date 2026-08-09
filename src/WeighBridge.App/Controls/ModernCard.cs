using System.Windows;
using System.Windows.Controls;

namespace WeighBridge.App.Controls;

/// <summary>
/// Rounded container with a soft shadow — the standard content surface for the
/// application. Optionally shows a header (title, subtitle, icon) above its content.
/// </summary>
/// <remarks>
/// Views should place content inside a <see cref="ModernCard"/> rather than styling
/// their own borders, so padding, radius and elevation stay consistent everywhere.
/// </remarks>
public sealed class ModernCard : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(
            nameof(Header),
            typeof(string),
            typeof(ModernCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubHeaderProperty =
        DependencyProperty.Register(
            nameof(SubHeader),
            typeof(string),
            typeof(ModernCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(
            nameof(Icon),
            typeof(string),
            typeof(ModernCard),
            new PropertyMetadata(string.Empty));

    /// <summary>Content shown at the right edge of the header row (usually buttons).</summary>
    public static readonly DependencyProperty HeaderActionsProperty =
        DependencyProperty.Register(
            nameof(HeaderActions),
            typeof(object),
            typeof(ModernCard),
            new PropertyMetadata(null));

    /// <summary>Set to <c>false</c> for cards nested inside another elevated surface.</summary>
    public static readonly DependencyProperty HasShadowProperty =
        DependencyProperty.Register(
            nameof(HasShadow),
            typeof(bool),
            typeof(ModernCard),
            new PropertyMetadata(true));

    static ModernCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ModernCard),
            new FrameworkPropertyMetadata(typeof(ModernCard)));
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string SubHeader
    {
        get => (string)GetValue(SubHeaderProperty);
        set => SetValue(SubHeaderProperty, value);
    }

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public object? HeaderActions
    {
        get => GetValue(HeaderActionsProperty);
        set => SetValue(HeaderActionsProperty, value);
    }

    public bool HasShadow
    {
        get => (bool)GetValue(HasShadowProperty);
        set => SetValue(HasShadowProperty, value);
    }
}
