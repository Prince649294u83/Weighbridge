using WeighBridge.Core.Mvvm;
using WeighBridge.Domain.Enums;

namespace WeighBridge.Core.Status;

/// <summary>
/// Observable state of one subsystem shown in the shell status bar.
/// </summary>
public sealed class SubsystemStatus : ObservableObject
{
    private ConnectionState _state = ConnectionState.Unknown;
    private string _detail = string.Empty;
    private DateTime? _lastCheckedAt;

    public SubsystemStatus(string name, ConnectionState initialState = ConnectionState.Unknown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        _state = initialState;
    }

    /// <summary>Display name, e.g. "Database".</summary>
    public string Name { get; }

    /// <summary>Current connection state, driving the indicator colour.</summary>
    public ConnectionState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    /// <summary>
    /// Optional extra information (provider name, port, error text) shown as a tooltip.
    /// </summary>
    public string Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    /// <summary>Local time of the most recent probe.</summary>
    public DateTime? LastCheckedAt
    {
        get => _lastCheckedAt;
        set => SetProperty(ref _lastCheckedAt, value);
    }

    /// <summary>Human-readable state text rendered next to the indicator.</summary>
    public string DisplayText => State switch
    {
        ConnectionState.Connected => "Connected",
        ConnectionState.Connecting => "Connecting",
        ConnectionState.Degraded => "Degraded",
        ConnectionState.Disconnected => "Disconnected",
        ConnectionState.Disabled => "Disabled",
        _ => "Unknown",
    };

    /// <summary>Applies a probe result in one call, stamping the check time.</summary>
    public void Update(ConnectionState state, string? detail = null)
    {
        State = state;
        Detail = detail ?? string.Empty;
        LastCheckedAt = DateTime.Now;
    }
}
