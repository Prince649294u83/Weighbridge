using System.Windows;
using System.Windows.Controls;

namespace WeighBridge.App.Controls;

/// <summary>
/// Title, optional description and optional trailing actions, used to introduce a
/// page or a group of fields.
/// </summary>
public sealed class SectionHeader : Control
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(SectionHeader),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(
            nameof(Description),
            typeof(string),
            typeof(SectionHeader),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(
            nameof(Icon),
            typeof(string),
            typeof(SectionHeader),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionsProperty =
        DependencyProperty.Register(
            nameof(Actions),
            typeof(object),
            typeof(SectionHeader),
            new PropertyMetadata(null));

    /// <summary>Draws a hairline under the header. Off by default.</summary>
    public static readonly DependencyProperty ShowSeparatorProperty =
        DependencyProperty.Register(
            nameof(ShowSeparator),
            typeof(bool),
            typeof(SectionHeader),
            new PropertyMetadata(false));

    static SectionHeader()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SectionHeader),
            new FrameworkPropertyMetadata(typeof(SectionHeader)));
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public bool ShowSeparator
    {
        get => (bool)GetValue(ShowSeparatorProperty);
        set => SetValue(ShowSeparatorProperty, value);
    }
}
