using System.Text.RegularExpressions;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 英語のはずの出力に日本語などの CJK の文字が混ざっていないかを見る、英語ガードのテストが共有する正規表現
/// </summary>
/// <remarks>
/// 範囲は U+3000〜U+9FFF（CJK の記号・かな・漢字など）と U+FF00〜U+FFEF（全角と半角の形）。
/// 英語ガードのテストごとに同じ正規表現を書くと、範囲を直すときに 1 か所だけ取り残される。写しを置かないことは
/// <c>CjkPatternGuardTests</c> がテストのソースの走査で固定する。
/// </remarks>
public static class CjkText
{
    /// <summary>CJK の文字 1 文字に一致する正規表現</summary>
    public static Regex Pattern { get; } =
        new("[\u3000-\u9FFF\uFF00-\uFFEF]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
