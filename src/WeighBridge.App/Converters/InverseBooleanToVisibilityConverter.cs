using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WeighBridge.App.Converters;

/// <summary>
/// Returns <see cref="Visibility.Collapsed"/> when the input is <c>true</c>, otherwise
/// <see cref="Visibility.Visible"/>. Inverted form of <see cref="BooleanToVisibilityConverter"/>.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility visibility && visibility == Visibility.Collapsed;
}
