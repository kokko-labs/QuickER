using System.Globalization;
using System.Windows.Data;
using QuickER.AI;

namespace QuickER.AI.UI;

/// <summary>
/// <see cref="AiProvider"/> の表示名コンバーター。enum 名をそのまま出すと
/// <c>LocalLlm</c> のような内部表記が UI に出てしまうため、表示用の文字列へ変換する。
/// プロバイダー名は製品名（固有名詞）なので resx では持たず、言語に依らず同じ表記を使う。
/// </summary>
public class AiProviderDisplayNameConverter : IValueConverter
{
    /// <summary><see cref="AiProvider"/> を表示名へ変換する（enum 以外の値は素通し）</summary>
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value is AiProvider provider ? ToDisplayName(provider) : value;

    /// <summary>プロバイダーの表示名を返す</summary>
    /// <param name="provider">対象のプロバイダー</param>
    public static string ToDisplayName(AiProvider provider) =>
        provider switch
        {
            AiProvider.OpenAI => "OpenAI",
            AiProvider.Claude => "Claude",
            AiProvider.LocalLlm => "Local LLM",
            _ => provider.ToString(),
        };

    /// <summary>逆変換は非対応（表示専用。選択値は SelectedItem 経由で enum のまま渡る）</summary>
    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
