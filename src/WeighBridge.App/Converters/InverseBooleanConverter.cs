using System.Globalization;
using System.Windows.Data;

namespace WeighBridge.App.Converters;

/// <summary>
/// Inverts a boolean: <c>true</c> becomes <c>false</c> and vice versa.
/// </summary>
[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;
}
