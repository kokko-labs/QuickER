using System.Globalization;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 書式のカルチャ（<see cref="CultureInfo.CurrentCulture"/>）を一時的に差し替え、破棄で元へ戻す
/// </summary>
/// <remarks>
/// 出力がカルチャに依らないことを確かめる総当たり（<c>CultureInvariant*Tests</c>）が共有する。UI の言語
/// （<see cref="CultureInfo.CurrentUICulture"/>）は変えない＝文言の言語は同じまま、数値・日時・大文字小文字の書式だけを変える。
/// </remarks>
public sealed class CultureScope : IDisposable
{
    /// <summary>
    /// 確かめるカルチャ。トルコ語（i の大文字小文字）・ドイツ語（小数点がカンマ）・日本語・スウェーデン語（負号が U+2212）・
    /// タイ語（暦が仏暦）
    /// </summary>
    public static IReadOnlyList<string> Cultures { get; } =
    ["tr-TR", "de-DE", "ja-JP", "sv-SE", "th-TH"];

    /// <summary>差し替える前のカルチャ</summary>
    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

    /// <summary>書式のカルチャを差し替える</summary>
    public CultureScope(string name) => CultureInfo.CurrentCulture = new CultureInfo(name);

    /// <summary>書式のカルチャを元へ戻す</summary>
    public void Dispose() => CultureInfo.CurrentCulture = _previous;

    /// <summary>インバリアントで作った出力と、各カルチャで作った出力の食い違いを並べる</summary>
    /// <param name="produce">出力を作る処理（同じ入力から何度呼んでも同じものを返すこと）</param>
    /// <returns>「カルチャ: 説明」の一覧（食い違いが無ければ空）</returns>
    public static IReadOnlyList<string> FindDifferences(Func<string> produce)
    {
        string baseline;

        using (new CultureScope(CultureInfo.InvariantCulture.Name))
        {
            baseline = produce();
        }

        var differences = new List<string>();

        foreach (var name in Cultures)
        {
            using var scope = new CultureScope(name);
            var actual = produce();

            if (actual != baseline)
            {
                differences.Add($"{name}: {DescribeFirstDifference(baseline, actual)}");
            }
        }

        return differences;
    }

    /// <summary>最初に食い違う位置の前後を返す</summary>
    private static string DescribeFirstDifference(string expected, string actual)
    {
        var index = 0;

        while (index < expected.Length && index < actual.Length && expected[index] == actual[index])
        {
            index++;
        }

        var start = Math.Max(0, index - 40);

        return $"位置 {index}: インバリアント「{Slice(expected, start)}」・このカルチャ「{Slice(actual, start)}」";
    }

    private static string Slice(string text, int start) =>
        text.Substring(start, Math.Min(80, text.Length - start)).ReplaceLineEndings("⏎");
}
