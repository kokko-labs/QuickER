using System.Collections.Generic;
using System.Windows.Media;
using ERDesigner.Models;

namespace ERDesigner.Services;

public sealed class EntityTitleColorOption
{
    public EntityTitleColorOption(string label, string colorHex)
    {
        Label = label;
        ColorHex = EntityTitleColorPalette.Normalize(colorHex);
    }

    public string Label { get; }

    public string ColorHex { get; }
}

public static class EntityTitleColorPalette
{
    public static IReadOnlyList<EntityTitleColorOption> Options { get; } =
    [
        new("ブルー", Entity.DefaultTitleBackgroundColor),
        new("グリーン", "#E4F1C9"),
        new("イエロー", "#FFF0BF"),
        new("パープル", "#E7DDF9"),
        new("ピンク", "#F8DDD7"),
        new("グレー", "#E9EEF5"),
    ];

    public static string Normalize(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            return Entity.DefaultTitleBackgroundColor;
        }

        try
        {
            if (ColorConverter.ConvertFromString(colorHex.Trim()) is Color color)
            {
                return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }
        }
        catch { }

        return Entity.DefaultTitleBackgroundColor;
    }
}
