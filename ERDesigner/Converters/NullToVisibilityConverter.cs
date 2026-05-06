using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ERDesigner.Converters;

/// <summary>
/// 値が null なら <see cref="Visibility.Collapsed"/>、それ以外は <see cref="Visibility.Visible"/> を返すコンバーターです。
/// </summary>
/// <remarks>選択中オブジェクトを元にプロパティパネルの表示・非表示を切り替えるときに使います。</remarks>
public class NullToVisibilityConverter : IValueConverter
{
    /// <summary>値を <see cref="Visibility"/> に変換します。</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>逆変換はサポートしません。</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
