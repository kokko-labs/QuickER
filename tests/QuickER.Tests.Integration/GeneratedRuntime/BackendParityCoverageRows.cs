using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.GeneratedBackendParityFixture;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>網羅の行の入力（シナリオの作り方と、行が出すはずの組）</summary>
/// <param name="Create">シナリオを作る（名前の末尾は操作の範囲＝<see cref="BackendParityDimensions.Observe"/> が範囲を名前で読む）</param>
/// <param name="ExpectedTuples">
/// 行が出すはずの組（因子 → 水準。どれか 1 手の割り当てがその組をすべて含む）。出さなくなったら
/// <see cref="BackendParityCoverageRows.MissingTupleProblems"/> が行を名指しして赤にする
/// </param>
internal sealed record BackendParityCoverageInput(
    Func<ParityScenario> Create,
    IReadOnlyList<IReadOnlyDictionary<string, string>> ExpectedTuples
);

/// <summary>
/// 実装先どうしの一致の網羅の行（網羅しない組の一覧の Pending を埋めるために、乱数の操作の列のテストが種の前に必ず流す行）
/// </summary>
/// <remarks>
/// <para>
/// <b>決まった形のシナリオ（<see cref="BackendParityScenarios"/>）との違い</b>：シナリオは指摘ごとの回帰の網で、別の Theory で
/// 流れて網羅の計数に届かない。網羅の行は、乱数の列の次元表（<see cref="BackendParityDimensions"/>）と結果の表
/// （<see cref="BackendParityOutcomeDimensions"/>）で数え、既定の種で欠ける組を実際に流す。違反は既知の失敗の一覧で外さず、
/// どれも新しい失敗として赤にする（受け入れた手の差の打ち切りは乱数の列と同じに効く）。Id は <see cref="IdPrefix"/> で始める。
/// </para>
/// <para>
/// 行は 2 つの形。(1) <b>種の行</b>：夜間の範囲の種のシナリオをそのまま流す（生成器を変えずに組を作る）。種は生成器を種 1〜2000 で
/// 走らせ、欠ける組を作る最小の種を選んだ（2026-11-02）。(2) <b>種の行＋引数の上書き</b>：種のシナリオの操作の引数だけを
/// 書き換えた行（生成器が作らない組）。生成器を変えて種の指紋が変わったら、指紋と一緒に種を選び直す（行が出すはずの組を
/// 出さなくなると <see cref="MissingTupleProblems"/> が名指しして赤にする）。
/// </para>
/// </remarks>
internal static class BackendParityCoverageRows
{
    /// <summary>網羅の行の Id の接頭辞</summary>
    public const string IdPrefix = "coverage-";

    /// <summary>行の表</summary>
    public static ScenarioTable<BackendParityCoverageInput> Table { get; } =
        new(
            EngineIds.GeneratedRuntime,
            [
                // ---- 普段の 200 種で、列の末尾の手（全体の約 3 分の 2 の種に 1 つ）と範囲の組み合わせが足りない組 ----
                SeedRow(
                    210,
                    "参照されている代替キーの更新（ReferencedKeyUpdate）を、リモートを含む全実装先の範囲（Common）で流す",
                    (BackendParityDimensions.Mode, nameof(ParityGeneratorMode.Common)),
                    (BackendParityDimensions.Deviation, nameof(ParityDeviation.ReferencedKeyUpdate))
                ),
                SeedRow(
                    218,
                    "一意制約を参照する連絡先の外部キー（CustomerCode）を、除外 blob の Stream アクセサの範囲（Blob）で流す",
                    (BackendParityDimensions.Mode, nameof(ParityGeneratorMode.Blob)),
                    (
                        BackendParityDimensions.ContactRelation,
                        nameof(ParityContactRelation.CustomerCode)
                    )
                ),
                SeedRow(
                    286,
                    "Before で投げる Save フックの規則（ThrowBefore）を、全機能面の範囲（Full）で流す",
                    (BackendParityDimensions.Mode, nameof(ParityGeneratorMode.Full)),
                    (BackendParityDimensions.HookRule, nameof(ParityHookBehavior.ThrowBefore))
                ),
                // ---- 生成器が作らない組：生成器の SaveMany は保存の引数を渡さない（既定の Optimistic）----
                new(
                    IdPrefix + "seed30-save-many-force-overwrite",
                    null,
                    "種 30 の列の複数形のグラフ保存（SaveManyOrders）に ForceOverwrite を渡す。BackendParityFixture の表は行バージョンを"
                        + "持たないので ForceOverwrite の効果は状態に出ず、この行が確かめるのは、SaveMany に ForceOverwrite を渡しても"
                        + " 5 つの実装先が同じに受け付ける（リモートはモードを転送し、インメモリは検証する）こと。版の後勝ちの意味は"
                        + " ConcurrencyFixture の系統（Concurrency*RuntimeTests）が見ている",
                    new BackendParityCoverageInput(
                        () =>
                        {
                            var scenario = BackendParityGenerator.Create(30);

                            return scenario with
                            {
                                Name = $"{IdPrefix}seed30-force-overwrite-{ModeOf(scenario)}",
                                Ops =
                                [
                                    .. scenario.Ops.Select(op =>
                                        op is SaveManyOrdersOp many
                                            ? many with
                                            {
                                                Options = (many.Options ?? new()) with
                                                {
                                                    Mode = ConcurrencyMode.ForceOverwrite,
                                                },
                                            }
                                            : op
                                    ),
                                ],
                            };
                        },
                        [
                            Tuple(
                                (BackendParityDimensions.OpKind, "SaveManyOrders"),
                                (
                                    BackendParityDimensions.Concurrency,
                                    nameof(ConcurrencyMode.ForceOverwrite)
                                )
                            ),
                        ]
                    )
                ),
            ]
        );

    /// <summary>行が出すはずの組を出さなかった問題（行を名指しする）</summary>
    /// <param name="row">行</param>
    /// <param name="scenario">行が作ったシナリオ</param>
    public static IEnumerable<string> MissingTupleProblems(
        ScenarioRow<BackendParityCoverageInput> row,
        ParityScenario scenario
    )
    {
        var assignments = BackendParityDimensions.Assignments(scenario).ToList();

        foreach (var expected in row.Input.ExpectedTuples)
        {
            if (
                !assignments.Any(assignment =>
                    expected.All(pair =>
                        assignment.TryGetValue(pair.Key, out var level) && level == pair.Value
                    )
                )
            )
            {
                yield return $"coverage row {row.Id}: expected a step with "
                    + string.Join(", ", expected.Select(pair => $"{pair.Key}={pair.Value}"))
                    + " (the generator changed the seed's shape: pick the seed rows again with the fingerprints)";
            }
        }
    }

    /// <summary>種の行（種 <paramref name="seed"/> のシナリオをそのまま流す）</summary>
    private static ScenarioRow<BackendParityCoverageInput> SeedRow(
        int seed,
        string description,
        params (string Factor, string Level)[] expected
    ) =>
        new(
            $"{IdPrefix}seed{seed}",
            null,
            $"種 {seed}（夜間の範囲）：{description}",
            new BackendParityCoverageInput(
                () =>
                {
                    var scenario = BackendParityGenerator.Create(seed);

                    return scenario with
                    {
                        Name = $"{IdPrefix}seed{seed}-{ModeOf(scenario)}",
                    };
                },
                [Tuple(expected)]
            )
        );

    /// <summary>シナリオの操作の範囲（名前の末尾）</summary>
    private static string ModeOf(ParityScenario scenario) => scenario.Name.Split('-')[^1];

    private static IReadOnlyDictionary<string, string> Tuple(
        params (string Factor, string Level)[] pairs
    ) => pairs.ToDictionary(pair => pair.Factor, pair => pair.Level, StringComparer.Ordinal);
}
