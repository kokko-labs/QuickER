using System.Globalization;

namespace QuickER.Services;

/// <summary>
/// 表示言語コードの解決を担う純粋関数のヘルパ（UI・WPF に依存しないため単体テスト可能）。
/// </summary>
/// <remarks>
/// サポート言語は日本語（<c>ja</c>）と英語（<c>en</c>）の 2 つ。中立カルチャ＝日本語を正とし、
/// 英語はサテライトリソースで賄う。設定未指定・不正値のときは OS 言語から導出する
/// （OS が日本語なら <c>ja</c>・それ以外は <c>en</c>）。
/// </remarks>
public static class AppLanguage
{
    /// <summary>日本語の言語コード</summary>
    public const string Japanese = "ja";

    /// <summary>英語の言語コード</summary>
    public const string English = "en";

    /// <summary>
    /// 設定値と OS カルチャから実効言語コード（<c>"ja"</c> / <c>"en"</c>）を解決する。
    /// </summary>
    /// <param name="setting">設定に保存された言語コード（未設定は <c>null</c>・不正値も許容する）</param>
    /// <param name="osCulture">OS のカルチャ（未設定・不正値のときの導出元）</param>
    /// <returns>実効言語コード（必ず <c>"ja"</c> か <c>"en"</c> のいずれか）</returns>
    public static string Resolve(string? setting, CultureInfo osCulture)
    {
        // 明示設定が有効値ならそのまま採用する（大文字小文字・前後空白は許容する）
        var normalized = setting?.Trim().ToLowerInvariant();

        if (normalized == Japanese || normalized == English)
        {
            return normalized;
        }

        // 未設定・不正値は OS 言語から導出する（日本語系なら ja・それ以外は en）
        return IsJapanese(osCulture) ? Japanese : English;
    }

    /// <summary>指定カルチャが日本語系（言語が <c>ja</c>）かどうかを判定する</summary>
    private static bool IsJapanese(CultureInfo culture)
    {
        // ja / ja-JP など地域付きも含めて二文字言語コードで判定する
        return string.Equals(
            culture.TwoLetterISOLanguageName,
            Japanese,
            StringComparison.OrdinalIgnoreCase
        );
    }
}
