using System.Globalization;
using System.Windows.Data;

namespace QuickER.AI.UI;

/// <summary>
/// Codex プロバイダー名の表示用コンバーター。内部値 "openai"（Codex の組み込みプロバイダー ID・
/// 送信値のため小文字固定）だけを表示上 "OpenAI" へ変換し、config.toml 由来の
/// プロバイダー ID はそのまま表示する。
/// </summary>
public class CodexProviderDisplayNameConverter : IValueConverter
{
    /// <summary>内部値 "openai" を表示名 "OpenAI" へ変換する（それ以外は素通し）</summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (
            value is string provider
            && provider.Trim().Equals("openai", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "OpenAI";
        }

        return value;
    }

    /// <summary>逆変換は非対応（表示専用。選択値は SelectedItem 経由で内部値のまま渡る）</summary>
    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
