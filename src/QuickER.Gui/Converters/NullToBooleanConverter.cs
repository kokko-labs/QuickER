using System.Globalization;
using System.Windows.Data;

namespace QuickER.Converters;

/// <summary>
/// 値が null なら <c>false</c>、それ以外は <c>true</c> を返すコンバーター
/// </summary>
/// <remarks>
/// MultiDataTrigger の <c>Condition</c> は等値比較のみで「null でない」を直接表現できないため、
/// 選択中オブジェクトの有無を真偽値へ変換して条件（Value="True"）に用いる。
/// </remarks>
public class NullToBooleanConverter : IValueConverter
{
    /// <summary>値の null 判定を真偽値へ変換する（非 null なら true）</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null;

    /// <summary>逆変換は非対応</summary>
    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
