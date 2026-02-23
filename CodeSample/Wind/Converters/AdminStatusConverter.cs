using System.Globalization;
using System.Windows.Data;

namespace Wind.Converters;

public class AdminStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isAdmin = value is bool b && b;
        return isAdmin ? "現在: 管理者権限で実行中" : "現在: 通常権限で実行中（次回起動時に昇格）";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
