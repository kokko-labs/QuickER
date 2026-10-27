using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 同期計画の総当たりの次元表・網羅しない組の一覧・台帳の行
/// </summary>
/// <remarks>
/// <para>
/// ケースは、種 1 つの結果（<see cref="FuzzSeedOutcome"/>）と、その種が目標の図へ加えた変更 1 つの組。観測は種を流し終えた
/// 結果を読むだけで、生成器の乱数を引かない（種の指紋は変わらない）。変更の種類は、生成器が書いた変更の文の頭
/// （<see cref="MutationKinds"/>）で決め、知らない頭は文の頭の語をそのまま水準にする＝宣言していない水準として観測が断る。
/// </para>
/// <para>
/// 範囲は方言の名前。網羅しない組の照合は、既定の種の数で、種が <c>ShapeCoverageMinimumSeeds</c>（50）以上の方言だけで
/// 行う（必須の形の表明と同じ条件＝SQLite 2000・MySQL 150・SQL Server 100）。PostgreSQL（既定 15 種）と Oracle（既定 4 種）
/// は観測と要約だけにする。種が少なく欠けが種の引き方の偶然で決まるうえ、Oracle は <c>QUICKER_SYNC_FUZZ_ORACLE=1</c> で
/// 既定の数そのものが 4 から 30 に変わり（<see cref="SeedPlan.IsDefaultCount"/> は真のまま）、一覧が環境で入れ替わるため。
/// </para>
/// </remarks>
internal static class SyncPlanFuzzDimensions
{
    /// <summary>差分の選び方</summary>
    public const string Selection = "Selection";

    /// <summary>目標の図へ加えた変更の種類</summary>
    public const string Mutation = "Mutation";

    /// <summary>
    /// 生成器（<see cref="SyncPlanFuzzGenerator"/> の Mutate）が書く変更の文の頭（長い頭から照合する＝<c>drop tables</c> を
    /// <c>drop table</c> より先に）。<c>alter type ... (duplicated fk)</c> は同じ列に外部キー 2 本の形の型の変更として分ける
    /// </summary>
    public static IReadOnlyList<string> MutationKinds { get; } =
    [
        "rename table case",
        "rename column case",
        "add table",
        "drop table",
        "drop tables",
        "add column",
        "drop column",
        "alter type",
        "alter type duplicated fk",
        "nullable",
        "reorder pk",
        "extend pk",
        "shrink pk",
        "move pk",
        "replace pk",
        "remove pk",
        "add fk",
        "drop fk",
        "fk action",
        "add unique",
        "drop unique",
        "change unique",
        "computed type",
        "computed nullable",
        "reorder columns",
        "reorder referenced pk",
        "retarget fk",
        "drop one of duplicated fks",
    ];

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.SyncPlan,
            [
                new Factor(
                    Selection,
                    Enum.GetValues<FuzzSelection>()
                        .Select(value => new Level(value.ToString()))
                        .ToList(),
                    "差分の選び方（種ごとに 1 つ）"
                ),
                new Factor(
                    Mutation,
                    MutationKinds.Select(kind => new Level(kind)).ToList(),
                    "目標の図へ加えた変更の種類（種ごとに 1〜5 個）"
                ),
            ]
        );

    /// <summary>
    /// 出現の少ない変更の、出にくい理由（前提の形）。前提の無い変更は、重み 1（全 26）の抽選だけで出にくい
    /// </summary>
    private static readonly Dictionary<string, string> RareMutationPremises = new(
        StringComparer.Ordinal
    )
    {
        ["reorder pk"] = "主キーが 2 列以上の表が要る（取り込んだ図の主キーの多くは 1 列）",
        ["shrink pk"] = "主キーが 2 列以上で、残りの列だけで一意な表が要る",
        ["extend pk"] = "主キーへ足せる列（NULL を含まない整数か文字列）が要る",
        ["move pk"] = "主キーを移せる、NULL を含まず一意な値の整数か文字列の列が要る",
        ["replace pk"] = "行の無い表が要る",
        ["computed type"] =
            "計算列は種の 40% でだけ作られ、ChangeComputed は型と NULL 許容のどちらかを半々で選ぶ",
        ["computed nullable"] =
            "計算列は種の 40% でだけ作られ、ChangeComputed は型と NULL 許容のどちらかを半々で選ぶ",
        ["reorder referenced pk"] = "外部キーに参照されている複合主キーが要る",
        ["retarget fk"] = "外部キーを付け替えられる別の列が要る",
        ["alter type duplicated fk"] = "同じ列ペアに外部キーが 2 本ある形が要る",
        ["drop one of duplicated fks"] = "同じ列ペアに外部キーが 2 本ある形が要る",
    };

    /// <summary>網羅しない組の一覧（範囲は方言の名前＝sqlite・mysql・sqlserver）</summary>
    /// <remarks>
    /// 変更の種類と差分の選び方の組の欠け（<c>SamplingGaps</c>）は、固定した既定の種の並びに合わせて書いている。
    /// 生成器を変えて種の指紋が変わったら、指紋と一緒にこの一覧も書き直す。
    /// </remarks>
    public static CoverageGapSet Gaps { get; } =
        CoverageGap.Set(
            EngineIds.SyncPlan,
            [
                CoverageGap.Matching(
                    "sqlite",
                    "P1-3",
                    "P2",
                    "SQLite の取込は UNIQUE 制約の名前を持ち帰らない（sqlite_autoindex_*＝名前 null）ので、名前付きの一意制約を前提にする change unique は、同じ種の変更で名前付きの一意制約を足した後にしか選べず、既定の 2000 種で 7 回しか出ない（差分の選び方 5 通りの全部と組まない）",
                    Selection,
                    Mutation,
                    (selection, mutation) =>
                        mutation == "change unique"
                        && selection is nameof(FuzzSelection.All) or nameof(FuzzSelection.Default)
                ),
                .. SamplingGaps(
                    "mysql",
                    MySqlSyncPlanFuzzTests.DefaultSeeds,
                    new()
                    {
                        ["computed nullable"] = [FuzzSelection.ByKinds, FuzzSelection.Single],
                        ["computed type"] =
                        [
                            FuzzSelection.All,
                            FuzzSelection.Default,
                            FuzzSelection.Random,
                            FuzzSelection.Single,
                            FuzzSelection.ByKinds,
                        ],
                        ["drop fk"] = [FuzzSelection.ByKinds],
                        ["fk action"] = [FuzzSelection.Default, FuzzSelection.Single],
                        ["move pk"] = [FuzzSelection.Default, FuzzSelection.Single],
                        ["rename table case"] = [FuzzSelection.ByKinds],
                        ["reorder columns"] = [FuzzSelection.Single],
                        ["reorder pk"] =
                        [
                            FuzzSelection.All,
                            FuzzSelection.Default,
                            FuzzSelection.Random,
                            FuzzSelection.Single,
                        ],
                        ["reorder referenced pk"] =
                        [
                            FuzzSelection.Default,
                            FuzzSelection.Single,
                            FuzzSelection.ByKinds,
                        ],
                        ["replace pk"] =
                        [
                            FuzzSelection.All,
                            FuzzSelection.Single,
                            FuzzSelection.ByKinds,
                        ],
                        ["retarget fk"] = [FuzzSelection.Random, FuzzSelection.ByKinds],
                        ["shrink pk"] =
                        [
                            FuzzSelection.All,
                            FuzzSelection.Default,
                            FuzzSelection.Single,
                            FuzzSelection.ByKinds,
                        ],
                    }
                ),
                .. SamplingGaps(
                    "sqlserver",
                    SqlServerSyncPlanFuzzTests.DefaultSeeds,
                    new()
                    {
                        ["add table"] = [FuzzSelection.Single],
                        ["alter type duplicated fk"] = [FuzzSelection.All, FuzzSelection.Single],
                        ["computed nullable"] =
                        [
                            FuzzSelection.All,
                            FuzzSelection.Default,
                            FuzzSelection.Random,
                            FuzzSelection.ByKinds,
                        ],
                        ["computed type"] = [FuzzSelection.All, FuzzSelection.Default],
                        ["drop fk"] = [FuzzSelection.Random],
                        ["drop one of duplicated fks"] = [FuzzSelection.All, FuzzSelection.Single],
                        ["drop tables"] = [FuzzSelection.ByKinds],
                        ["extend pk"] = [FuzzSelection.Single],
                        ["fk action"] =
                        [
                            FuzzSelection.All,
                            FuzzSelection.Default,
                            FuzzSelection.Random,
                            FuzzSelection.Single,
                            FuzzSelection.ByKinds,
                        ],
                        ["move pk"] = [FuzzSelection.All],
                        ["rename column case"] = [FuzzSelection.Single],
                        ["rename table case"] = [FuzzSelection.All],
                        ["reorder columns"] = [FuzzSelection.ByKinds],
                        ["reorder pk"] =
                        [
                            FuzzSelection.Random,
                            FuzzSelection.Single,
                            FuzzSelection.ByKinds,
                        ],
                        ["reorder referenced pk"] = [FuzzSelection.Random, FuzzSelection.Single],
                        ["replace pk"] = [FuzzSelection.All, FuzzSelection.Single],
                        ["retarget fk"] =
                        [
                            FuzzSelection.All,
                            FuzzSelection.Random,
                            FuzzSelection.Single,
                            FuzzSelection.ByKinds,
                        ],
                        ["shrink pk"] = [FuzzSelection.ByKinds],
                    }
                ),
            ]
        );

    /// <summary>
    /// 種の数が足りずに出揃わない組を、変更の種類ごとに 1 項目にする（項目は名指しした選び方の組だけに当たる）
    /// </summary>
    /// <param name="dialect">範囲（方言の名前）</param>
    /// <param name="seeds">その方言の既定の種の数</param>
    /// <param name="missing">変更の種類ごとの、組まない選び方</param>
    private static IEnumerable<CoverageGapEntry> SamplingGaps(
        string dialect,
        int seeds,
        Dictionary<string, FuzzSelection[]> missing
    ) =>
        missing.Select(pair =>
        {
            var selections = pair.Value.Select(s => s.ToString()).ToHashSet(StringComparer.Ordinal);
            var premise = RareMutationPremises.TryGetValue(pair.Key, out var text)
                ? text
                : "前提の形は要らないが、重み 1（全 26）の抽選でしか選ばれない";

            return CoverageGap.Matching(
                dialect,
                "P1-3",
                "P2",
                $"{pair.Key} は出にくい（{premise}）。差分の選び方は種ごとに 1 つを 5 通りから引くので、既定の {seeds} 種では {string.Join("・", pair.Value)} と組む種がたまたま無い（選び方を変更の種類に割り付けて組を作る生成をしていない）",
                Selection,
                Mutation,
                (selection, mutation) =>
                    string.Equals(mutation, pair.Key, StringComparison.Ordinal)
                    && selections.Contains(selection)
            );
        });

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.SyncPlan,
            "Fuzz",
            "同期計画の総当たり（5 方言の実 DB）",
            Table,
            0,
            [
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzDialect.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzDimensions.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzGenerator.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzKnownFailures.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzRunner.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzTestsBase.cs",
            ],
            [
                "src/QuickER.Provider/SchemaDiffService.cs",
                "src/QuickER.Provider/SyncPlanner.cs",
                "src/QuickER.Provider/SyncPlan.cs",
                "src/QuickER.Provider/SyncDialectCapabilities.cs",
                "src/QuickER.Provider/SyncScriptBuilderBase.cs",
                "src/QuickER.Provider/SyncScriptBuilderHelper.cs",
                "src/QuickER.Provider.MySql",
                "src/QuickER.Provider.Oracle",
                "src/QuickER.Provider.PostgreSql",
                "src/QuickER.Provider.SqlServer",
                "src/QuickER.Provider.Sqlite",
            ]
        );

    /// <summary>種 1 つの結果を、変更ごとのケースとして数える（結果を読むだけで乱数を引かない）</summary>
    /// <param name="counter">数える先</param>
    /// <param name="outcome">種 1 つの結果</param>
    public static void Observe(CoverageCounter counter, FuzzSeedOutcome outcome)
    {
        foreach (var mutation in outcome.Mutations)
        {
            counter.Observe(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Selection] = outcome.Selection.ToString(),
                    [Mutation] = KindOf(mutation),
                }
            );
        }
    }

    /// <summary>変更の文の種類（知らない頭は文の頭の 2 語を返す＝宣言していない水準）</summary>
    internal static string KindOf(string mutation)
    {
        if (
            mutation.StartsWith("alter type ", StringComparison.Ordinal)
            && mutation.EndsWith(" (duplicated fk)", StringComparison.Ordinal)
        )
        {
            return "alter type duplicated fk";
        }

        var kind = MutationKinds
            .Where(kind => mutation.StartsWith(kind + " ", StringComparison.Ordinal))
            .OrderByDescending(kind => kind.Length)
            .FirstOrDefault();

        return kind ?? $"(unknown: {string.Join(' ', mutation.Split(' ').Take(2))})";
    }
}
