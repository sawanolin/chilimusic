using System.Globalization;
using System.Windows.Data;
namespace ChiliMusic;
public sealed class NavSelectedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => values.Length == 2 && values[0]?.ToString() == values[1]?.ToString();
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
