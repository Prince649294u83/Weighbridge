using System.Windows;
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
            new PropertyMetadata(string.Empty));

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
}
