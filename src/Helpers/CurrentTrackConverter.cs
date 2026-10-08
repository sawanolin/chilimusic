using System.Globalization;
using System.Windows.Data;

namespace ChiliMusic;

public sealed class CurrentTrackConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => values.Length == 2 && values[0] is Track row && values[1] is Track current && row.Id == current.Id;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
