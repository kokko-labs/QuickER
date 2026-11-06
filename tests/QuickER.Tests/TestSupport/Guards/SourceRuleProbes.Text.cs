namespace QuickER.Tests.TestSupport.Guards;

/// <summary>規則の自己検査の表：ソースのテキストの横断の規則（見えない文字・英語ガードの CJK の範囲）</summary>
/// <remarks>
/// このファイルも tests を走査する規則の範囲に入る。見えない文字は数値から作って連結し、CJK の範囲の写しは生文字列の中に
/// エスケープの表記で書く（生文字列はエスケープを解かないので、このファイルのトークンの値には範囲の文字が現れない）。
/// </remarks>
internal static partial class SourceRuleProbes
{
    /// <summary>C# のユニコードのエスケープの始まり（バックスラッシュと u の 2 文字）</summary>
    private static readonly string UnicodeEscape = new([(char)0x5C, 'u']);

    /// <summary>CJK の文字の範囲の写し（実文字。数値から作る）</summary>
    private static readonly string RealCjkRange = new([(char)0x3000, '-', (char)0x9FFF]);

    private static IEnumerable<SourceRuleProbe> Text() =>
        [
            // 5 種の見えない文字を 1 文字 1 件で拾い、CR・LF は拾わない
            new SourceRuleProbe(
                "source-invisible-characters-src-forms",
                "source-invisible-characters-src",
                [
                    new ProbeFile(
                        "x.cs",
                        string.Concat(
                            "a",
                            (char)0x0009,
                            "\n",
                            (char)0x000C,
                            (char)0x0085,
                            "\n\n",
                            (char)0x2028,
                            (char)0x2029
                        ),
                        "TAB",
                        "FF",
                        "NEL",
                        "U+2028",
                        "U+2029"
                    ),
                    new ProbeFile("plain.cs", "plain\r\ntext\n"),
                ]
            ),
            // 文字列で読む規則は、条件コンパイルの外れる分岐の中の文字も拾う
            new SourceRuleProbe(
                "source-invisible-characters-tests-inactive-branch",
                "source-invisible-characters-tests",
                [
                    new ProbeFile(
                        "Probe.cs",
                        string.Concat(
                            "internal static class Probe\n{\n#if QUICKER_PROBE_BRANCH\n    const string Path = \"C:",
                            (char)0x0009,
                            "emp\";\n#endif\n}\n"
                        ),
                        "TAB"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // 文字列リテラルの値に範囲の写しを含むものを拾い（エスケープの表記で書いた写しも）、逐語的文字列のエスケープの表記・
            // コメント・「-」でつながない表記・範囲の片側だけは拾わない
            new SourceRuleProbe(
                "cjk-range-copies-forms",
                "cjk-range-copies",
                [
                    new ProbeFile(
                        "Probe.cs",
                        $$"""
                        internal static class Probe
                        {
                            // [{{RealCjkRange}}]
                            public const string A = "[{{UnicodeEscape}}3000-{{UnicodeEscape}}9FFF]";
                            public const string B = "[{{UnicodeEscape}}FF00-{{UnicodeEscape}}FFEF{{UnicodeEscape}}3000-{{UnicodeEscape}}9FFF]";
                            public const string C = @"[{{UnicodeEscape}}3000-{{UnicodeEscape}}9FFF]";
                            public const string D = "[{{UnicodeEscape}}3000{{UnicodeEscape}}9FFF]";
                            public const string E = "[{{UnicodeEscape}}3000-{{UnicodeEscape}}9FFE]";
                            public const string F = "[{{RealCjkRange}}]";
                        }
                        """,
                        "CJK の範囲",
                        "CJK の範囲",
                        "CJK の範囲"
                    ),
                ]
            ),
            // IgnoreCase だけのビット和・インラインの (?i を拾い、CultureInvariant を併せて付けたもの（括弧で包んだものも）は拾わない
            new SourceRuleProbe(
                "regex-ignore-case-culture-forms",
                "regex-ignore-case-culture",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System.Text.RegularExpressions;

                        internal static class Probe
                        {
                            public static readonly Regex A = new("x", RegexOptions.IgnoreCase);
                            public static readonly Regex B = new("x", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                            public static readonly Regex C = new("(?i)x");
                            public static readonly Regex D = new("x", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                            public static readonly Regex E = new("x", (RegexOptions.IgnoreCase | RegexOptions.Compiled) | RegexOptions.CultureInvariant);
                        }
                        """,
                        "RegexOptions.IgnoreCase",
                        "RegexOptions.Compiled | RegexOptions.IgnoreCase",
                        "インラインの (?i"
                    ),
                ]
            ),
            // \d は直前の \ の数が奇数のときだけ数え（元の判定の 4 行：a\d・a\\d・a\\\d・[0-9]d）、ECMAScript を付けた呼び出しの引数は数えない
            new SourceRuleProbe(
                "regex-digit-classes-forms",
                "regex-digit-classes",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System.Text.RegularExpressions;

                        internal static class Probe
                        {
                            public static readonly Regex A = new(@"a\d");
                            public static readonly Regex B = new(@"a\\d");
                            public static readonly Regex C = new(@"a\\\d");
                            public static readonly Regex D = new(@"[0-9]d");
                            public static readonly Regex E = new(@"a\d", RegexOptions.ECMAScript);
                        }
                        """,
                        """@"a\d" """.TrimEnd(),
                        """@"a\\\d" """.TrimEnd()
                    ),
                ]
            ),
        ];
}
