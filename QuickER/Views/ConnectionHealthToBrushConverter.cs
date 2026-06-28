using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using QuickER.Services.Chat;

namespace QuickER.Views;

/// <summary>
/// <see cref="ConnectionHealth"/> を状態ドットのブラシへ変換する。
/// Ready=緑 / Pending=灰 / NeedsAction=赤。
/// </summary>
public sealed class ConnectionHealthToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ReadyBrush = CreateFrozen("#16A34A");
    private static readonly SolidColorBrush PendingBrush = CreateFrozen("#9CA3AF");
    private static readonly SolidColorBrush NeedsActionBrush = CreateFrozen("#DC2626");

    /// <inheritdoc />
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) =>
        value switch
        {
            ConnectionHealth.Ready => ReadyBrush,
            ConnectionHealth.NeedsAction => NeedsActionBrush,
            _ => PendingBrush,
        };

    /// <inheritdoc />
    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();

    /// <summary>16 進カラーから凍結済みブラシを生成する</summary>
    private static SolidColorBrush CreateFrozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
