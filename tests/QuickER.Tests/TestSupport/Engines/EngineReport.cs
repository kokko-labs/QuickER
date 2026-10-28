using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Xunit;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>
/// エンジンの失敗を、範囲・種・署名・再現の手順つきでまとめる報告
/// </summary>
/// <remarks>
/// 先頭の <c>maxDetailed</c> 件（既定 8）を詳しく書き、残りは範囲ごとの件数だけを書く（1 回の実行で数百件出ても読める長さに保つ）。
/// 失敗には再現の手順を必ず添える（種なら <see cref="SeedPlan.ReproductionFor"/>、シナリオなら <see cref="ScenarioReproduction"/>）。
/// </remarks>
public sealed class EngineReport
{
    /// <summary>詳しく書く件数の既定</summary>
    public const int DefaultMaxDetailed = 8;

    private readonly List<EngineFailure> _failures = [];
    private readonly Dictionary<EngineFailure, string> _reproductions = new(
        ReferenceEqualityComparer.Instance
    );

    /// <summary>報告を作る</summary>
    /// <param name="engineId">エンジンの ID</param>
    /// <param name="maxDetailed">詳しく書く件数（1 以上）</param>
    public EngineReport(string engineId, int maxDetailed = DefaultMaxDetailed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineId);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxDetailed, 0);
        EngineId = engineId;
        MaxDetailed = maxDetailed;
    }

    /// <summary>エンジンの ID</summary>
    public string EngineId { get; }

    /// <summary>詳しく書く件数</summary>
    public int MaxDetailed { get; }

    /// <summary>集めた失敗（追加した順）</summary>
    public IReadOnlyList<EngineFailure> Failures => _failures;

    /// <summary>シナリオを再現する手順</summary>
    /// <param name="id">シナリオの Id</param>
    public static string ScenarioReproduction(string id) => $"シナリオ `{id}` を流す";

    /// <summary>失敗を再現の手順つきで加える</summary>
    /// <param name="failure">失敗</param>
    /// <param name="reproduction">再現の手順（空は断る）</param>
    public void Add(EngineFailure failure, string reproduction)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentException.ThrowIfNullOrWhiteSpace(reproduction);
        _failures.Add(failure);
        _reproductions[failure] = reproduction;
    }

    /// <summary>失敗を打ち切りつきで文章にする</summary>
    /// <param name="subset">書く失敗（null なら集めた全部。この報告に加えていない失敗は再現の手順なしで書く）</param>
    public string Render(IEnumerable<EngineFailure>? subset = null)
    {
        var failures = (subset ?? _failures).ToList();
        var text = new StringBuilder();

        foreach (var failure in failures.Take(MaxDetailed))
        {
            AppendFailure(text, failure);

            if (_reproductions.TryGetValue(failure, out var reproduction))
            {
                text.Append("\n    再現: ").Append(reproduction);
            }

            text.Append('\n');
        }

        var rest = failures.Skip(MaxDetailed).ToList();

        if (rest.Count > 0)
        {
            var byScope = rest.GroupBy(f => f.Scope, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g =>
                    string.Create(CultureInfo.InvariantCulture, $"{g.Key} {g.Count()} 件")
                );
            text.Append(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"  ほか {rest.Count} 件（{string.Join("・", byScope)}）\n"
                )
            );
        }

        return text.ToString().TrimEnd('\n');
    }

    /// <summary>再現の手順を持たない失敗を、打ち切りなしで並べる（報告を渡されなかったときの既定）</summary>
    /// <param name="failures">失敗</param>
    public static string RenderPlain(IEnumerable<EngineFailure> failures)
    {
        var text = new StringBuilder();

        foreach (var failure in failures)
        {
            AppendFailure(text, failure);
            text.Append('\n');
        }

        return text.ToString().TrimEnd('\n');
    }

    /// <summary>件数と既知の失敗の照合の結果をテストの出力へ書く</summary>
    /// <param name="output">出力先（null なら現在のテストの出力）</param>
    /// <param name="known">既知の失敗の照合の結果（無ければ書かない）</param>
    public void WriteSummary(ITestOutputHelper? output = null, KnownReport? known = null)
    {
        output ??= TestContext.Current.TestOutputHelper;

        if (output is null)
        {
            return;
        }

        output.WriteLine(
            string.Create(CultureInfo.InvariantCulture, $"[{EngineId}] 失敗 {_failures.Count} 件")
        );

        if (known is not null)
        {
            output.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"[{EngineId}] 既知 {known.Matched.Count} 件・新しい失敗 {known.Unknown.Count} 件・起きなかった項目 {known.NotObserved.Count} 件・理由の食い違い {known.ReasonMismatch.Count} 件"
                )
            );

            foreach (var match in known.Matched.Take(MaxDetailed))
            {
                output.WriteLine($"  [known] {match.Entry.Finding}: {Head(match.Failure)}");
            }
        }

        if (_failures.Count > 0)
        {
            output.WriteLine(Render());
        }
    }

    private static string Head(EngineFailure failure) =>
        failure.Seed is { } seed
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{failure.Scope} / 種 {seed} | {failure.Signature}"
            )
            : $"{failure.Scope} | {failure.Signature}";

    private static void AppendFailure(StringBuilder text, EngineFailure failure)
    {
        text.Append("  - ").Append(Head(failure));

        if (failure.Detail.Length > 0)
        {
            text.Append("\n    ")
                .Append(failure.Detail.Replace("\n", "\n    ", StringComparison.Ordinal));
        }
    }
}
