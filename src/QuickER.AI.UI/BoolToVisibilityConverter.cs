using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QuickER.AI.UI;

/// <summary>bool 値に応じて <see cref="Visibility"/> を切り替えるコンバーター（AI 共有 UI 部品用）</summary>
/// <remarks>
/// <see cref="AttachmentPanel"/> など AI.UI 内の UserControl がリソースとして自己保持するための最小実装。
/// アプリ本体（QuickER.Gui）側の同名コンバーターとは独立しており、AI.UI が Gui を参照しないための分離。
/// </remarks>
public class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>bool 値を <see cref="Visibility"/> へ変換する</summary>
    /// <param name="parameter">"Inverse" を指定すると真偽を反転する</param>
    /// <returns>真で <see cref="Visibility.Visible"/>、偽で <see cref="Visibility.Collapsed"/></returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var boolValue = value is true;

        if (
            parameter is string param
            && param.Equals("Inverse", StringComparison.OrdinalIgnoreCase)
        )
        {
            boolValue = !boolValue;
        }

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>逆変換は非対応</summary>
    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
