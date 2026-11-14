namespace QuickER.Tests.TestSupport.Guards;

/// <summary>規則の自己検査の表：ソースのテキストの横断の規則（見えない文字・英語ガードの CJK の範囲・カルチャ・変異テストの Safe Mode）</summary>
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
            // || の 2 つ目以降の項の out の宣言と、条件のどこかのパターンの変数（単独の is not { } を含む）を if の後ろで読む
            // 早く抜ける if を、continue・break・return;・throw・本体の最後の跳びで拾い、1 つ目の項の out の宣言・値を返す return・
            // 後ろで読まない・肯定形の && の then・項ごとの if・初期化子つきの変数の null の判定・ラムダの中の宣言・ブロックでない
            // 親は拾わない
            new SourceRuleProbe(
                "mutation-safe-mode-jump-guards-forms",
                "mutation-safe-mode-jump-guards",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System;
                        using System.Collections.Generic;
                        using System.Linq;

                        internal static class Probe
                        {
                            public static void OutSecond(Dictionary<int, string> map, int[] keys)
                            {
                                foreach (var key in keys)
                                {
                                    if (key < 0 || !map.TryGetValue(key, out var value))
                                    {
                                        continue;
                                    }

                                    Console.WriteLine(value);
                                }
                            }

                            public static void PatternSecond(string? text, object? other)
                            {
                                if (text is null || other is not string found)
                                {
                                    return;
                                }

                                Console.WriteLine(found);
                            }

                            public static string Throws(Dictionary<int, string> map, int? key)
                            {
                                if (key is not { } id || !map.TryGetValue(id, out var value))
                                {
                                    throw new InvalidOperationException("missing");
                                }

                                return value;
                            }

                            public static void BreaksAfterWork(Dictionary<int, string> map, int[] keys, List<int> missed)
                            {
                                while (true)
                                {
                                    if (keys.Length == 0 || !map.TryGetValue(keys[0], out var value))
                                    {
                                        missed.Add(keys.Length);
                                        break;
                                    }

                                    Console.WriteLine(value);
                                }
                            }

                            public static void PatternOnly(Dictionary<int, string> map, int[] keys)
                            {
                                foreach (var key in keys)
                                {
                                    if (map.GetValueOrDefault(key) is not { } value)
                                    {
                                        continue;
                                    }

                                    Console.WriteLine(value);
                                }
                            }

                            public static void NullCheck(Dictionary<int, string> map, int key)
                            {
                                var value = map.GetValueOrDefault(key);

                                if (value is null)
                                {
                                    return;
                                }

                                Console.WriteLine(value);
                            }

                            public static void FirstOperand(Dictionary<int, string> map, int key)
                            {
                                if (!map.TryGetValue(key, out var value) || value.Length == 0)
                                {
                                    return;
                                }

                                Console.WriteLine(value);
                            }

                            public static string? ValueReturn(Dictionary<int, string> map, int key)
                            {
                                if (key < 0 || !map.TryGetValue(key, out var value))
                                {
                                    return null;
                                }

                                return value;
                            }

                            public static void NotReadAfter(Dictionary<int, string> map, int key)
                            {
                                if (key < 0 || !map.TryGetValue(key, out var value) || value.Length == 0)
                                {
                                    return;
                                }

                                Console.WriteLine(key);
                            }

                            public static void Positive(Dictionary<int, string> map, int key)
                            {
                                if (key >= 0 && map.TryGetValue(key, out var value))
                                {
                                    Console.WriteLine(value);
                                    return;
                                }

                                Console.WriteLine(key);
                            }

                            public static void Split(Dictionary<int, string> map, int key)
                            {
                                if (key < 0)
                                {
                                    return;
                                }

                                if (!map.TryGetValue(key, out var value))
                                {
                                    return;
                                }

                                Console.WriteLine(value);
                            }

                            public static void InLambda(List<string> items, string value)
                            {
                                if (items.Count == 0 || items.Any(item => item is { Length: > 0 } value))
                                {
                                    return;
                                }

                                Console.WriteLine(value);
                            }

                            public static void NotInBlock(Dictionary<int, string> map, int key, bool flag)
                            {
                                if (flag)
                                    if (key < 0 || !map.TryGetValue(key, out var value))
                                        return;
                            }
                        }
                        """,
                        "OutSecond",
                        "PatternSecond",
                        "Throws",
                        "BreaksAfterWork",
                        "PatternOnly"
                    ),
                ]
            ),
            // 条件コンパイルの外れる分岐の中の早く抜ける if も拾う
            new SourceRuleProbe(
                "mutation-safe-mode-jump-guards-inactive-branch",
                "mutation-safe-mode-jump-guards",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System;
                        using System.Collections.Generic;

                        internal static class Probe
                        {
                        #if QUICKER_PROBE_NEVER
                            public static void Hidden(Dictionary<int, string> map, int key)
                            {
                                if (key < 0 || !map.TryGetValue(key, out var value))
                                {
                                    return;
                                }

                                Console.WriteLine(value);
                            }
                        #endif
                        }
                        """,
                        "Hidden"
                    ),
                ]
            )
            {
                InactiveBranch = true,
            },
        ];
}
