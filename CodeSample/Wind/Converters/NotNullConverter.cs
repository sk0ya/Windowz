using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Wind.Converters;

public class NotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNotNull = value != null;

        if (targetType == typeof(Visibility))
            return isNotNull ? Visibility.Visible : Visibility.Collapsed;

        return isNotNull;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
