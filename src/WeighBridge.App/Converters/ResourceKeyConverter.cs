using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WeighBridge.App.Converters;

/// <summary>
/// Resolves a resource key to its value from the application's resource dictionaries.
/// </summary>
/// <remarks>
/// Lets a ViewModel name a resource - typically a glyph key from
/// <c>Resources/Icons.xaml</c> - without holding the value itself. Keeping private-use
/// font codepoints out of C# means a glyph is changed in one dictionary rather than
/// hunted through ViewModels, and the ViewModel stays testable without WPF loaded.
/// </remarks>
[ValueConversion(typeof(string), typeof(object))]
public sealed class ResourceKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || key.Length == 0)
        {
            return null;
        }

        // Null in a unit-test host, and a missing key is a design-time authoring slip
        // rather than something a running application can recover from - so fall back
        // to the key itself, which is visible in the UI and names its own cause.
        return Application.Current?.TryFindResource(key) ?? key;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
