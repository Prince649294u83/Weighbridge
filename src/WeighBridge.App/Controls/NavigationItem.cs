using System.Windows;
using System.Windows.Controls;

namespace WeighBridge.App.Controls;

/// <summary>
/// A single entry in the left navigation panel: icon, label, selection accent bar and
/// an optional badge count.
/// </summary>
/// <remarks>
/// Derives from <see cref="RadioButton"/> so a group of items in the same panel gets
/// mutually exclusive selection for free, and keyboard arrow navigation behaves the
/// way Windows users expect from a navigation rail.
/// </remarks>
public sealed class NavigationItem : RadioButton
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(
            nameof(Icon),
            typeof(string),
            typeof(NavigationItem),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(
            nameof(Label),
            typeof(string),
            typeof(NavigationItem),
            new PropertyMetadata(string.Empty));

    /// <summary>Hides the label and centres the icon, for the collapsed rail.</summary>
    public static readonly DependencyProperty IsCollapsedProperty =
        DependencyProperty.Register(
            nameof(IsCollapsed),
            typeof(bool),
            typeof(NavigationItem),
            new PropertyMetadata(false));

    /// <summary>Optional trailing text, e.g. a pending count. Empty hides the badge.</summary>
    public static readonly DependencyProperty BadgeTextProperty =
        DependencyProperty.Register(
            nameof(BadgeText),
            typeof(string),
            typeof(NavigationItem),
            new PropertyMetadata(string.Empty));

    static NavigationItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(NavigationItem),
            new FrameworkPropertyMetadata(typeof(NavigationItem)));
    }

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool IsCollapsed
    {
        get => (bool)GetValue(IsCollapsedProperty);
        set => SetValue(IsCollapsedProperty, value);
    }

    public string BadgeText
    {
        get => (string)GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer()
        => new NavigationItemAutomationPeer(this);
}

public sealed class NavigationItemAutomationPeer : System.Windows.Automation.Peers.RadioButtonAutomationPeer,
    System.Windows.Automation.Provider.IInvokeProvider,
    System.Windows.Automation.Provider.ISelectionItemProvider
{
    public NavigationItemAutomationPeer(NavigationItem owner)
        : base(owner)
    {
    }

    public override object? GetPattern(System.Windows.Automation.Peers.PatternInterface patternInterface)
    {
        if (patternInterface == System.Windows.Automation.Peers.PatternInterface.Invoke ||
            patternInterface == System.Windows.Automation.Peers.PatternInterface.SelectionItem)
        {
            return this;
        }

        return base.GetPattern(patternInterface);
    }

    public void Invoke()
    {
        if (!IsEnabled())
        {
            throw new System.Windows.Automation.ElementNotEnabledException();
        }

        if (Owner is NavigationItem item)
        {
            item.IsChecked = true;
            if (item.Command?.CanExecute(item.CommandParameter) == true)
            {
                item.Command.Execute(item.CommandParameter);
            }
        }
    }

    void System.Windows.Automation.Provider.ISelectionItemProvider.Select()
    {
        if (!IsEnabled())
        {
            throw new System.Windows.Automation.ElementNotEnabledException();
        }

        if (Owner is NavigationItem item)
        {
            item.IsChecked = true;
            if (item.Command?.CanExecute(item.CommandParameter) == true)
            {
                item.Command.Execute(item.CommandParameter);
            }
        }
    }
}
