using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ERDesigner.Converters;

/// <summary>
/// 値が null なら <see cref="Visibility.Collapsed"/>、それ以外は <see cref="Visibility.Visible"/> を返すコンバーター
/// </summary>
/// <remarks>選択中オブジェクトの有無に応じてプロパティパネルの表示を切り替える用途に用いる</remarks>
public class NullToVisibilityConverter : IValueConverter
{
    /// <summary>値の null 判定を <see cref="Visibility"/> へ変換する</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>逆変換は非対応</summary>
    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
