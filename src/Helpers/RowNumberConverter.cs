using System.Globalization;
using System.Windows.Data;

namespace ChiliMusic;

public sealed class RowNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int index && index >= 0 ? (index + 1).ToString(culture) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
