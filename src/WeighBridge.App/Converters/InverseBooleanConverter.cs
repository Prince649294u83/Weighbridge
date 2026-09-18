using System.Globalization;
using System.Windows.Data;

namespace WeighBridge.App.Converters;

/// <summary>
/// Inverts a boolean value (true -> false, false -> true).
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? false : Binding.DoNothing;
        }

        return Binding.DoNothing;
    }
}
