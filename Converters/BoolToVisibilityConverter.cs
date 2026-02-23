using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WindowzTabManager.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool boolValue = value is bool b && b;
        bool invert = parameter?.ToString()?.ToLowerInvariant() == "invert";

        if (invert)
            boolValue = !boolValue;

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool invert = parameter?.ToString()?.ToLowerInvariant() == "invert";
        bool result = value is Visibility v && v == Visibility.Visible;

        return invert ? !result : result;
    }
}
