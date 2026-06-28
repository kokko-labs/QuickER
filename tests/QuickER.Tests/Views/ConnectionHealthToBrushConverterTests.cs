using System.Globalization;
using System.Windows.Media;
using QuickER.Services.Chat;
using QuickER.Views;
using FluentAssertions;

using QuickER.AI;

namespace QuickER.Tests.Views;

/// <summary><see cref="ConnectionHealthToBrushConverter"/> の色マッピングを検証するテストクラス</summary>
public class ConnectionHealthToBrushConverterTests
{
    private static Color Convert(ConnectionHealth health)
    {
        var converter = new ConnectionHealthToBrushConverter();
        var brush = (SolidColorBrush)
            converter.Convert(health, typeof(Brush), null!, CultureInfo.InvariantCulture);
        return brush.Color;
    }

    [Fact(DisplayName = "Ready は緑")]
    public void Ready_IsGreen() =>
        Convert(ConnectionHealth.Ready)
            .Should()
            .Be((Color)ColorConverter.ConvertFromString("#16A34A"));

    [Fact(DisplayName = "Pending は灰")]
    public void Pending_IsGray() =>
        Convert(ConnectionHealth.Pending)
            .Should()
            .Be((Color)ColorConverter.ConvertFromString("#9CA3AF"));

    [Fact(DisplayName = "NeedsAction は赤")]
    public void NeedsAction_IsRed() =>
        Convert(ConnectionHealth.NeedsAction)
            .Should()
            .Be((Color)ColorConverter.ConvertFromString("#DC2626"));
}
