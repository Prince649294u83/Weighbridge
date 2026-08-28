using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using WeighBridge.Domain.Enums;

namespace WeighBridge.App.Controls;

/// <summary>
/// Severity levels a <see cref="StatusBadge"/> can display.
/// </summary>
public enum BadgeSeverity
{
    Neutral,
    Information,
    Success,
    Warning,
    Danger,
}

/// <summary>
/// Pill-shaped label with an optional leading dot, used for connection states,
/// weighment statuses and record flags.
/// </summary>
/// <remarks>
/// Set <see cref="Severity"/> directly, or bind <see cref="ConnectionState"/> and let
/// the badge derive both the severity and the text from the subsystem state.
/// </remarks>
public sealed class StatusBadge : Control
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(StatusBadge),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty SeverityProperty =
        DependencyProperty.Register(
            nameof(Severity),
            typeof(BadgeSeverity),
            typeof(StatusBadge),
            new PropertyMetadata(BadgeSeverity.Neutral));

    public static readonly DependencyProperty ShowDotProperty =
        DependencyProperty.Register(
            nameof(ShowDot),
            typeof(bool),
            typeof(StatusBadge),
            new PropertyMetadata(true));

    /// <summary>
    /// Optional connection state. When set, it overrides <see cref="Severity"/> and —
    /// unless <see cref="Text"/> was supplied — the displayed text.
    /// </summary>
    public static readonly DependencyProperty ConnectionStateProperty =
        DependencyProperty.Register(
            nameof(ConnectionState),
            typeof(ConnectionState?),
            typeof(StatusBadge),
            new PropertyMetadata(null, OnConnectionStateChanged));

    static StatusBadge()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(StatusBadge),
            new FrameworkPropertyMetadata(typeof(StatusBadge)));
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public BadgeSeverity Severity
    {
        get => (BadgeSeverity)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public bool ShowDot
    {
        get => (bool)GetValue(ShowDotProperty);
        set => SetValue(ShowDotProperty, value);
    }

    public ConnectionState? ConnectionState
    {
        get => (ConnectionState?)GetValue(ConnectionStateProperty);
        set => SetValue(ConnectionStateProperty, value);
    }

    /// <summary>
    /// Reports the badge to assistive technology as a text element carrying <see cref="Text"/>.
    /// </summary>
    /// <remarks>
    /// The pill is drawn by a <see cref="System.Windows.Controls.TextBlock"/> inside the control
    /// template, and WPF keeps templated text blocks out of the automation control view. Without
    /// this peer the badge is on screen and unannounced - a screen reader would not say which
    /// stage a weighment is at, or whether the indicator is connected.
    /// </remarks>
    protected override AutomationPeer OnCreateAutomationPeer() => new StatusBadgeAutomationPeer(this);

    private static void OnConnectionStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not StatusBadge badge || e.NewValue is not ConnectionState state)
        {
            return;
        }

        badge.Severity = state switch
        {
            Domain.Enums.ConnectionState.Connected => BadgeSeverity.Success,
            Domain.Enums.ConnectionState.Connecting => BadgeSeverity.Information,
            Domain.Enums.ConnectionState.Degraded => BadgeSeverity.Warning,
            Domain.Enums.ConnectionState.Disconnected => BadgeSeverity.Danger,
            _ => BadgeSeverity.Neutral,
        };

        if (string.IsNullOrEmpty(badge.Text))
        {
            badge.Text = state.ToString();
        }
    }

    /// <summary>Announces the new wording, so a stage change is not a silent one.</summary>
    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Only when a client is actually listening: FromElement returns the peer that was
        // created for one, and nothing at all otherwise.
        UIElementAutomationPeer.FromElement((StatusBadge)d)?.RaisePropertyChangedEvent(
            AutomationElementIdentifiers.NameProperty,
            e.OldValue ?? string.Empty,
            e.NewValue ?? string.Empty);
    }
}

/// <summary>Peer that names a <see cref="StatusBadge"/> by the text it displays.</summary>
internal sealed class StatusBadgeAutomationPeer(StatusBadge owner) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

    protected override string GetClassNameCore() => nameof(StatusBadge);

    /// <summary>An explicit <c>AutomationProperties.Name</c> wins; otherwise the displayed text.</summary>
    protected override string GetNameCore()
    {
        var name = base.GetNameCore();
        return string.IsNullOrEmpty(name) ? owner.Text ?? string.Empty : name;
    }

    protected override bool IsControlElementCore() => true;
}
