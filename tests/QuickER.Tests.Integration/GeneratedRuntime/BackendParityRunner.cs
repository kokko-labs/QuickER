using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>一致テストのシナリオ（種まき＋比べる操作の列）</summary>
/// <param name="Name">シナリオの名前（既知の違いの一覧が名指しする）</param>
/// <param name="Seeds">種まき（ほかの書き手が行う。比べない）</param>
/// <param name="Ops">比べる操作の列</param>
/// <param name="Options">実装先の組み立ての条件（既定はフックあり・セーブポイントあり）</param>
/// <param name="CompareWithBaseline">
/// QuickER 版と比べるか。false のシナリオは、実装先ごとの期待（<see cref="ExpectOp"/>）だけで判定する
/// （docs に書いてある違いの上に立つ振る舞い＝ナビゲーションの条件など、基準と比べること自体に意味が無いもの）
/// </param>
public sealed record ParityScenario(
    string Name,
    IReadOnlyList<ParityOp> Seeds,
    IReadOnlyList<ParityOp> Ops,
    ParityBackendOptions? Options = null,
    bool CompareWithBaseline = true
)
{
    /// <summary>全操作がある実装先（QuickER 版を必ず含む＝基準）</summary>
    public IReadOnlyList<ParityBackendKind> Backends =>
        Enum.GetValues<ParityBackendKind>().Where(k => Ops.All(op => op.Supports(k))).ToList();

    /// <summary>
    /// 全実装先で流し、QuickER 版と比べた違いを返す（実装先ごとに空の保存先から始める）。
    /// </summary>
    public async Task<ParityRunResult> RunAsync(
        CancellationToken ct,
        IReadOnlyList<ParityBackendKind>? only = null
    )
    {
        var backends = Backends;

        if (!backends.Contains(ParityBackendKind.Ado))
        {
            throw new InvalidOperationException(
                $"Scenario '{Name}' must run on the QuickER backend, the baseline."
            );
        }

        if (only is not null)
        {
            backends = [.. backends.Where(k => k == ParityBackendKind.Ado || only.Contains(k))];
        }

        var traces = new Dictionary<ParityBackendKind, IReadOnlyList<ParityStep>>();

        foreach (var kind in backends)
        {
            traces[kind] = await RunOnAsync(kind, ct);
        }

        var baseline = traces[ParityBackendKind.Ado];
        var violations = new List<ParityViolation>();

        foreach (var (kind, trace) in traces)
        {
            if (CompareWithBaseline && kind != ParityBackendKind.Ado)
            {
                violations.AddRange(ParityStep.Compare(baseline, trace, kind));
            }

            // 期待は基準も含めて実装先ごとに見る（決定した振る舞いへ、基準の QuickER 版自身が追いついていないことがある）
            violations.AddRange(
                trace
                    .Where(s => s.IsExpectation && s.Outcome != ParityStep.ExpectationMet)
                    .Select(s => new ParityViolation(
                        kind,
                        ParityStep.ExpectAspect,
                        s.Index,
                        s.OpName,
                        ParityStep.ExpectationMet,
                        s.Outcome
                    ))
            );
        }

        return new ParityRunResult(this, traces, violations);
    }

    /// <summary>1 つの実装先で種まきと操作を流し、手ごとの記録を返す</summary>
    private async Task<IReadOnlyList<ParityStep>> RunOnAsync(
        ParityBackendKind kind,
        CancellationToken ct
    )
    {
        await using var backend = await ParityBackend.CreateAsync(kind, Options ?? new(), ct);

        foreach (var seed in Seeds)
        {
            await seed.RunAsync(backend, ct);
        }

        var steps = new List<ParityStep>
        {
            await ParityStep.CaptureAsync(backend, -1, "Seed", "ok", ct),
        };

        for (var i = 0; i < Ops.Count; i++)
        {
            var op = Ops[i];
            backend.Hooks.Clear();
            string outcome;

            try
            {
                outcome = op is ExpectOp expect
                    ? expect.Check(new ParityExpectation(kind, backend, steps[^1]))
                        ?? ParityStep.ExpectationMet
                    : await op.RunAsync(backend, ct);
            }
            catch (Exception ex)
            {
                outcome = ParityFormat.Exception(ex);
            }

            steps.Add(await ParityStep.CaptureAsync(backend, i, op.Name, outcome, ct));
        }

        return steps;
    }
}

/// <summary>
/// 1 手の後の観測：戻り値・保存先の全行・スロットのグラフ・フックの呼び出し。
/// </summary>
/// <remarks>
/// <para>
/// フックは Before と After を別々の列にして比べる。インメモリは After をすべての Before の後にまとめて走らせる
/// （保存の段の後で After の段を流す作り）ので、混ぜた 1 列ではどの保存でも違いになり、ほかの違いが埋もれる。
/// Before の列は、同じ呼び出しの繰り返しを最初の 1 回へ畳む（EF Core は再試行のたびに Before を走らせる＝docs の
/// Save フック節に書いてある違い。After は SaveChanges が成功した試行でだけ走るので畳まない）。
/// </para>
/// </remarks>
public sealed record ParityStep(
    int Index,
    string OpName,
    string Outcome,
    IReadOnlyList<string> State,
    IReadOnlyList<string> Slots,
    IReadOnlyList<string> BeforeHooks,
    IReadOnlyList<string> AfterHooks,
    IReadOnlyList<string> RawHooks
)
{
    /// <summary>期待（<see cref="ExpectOp"/>）を満たしたときの戻り値</summary>
    public const string ExpectationMet = "ok";

    /// <summary>期待を満たさなかったときの違いの観点</summary>
    public const string ExpectAspect = "expect";

    /// <summary>期待の手か（基準との比較では戻り値を比べない）</summary>
    public bool IsExpectation => OpName.StartsWith(ExpectOp.NamePrefix, StringComparison.Ordinal);

    /// <summary>比べる観点（違いの署名の先頭）</summary>
    public static readonly IReadOnlyList<string> Aspects =
    [
        "outcome",
        "state",
        "slots",
        "hooks-before",
        "hooks-after",
    ];

    /// <summary>今の観測を取る</summary>
    public static async Task<ParityStep> CaptureAsync(
        ParityBackend backend,
        int index,
        string opName,
        string outcome,
        CancellationToken ct
    )
    {
        IReadOnlyList<string> state;

        try
        {
            state = await backend.ReadStateAsync(ct);
        }
        catch (Exception ex)
        {
            state = [$"unreadable: {ParityFormat.Exception(ex)}"];
        }

        var slots = backend
            .Slots.OrderBy(s => s.Key, StringComparer.Ordinal)
            .SelectMany(s => ParityFormat.Graph(s.Value).Select(line => $"[{s.Key}] {line}"))
            .ToList();

        List<string> hooks;

        lock (backend.Hooks)
        {
            hooks = [.. backend.Hooks];
        }

        var before = hooks
            .Where(h => h.StartsWith("Before:", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var after = hooks.Where(h => h.StartsWith("After:", StringComparison.Ordinal)).ToList();

        return new ParityStep(index, opName, outcome, state, slots, before, after, hooks);
    }

    /// <summary>観点ごとの値（比べる単位）</summary>
    public string Aspect(string aspect) =>
        aspect switch
        {
            "outcome" => Outcome,
            "state" => string.Join("\n", State),
            "slots" => string.Join("\n", Slots),
            "hooks-before" => string.Join("\n", BeforeHooks),
            "hooks-after" => string.Join("\n", AfterHooks),
            _ => throw new ArgumentOutOfRangeException(nameof(aspect)),
        };

    /// <summary>基準の記録と比べ、手・観点ごとの違いを返す</summary>
    public static IEnumerable<ParityViolation> Compare(
        IReadOnlyList<ParityStep> baseline,
        IReadOnlyList<ParityStep> trace,
        ParityBackendKind backend
    )
    {
        for (var i = 0; i < baseline.Count; i++)
        {
            foreach (var aspect in Aspects)
            {
                if (aspect == "outcome" && baseline[i].IsExpectation)
                {
                    continue;
                }

                // 両方が例外で終わった保存の After は比べない。失敗の前に書いた行の After を走らせるか（QuickER 版・リモート）、
                // 1 回も走らせないか（EF Core は SaveChanges が成功した後にまとめて・インメモリは After の段の前に失敗する）は、
                // docs の Save フック節に書いてある作りの違い
                if (
                    aspect == "hooks-after"
                    && baseline[i].Outcome.StartsWith("throws ", StringComparison.Ordinal)
                    && trace[i].Outcome.StartsWith("throws ", StringComparison.Ordinal)
                )
                {
                    continue;
                }

                var expected = baseline[i].Aspect(aspect);
                var actual = trace[i].Aspect(aspect);

                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                {
                    yield return new ParityViolation(
                        backend,
                        aspect,
                        baseline[i].Index,
                        baseline[i].OpName,
                        expected,
                        actual
                    );
                }
            }
        }
    }
}

/// <summary>基準（QuickER 版）と食い違った 1 手の 1 観点</summary>
public sealed record ParityViolation(
    ParityBackendKind Backend,
    string Aspect,
    int StepIndex,
    string OpName,
    string Expected,
    string Actual
)
{
    /// <summary>違いの署名（観点:操作:実装先）</summary>
    public string Signature => $"{Aspect}:{OpName}:{Backend}";

    /// <summary>失敗の文言に使う詳細</summary>
    public string Describe() =>
        $"step {StepIndex} {OpName} [{Aspect}] {Backend}\n"
        + $"  expected (Ado):\n    {Expected.Replace("\n", "\n    ", StringComparison.Ordinal)}\n"
        + $"  actual:\n    {Actual.Replace("\n", "\n    ", StringComparison.Ordinal)}";
}

/// <summary>シナリオを全実装先で流した結果</summary>
public sealed record ParityRunResult(
    ParityScenario Scenario,
    IReadOnlyDictionary<ParityBackendKind, IReadOnlyList<ParityStep>> Traces,
    IReadOnlyList<ParityViolation> Violations
)
{
    /// <summary>実装先と観点の組（既知の違いの一覧が名指しする単位）</summary>
    public IReadOnlySet<(ParityBackendKind Backend, string Aspect)> Groups =>
        Violations.Select(v => (v.Backend, v.Aspect)).ToHashSet();

    /// <summary>違いを読める形で並べる（実装先と観点ごとに最初の 1 手だけ）</summary>
    public string Describe()
    {
        var builder = new StringBuilder($"scenario '{Scenario.Name}':\n");

        foreach (var first in Violations.GroupBy(v => (v.Backend, v.Aspect)).Select(g => g.First()))
        {
            builder.AppendLine(first.Describe());
        }

        return builder.ToString();
    }
}
