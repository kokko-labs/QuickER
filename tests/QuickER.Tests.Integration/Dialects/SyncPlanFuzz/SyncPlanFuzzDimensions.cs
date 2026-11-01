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
        "alter type duplicated unique",
        "drop one of duplicated uniques",
        "respell type",
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

    /// <summary>網羅しない組の一覧（範囲は方言の名前＝sqlite・mysql・sqlserver）</summary>
    /// <remarks>
    /// 載せるのは受け入れ（製品か DB の事実）だけ。既定の種の並びで欠ける変更の種類と選び方の組は、網羅の行
    /// （<see cref="SyncPlanCoverageRows"/>）が種の前に流して埋める（網羅の行は既定の種の並びに合わせて選んだので、生成器を変えて
    /// 種の指紋が変わったら、指紋と一緒に網羅の行も選び直す）。
    /// </remarks>
    public static CoverageGapSet Gaps { get; } =
        CoverageGap.Set(
            EngineIds.SyncPlan,
            [
                // 変更の種類を作らない方言の欠け（選び方の 5 通りの全部）
                MissingMutationGap(
                    "sqlite",
                    "respell type",
                    "SQLite は宣言の型をそのまま覚え、型の正規化を持たない（製品の事実＝NormalizeTypeNotation が恒等）ので、同じ型の別の綴りが無い"
                ),
                // SQLite の DB は同じ列の並びの UNIQUE 制約を 1 つの索引にまとめ（取り込むと 1 つ）、目標の図の変更でも重ねない
                // （SyncPlanFuzzDialect.DistinguishesSameSetConstraints）ので、同じ列の並びの一意制約 2 つを前提にする変更は作れない
                MissingMutationGap(
                    "sqlite",
                    "alter type duplicated unique",
                    "SQLite は同じ列の並びの UNIQUE 制約を 1 つの索引にまとめ（DB の事実）、製品は名前の無い制約を同じ列集合の DB の制約で満たされるとみなす（製品の事実）ので、同じ列の並びの一意制約 2 つの形が無い"
                ),
                MissingMutationGap(
                    "sqlite",
                    "drop one of duplicated uniques",
                    "SQLite は同じ列の並びの UNIQUE 制約を 1 つの索引にまとめ（DB の事実）、製品は名前の無い制約を同じ列集合の DB の制約で満たされるとみなす（製品の事実）ので、同じ列の並びの一意制約 2 つの形が無い"
                ),
                // SQL Server の参照アクションの変更（生成器が作らない＝AllowCascade が偽）と、MySQL・SQL Server の変更の種類と
                // 選び方の組で既定の種の並びに出ないもの（出にくい変更 × 選び方）は、網羅の行（SyncPlanCoverageRows）が埋める
            ]
        );

    /// <summary>変更の種類が一度も出ない方言の欠け（選び方の 5 通りの全部・受け入れ＝製品か DB の事実）</summary>
    private static CoverageGapEntry MissingMutationGap(
        string dialect,
        string mutation,
        string reason
    ) =>
        CoverageGap.AcceptedMatching(
            dialect,
            "P2-4",
            reason,
            Selection,
            Mutation,
            (_, kind) => kind == mutation
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.SyncPlan,
            "Fuzz",
            "同期計画の総当たり（5 方言の実 DB）",
            Table,
            SyncPlanScenarios.Table.Count + SyncPlanCoverageRows.Table.Count,
            [
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanCoverageRows.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzDialect.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzDimensions.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzGenerator.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzKnownFailures.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzRunner.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzTestsBase.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanScenarios.cs",
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
        if (mutation.StartsWith("alter type ", StringComparison.Ordinal))
        {
            if (mutation.EndsWith(" (duplicated fk)", StringComparison.Ordinal))
            {
                return "alter type duplicated fk";
            }

            if (mutation.EndsWith(" (duplicated unique)", StringComparison.Ordinal))
            {
                return "alter type duplicated unique";
            }
        }

        var kind = MutationKinds
            .Where(kind => mutation.StartsWith(kind + " ", StringComparison.Ordinal))
            .OrderByDescending(kind => kind.Length)
            .FirstOrDefault();

        return kind ?? $"(unknown: {string.Join(' ', mutation.Split(' ').Take(2))})";
    }
}
