using System.Globalization;
using System.Windows.Data;

namespace Wind.Converters;

public class StringEqualsConverter : IValueConverter, IMultiValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        return value.ToString()?.Equals(parameter.ToString(), StringComparison.OrdinalIgnoreCase) ?? false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter != null)
        {
            return parameter.ToString() ?? string.Empty;
        }

        return Binding.DoNothing;
    }

    // IMultiValueConverter implementation for comparing two bound values
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length < 2)
            return false;

        var value1 = values[0]?.ToString();
        var value2 = values[1]?.ToString();

        if (value1 == null || value2 == null)
            return false;

        return value1.Equals(value2, StringComparison.OrdinalIgnoreCase);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
