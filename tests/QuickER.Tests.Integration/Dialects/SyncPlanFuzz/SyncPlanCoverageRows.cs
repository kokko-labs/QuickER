using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>網羅の行の入力（流す方言・ケースの作り方・強制する選び方・行が出すはずの変更の種類・流す先）</summary>
/// <param name="Dialect">流す方言（<see cref="DialectNames"/> の値）</param>
/// <param name="Create">方言の部品からケースを作る（選び方は <see cref="Selection"/> で上書きする）</param>
/// <param name="Selection">強制する差分の選び方</param>
/// <param name="ExpectedKinds">
/// 行が出すはずの変更の種類（<see cref="SyncPlanFuzzDimensions.KindOf"/>）。出さなくなったら行の問題として名指しして赤にする
/// </param>
/// <param name="RealDatabase">実 DB の総当たりで流すか</param>
/// <param name="Net">能力の組の網で流すか</param>
/// <param name="SelectionSeed">強制する選び方が引く乱数の種（1 項目・種類の集まり・項目ごとの半々。種の行は種の番号）</param>
internal sealed record SyncPlanCoverageInput(
    string Dialect,
    Func<SyncPlanFuzzDialect, ISyncPlanFuzzCase> Create,
    FuzzSelection Selection,
    IReadOnlyList<string> ExpectedKinds,
    bool RealDatabase,
    bool Net,
    int SelectionSeed = 0
);

/// <summary>
/// 同期計画の総当たりと能力の組の網の網羅の行（網羅しない組の一覧の Pending を埋めるために、種の前に必ず流す行）
/// </summary>
/// <remarks>
/// <para>
/// <b>決定的な行（<see cref="SyncPlanScenarios"/>）との違い</b>：決定的な行は指摘の形を最小にした回帰の網で、選び方を行の述語で
/// 決める（報告の表記は All か ByKinds の名ばかり）ので、網羅の計数に数えない。網羅の行は、変更の種類と差分の選び方の組を
/// 実際に流すための行で、選び方を本物の 5 通りの意味（<see cref="SyncPlanFuzzGenerator.ApplySelection"/>）のまま強制し、
/// 網羅の計数に数える（数えないと、乱数の種で欠ける組を埋められない）。違反は決定的な行と同じ範囲（方言名＋
/// <see cref="SyncPlanScenarios.ScopeSuffix"/>）で既知の失敗の照合へ渡す。Id は <see cref="IdPrefix"/> で始める。
/// </para>
/// <para>
/// 行は 2 つの形。(1) <b>種の行</b>：実 DB の種 N の DB と変更をそのまま使い、選び方だけを強制する（選び方は 1 ケースの最後に
/// 引く乱数なので、DB と変更は種 N のときと同じになる。実 DB の種なので実 DB でだけ流す）。種 N は、実 DB の総当たりを
/// 夜間の種の数で <c>QUICKER_SYNC_FUZZ_LOG</c> 付きで流した記録から、欠けている組を多く埋める種を貪欲に選んだ
/// （<see cref="CoveringArray"/> は使わない＝自由な変数が種で、次元表の因子でないため）。生成器を変えて種の指紋が変わったら、
/// 種の行も選び直す（行が出すはずの変更の種類を出さなくなると、<see cref="MissingKindProblems"/> が名指しして赤にする）。
/// (2) <b>手で書いた行</b>：生成器が作らない組（SQL Server の参照アクションの変更）を、DB が断らない形で書いた行。
/// </para>
/// </remarks>
internal static class SyncPlanCoverageRows
{
    /// <summary>網羅の行の Id の接頭辞</summary>
    public const string IdPrefix = "coverage-";

    /// <summary>網羅の行か（Id の接頭辞で決める）</summary>
    /// <param name="scenarioId">行の Id</param>
    public static bool IsCoverageRow(string? scenarioId) =>
        scenarioId?.StartsWith(IdPrefix, StringComparison.Ordinal) == true;

    /// <summary>行の表</summary>
    public static ScenarioTable<SyncPlanCoverageInput> Table { get; } =
        new(
            EngineIds.SyncPlan,
            [
                .. SqlServerReferentialActionRows(),
                // ---- SQL Server の変更の種類と選び方の組（SyncPlanFuzzDimensions の SamplingGaps に書いていた 52 組）。種は実 DB の
                // 総当たりを夜間の 1000 種で流した記録から貪欲に選んだ（2026-11-02・行の並びは埋める組の多い順）。行の変更の種類が
                // 埋める組で、選び方との組になる ----
                SeedRow(
                    DialectNames.SqlServer,
                    314,
                    FuzzSelection.Random,
                    [
                        "alter type duplicated fk",
                        "alter type duplicated unique",
                        "drop one of duplicated fks",
                        "drop one of duplicated uniques",
                        "retarget fk",
                    ]
                ),
                SeedRow(
                    DialectNames.SqlServer,
                    316,
                    FuzzSelection.Default,
                    [
                        "alter type duplicated unique",
                        "computed type",
                        "drop one of duplicated uniques",
                        "respell type",
                    ]
                ),
                SeedRow(
                    DialectNames.SqlServer,
                    473,
                    FuzzSelection.Random,
                    ["drop fk", "extend pk", "reorder columns", "shrink pk"]
                ),
                SeedRow(
                    DialectNames.SqlServer,
                    3,
                    FuzzSelection.Single,
                    ["drop fk", "extend pk", "rename column case"]
                ),
                SeedRow(
                    DialectNames.SqlServer,
                    85,
                    FuzzSelection.All,
                    ["alter type duplicated unique", "drop one of duplicated uniques", "move pk"]
                ),
                SeedRow(
                    DialectNames.SqlServer,
                    85,
                    FuzzSelection.ByKinds,
                    ["add table", "alter type duplicated unique", "drop one of duplicated uniques"]
                ),
                SeedRow(
                    DialectNames.SqlServer,
                    574,
                    FuzzSelection.Default,
                    ["extend pk", "replace pk", "retarget fk"]
                ),
                SeedRow(
                    DialectNames.SqlServer,
                    101,
                    FuzzSelection.Single,
                    ["add unique", "retarget fk"]
                ),
                SeedRow(
                    DialectNames.SqlServer,
                    120,
                    FuzzSelection.All,
                    ["computed type", "retarget fk"]
                ),
                SeedRow(
                    DialectNames.SqlServer,
                    212,
                    FuzzSelection.ByKinds,
                    ["drop unique", "shrink pk"]
                ),
                SeedRow(
                    DialectNames.SqlServer,
                    537,
                    FuzzSelection.Single,
                    ["rename table case", "reorder referenced pk"]
                ),
                SeedRow(
                    DialectNames.SqlServer,
                    556,
                    FuzzSelection.Random,
                    ["computed nullable", "computed type"]
                ),
                SeedRow(
                    DialectNames.SqlServer,
                    556,
                    FuzzSelection.Single,
                    ["computed nullable", "computed type"]
                ),
                SeedRow(
                    DialectNames.SqlServer,
                    556,
                    FuzzSelection.ByKinds,
                    ["computed nullable", "computed type"]
                ),
                SeedRow(
                    DialectNames.SqlServer,
                    897,
                    FuzzSelection.All,
                    ["replace pk", "shrink pk"]
                ),
                SeedRow(DialectNames.SqlServer, 22, FuzzSelection.Random, ["replace pk"]),
                SeedRow(DialectNames.SqlServer, 81, FuzzSelection.Random, ["reorder pk"]),
                SeedRow(DialectNames.SqlServer, 81, FuzzSelection.Single, ["reorder pk"]),
                SeedRow(DialectNames.SqlServer, 133, FuzzSelection.All, ["computed nullable"]),
                SeedRow(DialectNames.SqlServer, 133, FuzzSelection.Default, ["computed nullable"]),
                SeedRow(DialectNames.SqlServer, 149, FuzzSelection.All, ["reorder referenced pk"]),
                SeedRow(
                    DialectNames.SqlServer,
                    149,
                    FuzzSelection.Default,
                    ["reorder referenced pk"]
                ),
                SeedRow(
                    DialectNames.SqlServer,
                    149,
                    FuzzSelection.Random,
                    ["reorder referenced pk"]
                ),
                SeedRow(
                    DialectNames.SqlServer,
                    149,
                    FuzzSelection.ByKinds,
                    ["reorder referenced pk"]
                ),
                SeedRow(DialectNames.SqlServer, 212, FuzzSelection.Default, ["shrink pk"]),
                SeedRow(DialectNames.SqlServer, 212, FuzzSelection.Single, ["shrink pk"]),
                // ---- MySQL の変更の種類と選び方の組（SamplingGaps に書いていた 31 組）。種は実 DB の総当たりを夜間の 1500 種で流した
                // 記録から貪欲に選んだ（2026-11-02・並びは埋める組の多い順） ----
                SeedRow(
                    DialectNames.MySql,
                    923,
                    FuzzSelection.Random,
                    [
                        "alter type duplicated fk",
                        "alter type duplicated unique",
                        "drop one of duplicated fks",
                        "drop one of duplicated uniques",
                    ]
                ),
                SeedRow(
                    DialectNames.MySql,
                    375,
                    FuzzSelection.Single,
                    ["computed nullable", "extend pk", "fk action"]
                ),
                SeedRow(
                    DialectNames.MySql,
                    525,
                    FuzzSelection.ByKinds,
                    ["alter type duplicated unique", "drop one of duplicated uniques", "move pk"]
                ),
                SeedRow(
                    DialectNames.MySql,
                    925,
                    FuzzSelection.Default,
                    ["alter type duplicated unique", "drop one of duplicated uniques", "shrink pk"]
                ),
                SeedRow(
                    DialectNames.MySql,
                    38,
                    FuzzSelection.Single,
                    ["rename table case", "shrink pk"]
                ),
                SeedRow(
                    DialectNames.MySql,
                    159,
                    FuzzSelection.Random,
                    ["reorder pk", "replace pk"]
                ),
                SeedRow(
                    DialectNames.MySql,
                    159,
                    FuzzSelection.Single,
                    ["reorder pk", "replace pk"]
                ),
                SeedRow(
                    DialectNames.MySql,
                    159,
                    FuzzSelection.ByKinds,
                    ["reorder pk", "replace pk"]
                ),
                SeedRow(
                    DialectNames.MySql,
                    544,
                    FuzzSelection.Default,
                    ["replace pk", "respell type"]
                ),
                SeedRow(DialectNames.MySql, 5, FuzzSelection.Random, ["drop tables"]),
                SeedRow(DialectNames.MySql, 21, FuzzSelection.All, ["computed type"]),
                SeedRow(DialectNames.MySql, 21, FuzzSelection.ByKinds, ["computed type"]),
                SeedRow(DialectNames.MySql, 23, FuzzSelection.ByKinds, ["extend pk"]),
                SeedRow(DialectNames.MySql, 55, FuzzSelection.All, ["retarget fk"]),
                SeedRow(DialectNames.MySql, 55, FuzzSelection.Single, ["retarget fk"]),
                SeedRow(DialectNames.MySql, 98, FuzzSelection.Random, ["computed nullable"]),
                SeedRow(DialectNames.MySql, 156, FuzzSelection.All, ["replace pk"]),
            ]
        );

    /// <summary>実 DB の総当たりで流す方言の行</summary>
    /// <param name="dialect">方言名</param>
    public static IEnumerable<ScenarioRow<SyncPlanCoverageInput>> ForRealDatabase(string dialect) =>
        Table.Where(row => row.Input.Dialect == dialect && row.Input.RealDatabase);

    /// <summary>能力の組の網で流す方言の行</summary>
    /// <param name="dialect">方言名</param>
    public static IEnumerable<ScenarioRow<SyncPlanCoverageInput>> ForNet(string dialect) =>
        Table.Where(row => row.Input.Dialect == dialect && row.Input.Net);

    /// <summary>行のケースを作る（選び方を強制する）</summary>
    /// <param name="row">行</param>
    /// <param name="dialect">方言の部品</param>
    public static ISyncPlanFuzzCase Create(
        ScenarioRow<SyncPlanCoverageInput> row,
        SyncPlanFuzzDialect dialect
    ) =>
        new SelectionOverrideCase(
            row.Input.Create(dialect),
            row.Input.Selection,
            row.Input.SelectionSeed
        );

    /// <summary>
    /// 行が出すはずの変更の種類を出さなかった行（生成器を変えて種の形が変わった・手で書いた行が変わった）を名指しする
    /// </summary>
    /// <param name="outcomes">網羅の行の結果</param>
    public static IEnumerable<string> MissingKindProblems(IEnumerable<FuzzSeedOutcome> outcomes)
    {
        foreach (var outcome in outcomes)
        {
            var row = Table[outcome.ScenarioId!];
            var kinds = outcome
                .Mutations.Select(SyncPlanFuzzDimensions.KindOf)
                .ToHashSet(StringComparer.Ordinal);
            var missing = row.Input.ExpectedKinds.Where(kind => !kinds.Contains(kind)).ToList();

            if (missing.Count > 0)
            {
                yield return $"coverage row {row.Id}: expected mutation kinds [{string.Join(", ", missing)}], "
                    + $"got [{string.Join(", ", outcome.Mutations)}] "
                    + "(the generator changed the seed's shape: pick the seed rows again with the fingerprints)";
            }

            if (outcome.Selection != row.Input.Selection)
            {
                yield return $"coverage row {row.Id}: expected selection {row.Input.Selection}, got {outcome.Selection}";
            }
        }
    }

    /// <summary>
    /// 種の行（実 DB の種 <paramref name="seed"/> の DB と変更をそのまま使い、選び方だけを <paramref name="selection"/> にする）
    /// </summary>
    /// <param name="dialect">方言名</param>
    /// <param name="seed">実 DB の総当たりの種</param>
    /// <param name="selection">強制する選び方</param>
    /// <param name="kinds">この行で埋める組の変更の種類（種の変更に含まれるはずの種類）</param>
    private static ScenarioRow<SyncPlanCoverageInput> SeedRow(
        string dialect,
        int seed,
        FuzzSelection selection,
        IReadOnlyList<string> kinds
    ) =>
        new(
            $"{IdPrefix}{dialect}-seed{seed}-{selection}",
            null,
            $"実 DB の種 {seed} の変更（{string.Join("・", kinds)}）を選び方 {selection} で流す",
            new SyncPlanCoverageInput(
                dialect,
                fuzzDialect => new SyncPlanFuzzGenerator(new Random(seed), fuzzDialect),
                selection,
                kinds,
                RealDatabase: true,
                Net: false,
                SelectionSeed: seed
            )
        );

    /// <summary>
    /// SQL Server の参照アクションの変更（項目 fk action と ReferentialAction の欠け）。生成器は SQL Server で参照アクションを
    /// 変えない（<see cref="SyncPlanFuzzDialect.AllowCascade"/> が偽＝連鎖の経路の重なりを CREATE で断る DB の制約を避ける）ので、
    /// 互いに独立した親子の組 8 つ（どの子も親を 1 つだけ参照する＝連鎖の経路が重ならない。子の列は NULL を許す＝SET NULL と
    /// SET DEFAULT の前提）の外部キーを、ON DELETE / ON UPDATE の 4 つの行き先へそれぞれ変え、5 つの選び方で流す
    /// </summary>
    private static IEnumerable<ScenarioRow<SyncPlanCoverageInput>> SqlServerReferentialActionRows()
    {
        // 行き先ごとの（句・元のアクション・行き先）。元のアクションは行き先と違うものにする
        (
            bool OnUpdate,
            ForeignKeyReferentialAction From,
            ForeignKeyReferentialAction To
        )[] changes =
        [
            (false, ForeignKeyReferentialAction.Cascade, ForeignKeyReferentialAction.NoAction),
            (false, ForeignKeyReferentialAction.NoAction, ForeignKeyReferentialAction.Cascade),
            (false, ForeignKeyReferentialAction.NoAction, ForeignKeyReferentialAction.SetNull),
            (false, ForeignKeyReferentialAction.NoAction, ForeignKeyReferentialAction.SetDefault),
            (true, ForeignKeyReferentialAction.Cascade, ForeignKeyReferentialAction.NoAction),
            (true, ForeignKeyReferentialAction.NoAction, ForeignKeyReferentialAction.Cascade),
            (true, ForeignKeyReferentialAction.NoAction, ForeignKeyReferentialAction.SetNull),
            (true, ForeignKeyReferentialAction.NoAction, ForeignKeyReferentialAction.SetDefault),
        ];

        foreach (var selection in Enum.GetValues<FuzzSelection>())
        {
            yield return new(
                $"{IdPrefix}{DialectNames.SqlServer}-fk-actions-{selection}",
                null,
                $"SQL Server の参照アクションの変更 8 つ（ON DELETE / ON UPDATE × NO ACTION・CASCADE・SET NULL・SET DEFAULT）を選び方 {selection} で流す",
                new SyncPlanCoverageInput(
                    DialectNames.SqlServer,
                    dialect => new SyncPlanScenarioCase(
                        dialect,
                        live =>
                        {
                            for (var i = 0; i < changes.Length; i++)
                            {
                                var (onUpdate, from, _) = changes[i];
                                var parent = live.Table($"p{i}_parents");
                                live.Int(parent, "id", pk: true);
                                var child = live.Table($"c{i}_children");
                                live.Int(child, "id", pk: true);
                                live.Ref(
                                    child,
                                    parent,
                                    "parent_id",
                                    $"fk_c{i}_p{i}",
                                    onDelete: onUpdate
                                        ? ForeignKeyReferentialAction.NoAction
                                        : from,
                                    nullable: true,
                                    onUpdate: onUpdate ? from : ForeignKeyReferentialAction.NoAction
                                );
                            }
                        },
                        target =>
                        {
                            for (var i = 0; i < changes.Length; i++)
                            {
                                var (onUpdate, _, to) = changes[i];
                                var relationship = target.ForeignKey($"c{i}_children", "parent_id");

                                if (onUpdate)
                                {
                                    target.SetOnUpdate(relationship, to);
                                }
                                else
                                {
                                    target.SetOnDelete(relationship, to);
                                }
                            }
                        },
                        select: null
                    ),
                    selection,
                    ["fk action"],
                    RealDatabase: true,
                    Net: true
                )
            );
        }
    }
}

/// <summary>
/// 中身のケースの DB 側の図と変更をそのまま使い、差分の選び方だけを強制するケース（選び方の意味は生成器と同じ
/// <see cref="SyncPlanFuzzGenerator.ApplySelection"/>）
/// </summary>
/// <param name="inner">中身のケース（Select は呼ばない）</param>
/// <param name="selection">強制する選び方</param>
/// <param name="selectionSeed">選び方が引く乱数の種（1 項目・種類の集まり・項目ごとの半々）</param>
internal sealed class SelectionOverrideCase(
    ISyncPlanFuzzCase inner,
    FuzzSelection selection,
    int selectionSeed
) : ISyncPlanFuzzCase
{
    /// <inheritdoc/>
    public List<string> Mutations => inner.Mutations;

    /// <inheritdoc/>
    public FuzzLiveSchema BuildLive() => inner.BuildLive();

    /// <inheritdoc/>
    public void Mutate(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    ) => inner.Mutate(entities, relationships, live);

    /// <inheritdoc/>
    public FuzzSelection Select(IReadOnlyList<SchemaDiffItem> items)
    {
        SyncPlanFuzzGenerator.ApplySelection(new Random(selectionSeed), selection, items);
        return selection;
    }
}
