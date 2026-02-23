using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Wind.Converters;

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isNull = value == null;
        bool invert = parameter?.ToString()?.ToLower() == "invert";

        if (invert) isNull = !isNull;

        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
