using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WeighBridge.App.Converters;

/// <summary>
/// Collapses the DataGrid column-header row unless <see cref="DataGridHeadersVisibility"/>
/// includes column headers.
/// </summary>
/// <remarks>
/// WPF's own DataGrid template uses an equivalent converter exposed on
/// <c>DataGrid.HeadersVisibilityConverter</c>, but that member is not part of the
/// public contract, so the retemplated grid in <c>Styles/DataGrid.xaml</c> uses this
/// one instead.
/// </remarks>
[ValueConversion(typeof(DataGridHeadersVisibility), typeof(Visibility))]
public sealed class ColumnHeadersVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var headers = value as DataGridHeadersVisibility? ?? DataGridHeadersVisibility.All;

        return headers is DataGridHeadersVisibility.All or DataGridHeadersVisibility.Column
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
