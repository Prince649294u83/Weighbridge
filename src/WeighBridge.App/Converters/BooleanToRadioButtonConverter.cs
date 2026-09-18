using System.Globalization;
using System.Windows.Data;

namespace WeighBridge.App.Converters;

/// <summary>
/// Converts a boolean value to a RadioButton IsChecked state using a ConverterParameter ("True" or "False").
/// Crucially returns <see cref="Binding.DoNothing"/> in ConvertBack when value is false, ensuring that uncheck events
/// caused by WPF group management or visual tree tab unloading NEVER overwrite the ViewModel property.
/// </summary>
public sealed class BooleanToRadioButtonConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            var target = ParseTargetParameter(parameter);
            return b == target;
        }

        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Only update the ViewModel when the user explicitly CHECKS this radio button (value == true).
        // Returning Binding.DoNothing when value == false prevents uncheck/unload events from clobbering the source.
        if (value is bool b && b)
        {
            return ParseTargetParameter(parameter);
        }

        return Binding.DoNothing;
    }

    private static bool ParseTargetParameter(object? parameter)
    {
        if (parameter is null)
        {
            return true;
        }

        if (parameter is bool b)
        {
            return b;
        }

        return bool.TryParse(parameter.ToString(), out var parsed) && parsed;
    }
}
