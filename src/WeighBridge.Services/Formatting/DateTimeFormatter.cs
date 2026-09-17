using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Services.Formatting;

/// <summary>
/// Authoritative date and time formatter backed by IOptionsMonitor&lt;WeighmentOptions&gt;.
/// Dynamically updates formatting when TimeFormat is modified at runtime.
/// </summary>
public sealed class DateTimeFormatter : IDateTimeFormatter, IDisposable
{
    private readonly IOptionsMonitor<WeighmentOptions> _optionsMonitor;
    private readonly IDisposable? _changeToken;

    public event EventHandler? FormatChanged;

    public DateTimeFormatter(IOptionsMonitor<WeighmentOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _changeToken = _optionsMonitor.OnChange(_ => FormatChanged?.Invoke(this, EventArgs.Empty));
    }

    private bool Is24Hour =>
        string.Equals(_optionsMonitor.CurrentValue.TimeFormat, "24 Hour", StringComparison.OrdinalIgnoreCase);

    public string FormatTime(DateTime dateTime)
    {
        return dateTime.ToString(Is24Hour ? "HH:mm:ss" : "hh:mm:ss tt");
    }

    public string FormatShortTime(DateTime dateTime)
    {
        return dateTime.ToString(Is24Hour ? "HH:mm" : "hh:mm tt");
    }

    public string FormatDate(DateTime dateTime)
    {
        return dateTime.ToString("dd/MM/yyyy");
    }

    public string FormatDateTime(DateTime dateTime)
    {
        return dateTime.ToString(Is24Hour ? "dd/MM/yyyy HH:mm:ss" : "dd/MM/yyyy hh:mm:ss tt");
    }

    public void Dispose()
    {
        _changeToken?.Dispose();
    }
}
