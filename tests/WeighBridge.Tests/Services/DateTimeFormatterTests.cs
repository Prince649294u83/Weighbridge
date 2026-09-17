using Microsoft.Extensions.Options;
using WeighBridge.Core.Configuration;
using WeighBridge.Services.Formatting;

namespace WeighBridge.Tests.Services;

/// <summary>
/// Verifies the authoritative DateTimeFormatter correctly switches between
/// 12-hour and 24-hour formats based on WeighmentOptions.TimeFormat,
/// and that format changes fire the FormatChanged event.
/// </summary>
public sealed class DateTimeFormatterTests
{
    private static IOptionsMonitor<WeighmentOptions> CreateMonitor(string timeFormat)
    {
        var options = new WeighmentOptions { TimeFormat = timeFormat };
        return new TestOptionsMonitor<WeighmentOptions>(options);
    }

    [Fact]
    public void FormatTime_24Hour_ReturnsHHmmss()
    {
        using var formatter = new DateTimeFormatter(CreateMonitor("24 Hour"));
        var dt = new DateTime(2026, 8, 31, 16, 35, 12);

        var result = formatter.FormatTime(dt);

        Assert.Equal("16:35:12", result);
    }

    [Fact]
    public void FormatTime_12Hour_Returnshhmmsstt()
    {
        using var formatter = new DateTimeFormatter(CreateMonitor("12 Hour"));
        var dt = new DateTime(2026, 8, 31, 16, 35, 12);

        var result = formatter.FormatTime(dt);

        Assert.Equal("04:35:12 PM", result);
    }

    [Fact]
    public void FormatShortTime_24Hour_ReturnsHHmm()
    {
        using var formatter = new DateTimeFormatter(CreateMonitor("24 Hour"));
        var dt = new DateTime(2026, 8, 31, 9, 5, 0);

        var result = formatter.FormatShortTime(dt);

        Assert.Equal("09:05", result);
    }

    [Fact]
    public void FormatShortTime_12Hour_Returnshhmmtt()
    {
        using var formatter = new DateTimeFormatter(CreateMonitor("12 Hour"));
        var dt = new DateTime(2026, 8, 31, 9, 5, 0);

        var result = formatter.FormatShortTime(dt);

        Assert.Equal("09:05 AM", result);
    }

    [Fact]
    public void FormatDateTime_24Hour_IncludesDateAndTime()
    {
        using var formatter = new DateTimeFormatter(CreateMonitor("24 Hour"));
        var dt = new DateTime(2026, 8, 31, 16, 35, 12);

        var result = formatter.FormatDateTime(dt);

        Assert.Equal("31/08/2026 16:35:12", result);
    }

    [Fact]
    public void FormatDateTime_12Hour_IncludesDateAndTime()
    {
        using var formatter = new DateTimeFormatter(CreateMonitor("12 Hour"));
        var dt = new DateTime(2026, 8, 31, 16, 35, 12);

        var result = formatter.FormatDateTime(dt);

        Assert.Equal("31/08/2026 04:35:12 PM", result);
    }

    [Fact]
    public void FormatDate_ReturnsConsistentFormat()
    {
        using var formatter = new DateTimeFormatter(CreateMonitor("24 Hour"));
        var dt = new DateTime(2026, 8, 31, 16, 35, 12);

        var result = formatter.FormatDate(dt);

        Assert.Equal("31/08/2026", result);
    }

    [Fact]
    public void FormatChanged_FiresWhenOptionsChange()
    {
        var monitor = new MutableTestOptionsMonitor(new WeighmentOptions { TimeFormat = "12 Hour" });
        using var formatter = new DateTimeFormatter(monitor);

        bool fired = false;
        formatter.FormatChanged += (_, _) => fired = true;

        monitor.Update(new WeighmentOptions { TimeFormat = "24 Hour" });

        Assert.True(fired, "FormatChanged should fire when options change");
    }

    [Fact]
    public void FormatTime_DefaultsTo12Hour_WhenTimeFormatIsNull()
    {
        using var formatter = new DateTimeFormatter(CreateMonitor(null!));
        var dt = new DateTime(2026, 8, 31, 16, 35, 12);

        // null is not "24 Hour", so defaults to 12-hour
        var result = formatter.FormatTime(dt);

        Assert.Equal("04:35:12 PM", result);
    }

    /// <summary>Simple IOptionsMonitor for testing with immutable options.</summary>
    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    /// <summary>Mutable IOptionsMonitor that fires OnChange callbacks.</summary>
    private sealed class MutableTestOptionsMonitor(WeighmentOptions initial) : IOptionsMonitor<WeighmentOptions>
    {
        private WeighmentOptions _current = initial;
        private readonly List<Action<WeighmentOptions, string?>> _listeners = [];

        public WeighmentOptions CurrentValue => _current;
        public WeighmentOptions Get(string? name) => _current;

        public IDisposable OnChange(Action<WeighmentOptions, string?> listener)
        {
            _listeners.Add(listener);
            return new CallbackDisposable(() => _listeners.Remove(listener));
        }

        public void Update(WeighmentOptions newOptions)
        {
            _current = newOptions;
            foreach (var listener in _listeners)
                listener(newOptions, null);
        }

        private sealed class CallbackDisposable(Action onDispose) : IDisposable
        {
            public void Dispose() => onDispose();
        }
    }
}
