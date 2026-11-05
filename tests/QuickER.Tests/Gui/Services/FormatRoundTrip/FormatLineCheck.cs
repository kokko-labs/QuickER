using System.Text.RegularExpressions;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>行の集まり（出力のテキストの行・図の写しの行・警告）への期待を積む</summary>
/// <remarks>
/// パターンは <c>*</c> だけが任意の文字列（ほかの文字はそのまま比べる）。<c>*</c> は元のテストが見ていない部分だけに使う。
/// 部分一致の照合（出力のテキストの断片）は <see cref="Contains"/> / <see cref="Absent"/> に断片をそのまま渡す（パターンにしない）。
/// </remarks>
internal sealed class FormatLineCheck
{
    private readonly List<Func<IReadOnlyList<string>, string, IEnumerable<string>>> _checks = [];

    /// <summary>期待が 1 つも無い</summary>
    public bool IsEmpty => _checks.Count == 0;

    /// <summary>行の並びが期待と完全に一致する</summary>
    public FormatLineCheck Exact(params string[] patterns)
    {
        _checks.Add(
            (lines, what) =>
                lines.Count == patterns.Length
                && lines.Zip(patterns).All(pair => Like(pair.First, pair.Second))
                    ? []
                    :
                    [
                        $"{what}: 行が期待と一致しない\n  期待: [{string.Join(" | ", patterns)}]\n  実際: [{string.Join(" | ", lines)}]",
                    ]
        );
        return this;
    }

    /// <summary>行の集合が期待と一致する（並びは問わない）</summary>
    public FormatLineCheck Unordered(params string[] patterns)
    {
        _checks.Add(
            (lines, what) =>
            {
                var remaining = lines.ToList();

                foreach (var pattern in patterns)
                {
                    var hit = remaining.FindIndex(line => Like(line, pattern));

                    if (hit < 0)
                    {
                        return
                        [
                            $"{what}: {pattern} に合う行が無い（実際: [{string.Join(" | ", lines)}]）",
                        ];
                    }

                    remaining.RemoveAt(hit);
                }

                return remaining.Count == 0
                    ? []
                    : [$"{what}: 期待に無い行がある [{string.Join(" | ", remaining)}]"];
            }
        );
        return this;
    }

    /// <summary>パターンに合う行がちょうど 1 つある</summary>
    public FormatLineCheck Single(string pattern) => Count(pattern, 1);

    /// <summary>パターンに合う行がちょうど n ある</summary>
    public FormatLineCheck Count(string pattern, int count)
    {
        _checks.Add(
            (lines, what) =>
            {
                var hits = lines.Count(line => Like(line, pattern));

                return hits == count
                    ? []
                    :
                    [
                        $"{what}: {pattern} に合う行が {hits} 個（期待 {count}）\n  実際: [{string.Join(" | ", lines)}]",
                    ];
            }
        );
        return this;
    }

    /// <summary>パターンに合う行が無い</summary>
    public FormatLineCheck None(string pattern) => Count(pattern, 0);

    /// <summary>行が 1 つも無い</summary>
    public FormatLineCheck Empty()
    {
        _checks.Add(
            (lines, what) =>
                lines.Count == 0 ? [] : [$"{what}: 空でない [{string.Join(" | ", lines)}]"]
        );
        return this;
    }

    /// <summary>行を連結したテキストが断片を含む（部分一致・大文字小文字を区別）</summary>
    public FormatLineCheck Contains(string fragment)
    {
        _checks.Add(
            (lines, what) =>
                Joined(lines).Contains(fragment, StringComparison.Ordinal)
                    ? []
                    : [$"{what}: 断片 {Show(fragment)} を含まない"]
        );
        return this;
    }

    /// <summary>行を連結したテキストが断片を含まない</summary>
    public FormatLineCheck Absent(string fragment)
    {
        _checks.Add(
            (lines, what) =>
                Joined(lines).Contains(fragment, StringComparison.Ordinal)
                    ? [$"{what}: 断片 {Show(fragment)} を含む"]
                    : []
        );
        return this;
    }

    /// <summary>行を連結したテキストが正規表現に合う（元のテストが正規表現で見ていたときだけ使う）</summary>
    public FormatLineCheck Matches(string regex, int count = -1)
    {
        _checks.Add(
            (lines, what) =>
            {
                var hits = Regex.Matches(Joined(lines), regex, RegexOptions.CultureInvariant).Count;

                return (count < 0 ? hits > 0 : hits == count)
                    ? []
                    :
                    [
                        $"{what}: 正規表現 {regex} の一致が {hits} 個（期待 {(count < 0 ? "1 以上" : count)}）",
                    ];
            }
        );
        return this;
    }

    /// <summary>任意の検査（書式で書けない表明だけ。問題を文で返す）</summary>
    public FormatLineCheck That(string description, Func<IReadOnlyList<string>, bool> predicate)
    {
        _checks.Add(
            (lines, what) => predicate(lines) ? [] : [$"{what}: {description} が成り立たない"]
        );
        return this;
    }

    /// <summary>行に期待を当て、問題を返す</summary>
    public IEnumerable<string> Problems(IReadOnlyList<string> lines, string what) =>
        _checks.SelectMany(check => check(lines, what));

    /// <summary>パターンに合うか（<c>*</c> だけが任意の文字列）</summary>
    public static bool Like(string line, string pattern) =>
        Regex.IsMatch(
            line,
            "^" + string.Join(".*", pattern.Split('*').Select(Regex.Escape)) + "$",
            RegexOptions.Singleline | RegexOptions.CultureInvariant
        );

    private static string Joined(IReadOnlyList<string> lines) => string.Join("\n", lines);

    private static string Show(string text) =>
        "\"" + text.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
}
