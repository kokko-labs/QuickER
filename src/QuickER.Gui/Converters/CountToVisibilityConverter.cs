using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QuickER.Converters;

/// <summary>
/// 整数の件数が 0 より大きければ <see cref="Visibility.Visible"/>、0 以下なら <see cref="Visibility.Collapsed"/> を返すコンバーター
/// </summary>
/// <remarks>検索候補が 1 件以上あるときだけ候補リストを表示する用途に用いる</remarks>
public class CountToVisibilityConverter : IValueConverter
{
    /// <summary>件数（int）を <see cref="Visibility"/> へ変換する</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>逆変換は非対応</summary>
    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
