using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Wind.Converters;

public class BackgroundColorPreviewConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length < 2)
            return Brushes.Transparent;

        var colorCode = values[0]?.ToString();
        var name = values[1]?.ToString();

        // "Default" shows a gradient pattern to indicate theme default
        if (string.IsNullOrEmpty(colorCode) || name == "Default")
        {
            return new LinearGradientBrush(
                Color.FromRgb(60, 60, 60),
                Color.FromRgb(30, 30, 30),
                45);
        }

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(colorCode);
            return new SolidColorBrush(color);
        }
        catch
        {
            return Brushes.Transparent;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
