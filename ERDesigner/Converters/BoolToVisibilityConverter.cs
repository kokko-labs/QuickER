using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ERDesigner.Converters;

/// <summary>
/// bool 値に応じて <see cref="Visibility"/> を切り替えるコンバーターです。
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>bool 値を <see cref="Visibility"/> に変換します。parameter に "Inverse" を指定すると反転します。</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var boolValue = value is true;

        if (parameter is string param && param.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
        {
            boolValue = !boolValue;
        }

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>逆変換はサポートしません。</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
