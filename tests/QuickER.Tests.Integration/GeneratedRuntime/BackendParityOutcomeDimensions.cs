using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.GeneratedBackendParityFixture;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 実装先どうしの一致の乱数の操作の列で、Save フックが受けた呼び出し（段・操作・当たった規則の振る舞い）の次元表
/// </summary>
/// <remarks>
/// <para>
/// ケースは、基準の QuickER 版で Save フックが受けた呼び出し 1 件（<see cref="ParityStep.RawHooks"/> の <c>Before:</c> と
/// <c>After:</c> の行）。生成器の次元表（<see cref="BackendParityDimensions"/>）と違い、流した後の結果を数える（操作の列の乱数を
/// 引かないので、種の指紋に関係しない）。操作（<see cref="SaveOperation"/> の 3 値）ごとに、フックの規則の振る舞いとの組を
/// 生成器が作っていることを、既定の種の数で確かめる。
/// </para>
/// <para>
/// 網羅しない組の照合は、既定の種の数で、すべての種を流し終えたときだけ行う（範囲 <see cref="BackendParityDimensions.RandomScope"/>）。
/// </para>
/// </remarks>
internal static class BackendParityOutcomeDimensions
{
    /// <summary>フックの段（Before / After）</summary>
    public const string Phase = "Phase";

    /// <summary>フックが受けた操作</summary>
    public const string Operation = "Operation";

    /// <summary>その呼び出しに当たった規則の振る舞い（当たらなければ Record）</summary>
    public const string Behavior = "Behavior";

    /// <summary>After が走らない振る舞い（Before が飛ばした行・Before で投げた行）</summary>
    private static readonly string[] NoAfterBehaviors =
    [
        nameof(ParityHookBehavior.Skip),
        nameof(ParityHookBehavior.EditThenSkip),
        nameof(ParityHookBehavior.ThrowBefore),
    ];

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.GeneratedRuntime,
            [
                new Factor(Phase, [new Level("Before"), new Level("After")], "Save フックの段"),
                new Factor(
                    Operation,
                    Enum.GetValues<SaveOperation>()
                        .Select(value => new Level(value.ToString(), AxisMember.Of(value)))
                        .ToList(),
                    "Save フックが受けた操作（SaveOperation）"
                ),
                new Factor(
                    Behavior,
                    Enum.GetValues<ParityHookBehavior>()
                        .Select(value => new Level(value.ToString()))
                        .ToList(),
                    "その呼び出しに当たった Save フックの規則の振る舞い（規則の無い呼び出しは Record）"
                ),
            ],
            [],
            IsValid
        );

    /// <summary>網羅しない組の一覧（範囲は <see cref="BackendParityDimensions.RandomScope"/> だけ）</summary>
    public static CoverageGapSet Gaps { get; } = CoverageGap.Set(EngineIds.GeneratedRuntime, []);

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.GeneratedRuntime,
            "ParityOutcomes",
            "実装先どうしの一致（乱数の操作の列で Save フックが受けた段・操作・規則の振る舞い）",
            Table,
            0,
            [
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityOutcomeDimensions.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityBackend.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityRuntimeTests.cs",
            ],
            ["src/QuickER.Runtime", "src/QuickER.Runtime.Sqlite"]
        );

    /// <summary>1 つの種の、基準の QuickER 版の記録で Save フックが受けた呼び出しを数える</summary>
    /// <param name="counter">数える先</param>
    /// <param name="scenario">流したシナリオ（Save フックの規則を読む）</param>
    /// <param name="baseline">基準の記録</param>
    public static void Observe(
        CoverageCounter counter,
        ParityScenario scenario,
        IReadOnlyList<ParityStep> baseline
    )
    {
        var rules = scenario.Options?.HookRules ?? [];

        foreach (var hook in baseline.SelectMany(step => step.RawHooks))
        {
            var phase =
                hook.StartsWith("Before:", StringComparison.Ordinal) ? "Before"
                : hook.StartsWith("After:", StringComparison.Ordinal) ? "After"
                : null;

            if (phase is null)
            {
                continue;
            }

            var call = hook[(phase.Length + 1)..];
            counter.Observe(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Phase] = phase,
                    [Operation] = call[..call.IndexOf(':', StringComparison.Ordinal)],
                    [Behavior] = (
                        rules.FirstOrDefault(rule => rule.Matches(call))?.Behavior
                        ?? ParityHookBehavior.Record
                    ).ToString(),
                }
            );
        }
    }

    /// <summary>原理的に起こり得ない組み合わせのときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        assignment.TryGetValue(Phase, out var phase);
        assignment.TryGetValue(Behavior, out var behavior);

        // Before が飛ばした行・Before で投げた行には After が走らない
        return !(phase == "After" && behavior is not null && NoAfterBehaviors.Contains(behavior));
    }
}
