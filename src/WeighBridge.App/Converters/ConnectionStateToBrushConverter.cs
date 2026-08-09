using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using WeighBridge.Domain.Enums;

namespace WeighBridge.App.Converters;

/// <summary>
/// Maps <see cref="ConnectionState"/> to a semantic brush for status indicators.
/// </summary>
[ValueConversion(typeof(ConnectionState), typeof(Brush))]
public sealed class ConnectionStateToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ConnectionState state)
        {
            return null;
        }

        var key = state switch
        {
            ConnectionState.Connected => "Brush.Success.Default",
            ConnectionState.Connecting => "Brush.Info.Default",
            ConnectionState.Degraded => "Brush.Warning.Default",
            ConnectionState.Disconnected => "Brush.Danger.Default",
            ConnectionState.Disabled => "Brush.Neutral.Default",
            _ => "Brush.Neutral.Default",
        };

        // Application.Current is null in a unit-test host.
        return System.Windows.Application.Current?.TryFindResource(key) as Brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
