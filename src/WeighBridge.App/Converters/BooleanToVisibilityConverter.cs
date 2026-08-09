using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WeighBridge.App.Converters;

/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the input is <c>true</c>, otherwise
/// <see cref="Visibility.Collapsed"/>.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility visibility && visibility == Visibility.Visible;
}
