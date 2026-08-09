using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WeighBridge.App.Converters;

/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the input is <c>null</c> or an empty
/// string, otherwise <see cref="Visibility.Collapsed"/>. Used to show placeholders.
/// </summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
