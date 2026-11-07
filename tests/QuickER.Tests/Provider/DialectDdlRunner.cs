using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Provider;

/// <summary>1 ケースの出力（面ごとの行）</summary>
/// <param name="Lines">構造の行（<see cref="DialectDdlReader.Read"/>）</param>
/// <param name="Text">素の行</param>
/// <param name="Warnings">計画の警告（<c>{種類} {表} {詳細}</c>）</param>
/// <param name="Exception">例外（<c>{型名}: {メッセージ}</c> の 1 行。投げなければ空）</param>
/// <param name="Raw">出力の全文（例外なら空）</param>
internal sealed record DdlOutcome(
    IReadOnlyList<string> Lines,
    IReadOnlyList<string> Text,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Exception,
    string Raw
)
{
    /// <summary>面の行を引く</summary>
    /// <param name="output">面（<see cref="DialectDdlOutputs"/>）</param>
    public IReadOnlyList<string> Of(string output) =>
        output switch
        {
            DialectDdlOutputs.Lines => Lines,
            DialectDdlOutputs.Text => Text,
            DialectDdlOutputs.Warnings => Warnings,
            DialectDdlOutputs.Exception => Exception,
            _ => throw new InvalidOperationException($"知らない面 {output}"),
        };
}

/// <summary>
/// DDL の表の実行部：行を 1 方言で流して面ごとの行へ写し、期待と照合する
/// </summary>
/// <remarks>
/// <para>
/// 期待の照合に加えて、より強い自動の照合を全行に掛ける：
/// (1) 書式のカルチャ（<see cref="CultureScope.Cultures"/>）で作り直した出力がインバリアントと同じ（生成日時の数字は伏せる）で、
/// 生成日時の年が西暦（現在の年 ±1）。
/// (2) 入力（図・項目）の名前・型・説明に CJK が無ければ、出力に CJK が無い（固定文は英語）。
/// (3) 扱いの行は、方言ごとに手で書いた扱いが扱いの表（<see cref="UnsupportedTreatments"/>）と一致する。
/// </para>
/// </remarks>
internal static class DialectDdlRunner
{
    private static readonly Regex GeneratedAt = new(
        "^-- Generated at: .*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant
    );

    private static readonly Regex GeneratedAtYear = new(
        "^-- Generated at: (?<year>[0-9]{4})-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2}:[0-9]{2}$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant
    );

    /// <summary>行を 1 方言で流して照合し、問題を返す（無ければ空）</summary>
    /// <param name="row">行</param>
    /// <param name="dialect">方言</param>
    /// <param name="autoCheckRan">
    /// 自動の照合が実際に走ったとき、その観点（<see cref="DialectDdlFacets.Culture"/>・<see cref="DialectDdlFacets.English"/>）を受け取る
    /// （網羅の計数が、照合の経路で観点を数えるため。英語の照合は入力に CJK が無いときだけ走る）
    /// </param>
    public static IReadOnlyList<string> Check(
        DialectDdlCase row,
        string dialect,
        Action<string>? autoCheckRan = null
    )
    {
        var problems = new List<string>();
        var outcome = Run(row, dialect);

        foreach (var expectation in row.Expected[dialect])
        {
            if (Match(expectation, outcome.Of(expectation.Output)) is { } problem)
            {
                problems.Add(problem);
            }
        }

        problems.AddRange(CultureProblems(row, dialect, autoCheckRan));
        problems.AddRange(EnglishProblems(row, dialect, outcome, autoCheckRan));
        problems.AddRange(TreatmentProblems(row, dialect));

        if (problems.Count > 0)
        {
            problems.Add("出力の構造の行：\n    " + string.Join("\n    ", outcome.Lines));

            if (outcome.Exception.Count > 0)
            {
                problems.Add("例外：" + outcome.Exception[0]);
            }

            if (outcome.Warnings.Count > 0)
            {
                problems.Add("警告：\n    " + string.Join("\n    ", outcome.Warnings));
            }
        }

        return problems;
    }

    /// <summary>行を 1 方言で流して面ごとの行へ写す</summary>
    /// <param name="row">行</param>
    /// <param name="dialect">方言</param>
    public static DdlOutcome Run(DialectDdlCase row, string dialect)
    {
        var adapter = DialectDdlAxis.Get(dialect);
        var input = row.Build(dialect);
        IReadOnlyList<string> warnings = [];
        string raw;

        // 製品が投げた例外は面（exception）にする（期待していない例外は期待の食い違いで赤になる）
        try
        {
            raw = Produce(input, adapter, out warnings);
        }
        catch (Exception ex) when (ex is not DdlRowDeclarationException)
        {
            return new DdlOutcome([], [], warnings, [Describe(ex)], string.Empty);
        }

        return new DdlOutcome(
            DialectDdlReader.Read(raw, adapter),
            DialectDdlReader.Text(raw),
            warnings,
            [],
            raw
        );
    }

    /// <summary>入力から出力を作る（経路は入力の型で決まる）</summary>
    private static string Produce(
        object input,
        DialectDdlAdapter adapter,
        out IReadOnlyList<string> warnings
    )
    {
        var provider = adapter.Provider;
        warnings = [];

        switch (input)
        {
            case ErDiagram diagram:
                return provider.DdlGenerator.Build(diagram);

            case DdlSyncItems items:
            {
                var plan = new SyncPlanner().BuildPlan(
                    items.Items,
                    items.Capabilities ?? provider.SyncCapabilities,
                    items.Context
                );
                warnings = Warnings(plan);
                return provider.SyncScriptBuilder.Build(plan);
            }

            case DdlSyncDiff diff:
            {
                var capabilities = provider.SyncCapabilities;
                var computed = new SchemaDiffService().Compute(
                    diff.LiveEntities,
                    diff.LiveRelationships,
                    diff.TargetEntities,
                    diff.TargetRelationships,
                    capabilities
                );

                if (diff.Select is { } select)
                {
                    select(computed.Items);
                }
                else
                {
                    foreach (var item in computed.Items)
                    {
                        item.IsSelected = item.IsSelectable;
                    }
                }

                var plan = new SyncPlanner().BuildPlan(
                    computed.Items.Where(item => item.IsSelected),
                    capabilities,
                    new SyncPlanContext
                    {
                        LiveEntities = diff.LiveEntities,
                        LiveRelationships = diff.LiveRelationships,
                    }
                );
                warnings = Warnings(plan);
                return provider.SyncScriptBuilder.Build(plan);
            }

            case SyncPlan plan:
                warnings = Warnings(plan);
                return provider.SyncScriptBuilder.Build(plan);

            default:
                throw new DdlRowDeclarationException(
                    $"知らない入力の型 {input?.GetType().Name ?? "null"}"
                );
        }
    }

    private static IReadOnlyList<string> Warnings(SyncPlan plan) =>
        plan.Warnings.Select(w => $"{w.Kind} {w.TableName} {w.Detail}".TrimEnd()).ToList();

    private static string Describe(Exception ex) =>
        $"{ex.GetType().Name}: {ex.Message.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}";

    /// <summary>期待 1 つを照合する（合えば null）</summary>
    private static string? Match(DdlExpectation expectation, IReadOnlyList<string> lines)
    {
        var patterns = expectation.Patterns;

        var ok = expectation.Match switch
        {
            DdlMatch.Contains => patterns.All(p => lines.Any(line => Like(p, line))),
            DdlMatch.Absent => !lines.Any(line => patterns.Any(p => Like(p, line))),
            DdlMatch.Count => lines.Count(line => Like(patterns[0], line)) == expectation.Count,
            DdlMatch.Exact => lines.Count == patterns.Count
                && lines.Zip(patterns).All(pair => Like(pair.Second, pair.First)),
            DdlMatch.Sequence => Enumerable
                .Range(0, Math.Max(0, lines.Count - patterns.Count + 1))
                .Any(start => patterns.Select((p, i) => Like(p, lines[start + i])).All(x => x)),
            DdlMatch.Order => InOrder(patterns, lines),
            _ => throw new DdlRowDeclarationException($"知らない強さ {expectation.Match}"),
        };

        return ok ? null : $"期待 {expectation} に合わない";
    }

    private static bool InOrder(IReadOnlyList<string> patterns, IReadOnlyList<string> lines)
    {
        var index = 0;

        foreach (var pattern in patterns)
        {
            while (index < lines.Count && !Like(pattern, lines[index]))
            {
                index++;
            }

            if (index == lines.Count)
            {
                return false;
            }

            index++;
        }

        return true;
    }

    /// <summary><c>*</c> を任意の文字列とするパターンの一致（ほかの文字はそのまま・序数）</summary>
    public static bool Like(string pattern, string line)
    {
        var parts = pattern.Split('*');

        if (parts.Length == 1)
        {
            return string.Equals(pattern, line, StringComparison.Ordinal);
        }

        if (
            !line.StartsWith(parts[0], StringComparison.Ordinal)
            || !line.EndsWith(parts[^1], StringComparison.Ordinal)
            || line.Length < parts[0].Length + parts[^1].Length
        )
        {
            return false;
        }

        var position = parts[0].Length;
        var end = line.Length - parts[^1].Length;

        for (var i = 1; i < parts.Length - 1; i++)
        {
            var found = line.IndexOf(parts[i], position, StringComparison.Ordinal);

            if (found < 0 || found + parts[i].Length > end)
            {
                return false;
            }

            position = found + parts[i].Length;
        }

        return true;
    }

    /// <summary>自動の照合 (1)：書式のカルチャに依らない・生成日時は西暦</summary>
    private static IEnumerable<string> CultureProblems(
        DialectDdlCase row,
        string dialect,
        Action<string>? autoCheckRan
    )
    {
        var adapter = DialectDdlAxis.Get(dialect);
        var problems = new List<string>();
        var year = DateTime.Now.Year;

        string Produce()
        {
            try
            {
                var input = row.Build(dialect);
                var raw = NormalizeLineEndings(
                    DialectDdlRunner.Produce(input, adapter, out var warnings)
                );
                var match = GeneratedAtYear.Match(raw);

                // DDL は必ず生成日時の行を持つ（行が無いと暦の照合が黙って通る）
                if (input is ErDiagram && !GeneratedAt.IsMatch(raw))
                {
                    problems.Add(
                        $"[自動・カルチャ] {CultureInfo.CurrentCulture.Name} の DDL に生成日時の行が無い"
                    );
                }

                if (GeneratedAt.IsMatch(raw))
                {
                    var produced = match.Success
                        ? int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture)
                        : -1;

                    if (Math.Abs(produced - year) > 1)
                    {
                        problems.Add(
                            $"[自動・カルチャ] {CultureInfo.CurrentCulture.Name} の生成日時が西暦の yyyy-MM-dd HH:mm:ss でない：{GeneratedAt.Match(raw).Value}"
                        );
                    }
                }

                return GeneratedAt.Replace(raw, "-- Generated at: (masked)")
                    + "\n"
                    + string.Join("\n", warnings);
            }
            catch (Exception ex) when (ex is not DdlRowDeclarationException)
            {
                return Describe(ex);
            }
        }

        foreach (var difference in CultureScope.FindDifferences(Produce))
        {
            problems.Add($"[自動・カルチャ] {difference}");
        }

        autoCheckRan?.Invoke(DialectDdlFacets.Culture);

        return problems.Distinct();
    }

    /// <summary>改行を LF にそろえる（生成日時の行の照合は行末で区切る）</summary>
    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>自動の照合 (2)：入力に CJK が無ければ出力に CJK が無い</summary>
    private static IEnumerable<string> EnglishProblems(
        DialectDdlCase row,
        string dialect,
        DdlOutcome outcome,
        Action<string>? autoCheckRan
    )
    {
        if (InputStrings(row.Build(dialect)).Any(text => CjkText.Pattern.IsMatch(text)))
        {
            return [];
        }

        autoCheckRan?.Invoke(DialectDdlFacets.English);

        return outcome
            .Text.Concat(outcome.Exception)
            .Where(line => CjkText.Pattern.IsMatch(line))
            .Select(line => $"[自動・英語] 入力に CJK が無いのに出力の行に CJK がある：{line}");
    }

    /// <summary>自動の照合 (3)：手で書いた扱いが扱いの表と一致する</summary>
    private static IEnumerable<string> TreatmentProblems(DialectDdlCase row, string dialect)
    {
        if (row.Treatment is not { } cell || !cell.Treatments.TryGetValue(dialect, out var written))
        {
            yield break;
        }

        var declared = UnsupportedTreatments.For(cell.Kind, cell.Feature, dialect);

        if (declared != written)
        {
            yield return $"[自動・扱いの表] {cell.Kind} × {cell.Feature} × {dialect} の扱いは表では {declared}・行では {written}";
        }
    }

    /// <summary>入力の文字列（名前・型・説明など）をすべて集める（公開の文字列プロパティを再帰的にたどる）</summary>
    public static IEnumerable<string> InputStrings(object input)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var found = new List<string>();
        Walk(input, seen, found, depth: 0);
        return found;
    }

    private static void Walk(object? value, HashSet<object> seen, List<string> found, int depth)
    {
        if (value is null || depth > 12)
        {
            return;
        }

        if (value is string text)
        {
            found.Add(text);
            return;
        }

        var type = value.GetType();

        if (
            type.IsPrimitive
            || type.IsEnum
            || value is Guid
            || value is Delegate
            || !seen.Add(value)
        )
        {
            return;
        }

        if (value is IEnumerable sequence)
        {
            foreach (var item in sequence)
            {
                Walk(item, seen, found, depth + 1);
            }

            return;
        }

        if (
            type.Namespace is null
            || !type.Namespace.StartsWith("QuickER", StringComparison.Ordinal)
        )
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            Walk(property.GetValue(value), seen, found, depth + 1);
        }
    }
}

/// <summary>行の宣言の誤り（期待・入力の書き間違い。製品の例外と区別して必ずテストを赤にする）</summary>
internal sealed class DdlRowDeclarationException(string message) : Exception(message);
