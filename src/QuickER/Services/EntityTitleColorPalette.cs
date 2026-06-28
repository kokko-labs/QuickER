using System.Collections.Generic;
using System.Windows.Media;
using QuickER.Documents;

namespace QuickER.Services;

/// <summary>エンティティのタイトル背景色 1 候補（表示ラベルと色コードの組）</summary>
public sealed class EntityTitleColorOption
{
    /// <summary>ラベルと色コードを指定して候補を生成する（色コードは正規化して保持する）</summary>
    public EntityTitleColorOption(string label, string colorHex)
    {
        Label = label;
        ColorHex = EntityTitleColorPalette.Normalize(colorHex);
    }

    /// <summary>UI に表示する色名</summary>
    public string Label { get; }

    /// <summary>正規化済みの色コード（<c>#RRGGBB</c>）</summary>
    public string ColorHex { get; }
}

/// <summary>エンティティタイトル背景色の選択肢を提供するパレット</summary>
public static class EntityTitleColorPalette
{
    /// <summary>UI で選択可能な色候補の一覧</summary>
    public static IReadOnlyList<EntityTitleColorOption> Options { get; } =
    [
        new("ブルー", EntityLayout.DefaultTitleBackgroundColor),
        new("グリーン", "#E4F1C9"),
        new("イエロー", "#FFF0BF"),
        new("パープル", "#E7DDF9"),
        new("ピンク", "#F8DDD7"),
        new("グレー", "#E9EEF5"),
    ];

    /// <summary>色コード文字列を <c>#RRGGBB</c> 形式へ正規化する</summary>
    /// <returns>解析できない場合は既定のタイトル背景色を返す</returns>
    public static string Normalize(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            return EntityLayout.DefaultTitleBackgroundColor;
        }

        try
        {
            // 名前付き色や #AARRGGBB 等も受理し、RGB 成分のみ抽出して 16 進へ整形する
            if (ColorConverter.ConvertFromString(colorHex.Trim()) is Color color)
            {
                return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }
        }
        catch
        {
            // 不正な色指定は握りつぶし、既定色へフォールバックする
        }

        return EntityLayout.DefaultTitleBackgroundColor;
    }
}
