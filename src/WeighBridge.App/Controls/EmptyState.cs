using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

namespace WeighBridge.App.Controls;

/// <summary>
/// Centred glyph, title, message and optional call-to-action shown in place of an
/// empty list, an unfiltered result set, or a feature that is not available yet.
/// </summary>
public sealed class EmptyState : Control
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(
            nameof(Icon),
            typeof(string),
            typeof(EmptyState),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(EmptyState),
            new PropertyMetadata("Nothing to show"));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(
            nameof(Message),
            typeof(string),
            typeof(EmptyState),
            new PropertyMetadata(string.Empty));

    /// <summary>Optional action button label. Empty hides the button.</summary>
    public static readonly DependencyProperty ActionTextProperty =
        DependencyProperty.Register(
            nameof(ActionText),
            typeof(string),
            typeof(EmptyState),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionCommandProperty =
        DependencyProperty.Register(
            nameof(ActionCommand),
            typeof(ICommand),
            typeof(EmptyState),
            new PropertyMetadata(null));

    static EmptyState()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(EmptyState),
            new FrameworkPropertyMetadata(typeof(EmptyState)));
    }

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    /// <summary>
    /// Reports the title and message to assistive technology as one text element.
    /// </summary>
    /// <remarks>
    /// Both are drawn by <see cref="TextBlock"/>s inside the control template, and WPF keeps
    /// templated text blocks out of the automation control view. Without this peer the reason
    /// a list is empty is on screen and unannounced, which is the one case where the screen
    /// has nothing else to say.
    /// </remarks>
    protected override AutomationPeer OnCreateAutomationPeer() => new EmptyStateAutomationPeer(this);
}

/// <summary>Peer that names an <see cref="EmptyState"/> by the wording it displays.</summary>
internal sealed class EmptyStateAutomationPeer(EmptyState owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

    protected override string GetClassNameCore() => nameof(EmptyState);

    /// <summary>An explicit <c>AutomationProperties.Name</c> wins; otherwise title and message.</summary>
    protected override string GetNameCore()
    {
        var name = base.GetNameCore();

        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        return string.IsNullOrEmpty(owner.Message)
            ? owner.Title ?? string.Empty
            : $"{owner.Title}. {owner.Message}";
    }

    protected override bool IsControlElementCore() => true;
}
