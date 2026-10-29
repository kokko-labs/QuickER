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

    /// <summary>
    /// 出現の少ない変更の、出にくい理由（前提の形）。前提の無い変更は、重み 1（全 29）の抽選だけで出にくい
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
        ["alter type duplicated unique"] = "同じ列の並びに一意制約が 2 つある形が要る",
        ["drop one of duplicated uniques"] = "同じ列の並びに一意制約が 2 つある形が要る",
        ["respell type"] = "既定の精度の日時の列が要る",
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
                // 変更の種類を作らない方言の欠け（選び方の 5 通りの全部）
                MissingMutationGap(
                    "sqlite",
                    "respell type",
                    null,
                    "SQLite は宣言の型をそのまま覚え、型の正規化を持たない（製品の事実＝NormalizeTypeNotation が恒等）ので、同じ型の別の綴りが無い"
                ),
                // SQLite の DB は同じ列の並びの UNIQUE 制約を 1 つの索引にまとめ（取り込むと 1 つ）、目標の図の変更でも重ねない
                // （SyncPlanFuzzDialect.DistinguishesSameSetConstraints）ので、同じ列の並びの一意制約 2 つを前提にする変更は作れない
                MissingMutationGap(
                    "sqlite",
                    "alter type duplicated unique",
                    null,
                    "SQLite は同じ列の並びの UNIQUE 制約を 1 つの索引にまとめ（DB の事実）、製品は名前の無い制約を同じ列集合の DB の制約で満たされるとみなす（製品の事実）ので、同じ列の並びの一意制約 2 つの形が無い"
                ),
                MissingMutationGap(
                    "sqlite",
                    "drop one of duplicated uniques",
                    null,
                    "SQLite は同じ列の並びの UNIQUE 制約を 1 つの索引にまとめ（DB の事実）、製品は名前の無い制約を同じ列集合の DB の制約で満たされるとみなす（製品の事実）ので、同じ列の並びの一意制約 2 つの形が無い"
                ),
                MissingMutationGap(
                    "sqlserver",
                    "fk action",
                    "P2-末",
                    "生成器は SQL Server で参照アクションを変えない（SyncPlanFuzzDialect.AllowCascade が偽：連鎖の経路の重なりを CREATE で断る DB の制約に当たらないよう、参照アクションの変更をまとめて作らない。重ならない経路だけで変える生成はエンジンの側で作れる）"
                ),
                .. SamplingGaps(
                    "mysql",
                    MySqlSyncPlanFuzzTests.DefaultSeeds,
                    new()
                    {
                        ["alter type duplicated fk"] = [FuzzSelection.Random],
                        ["alter type duplicated unique"] =
                        [
                            FuzzSelection.Default,
                            FuzzSelection.Random,
                            FuzzSelection.ByKinds,
                        ],
                        ["computed nullable"] = [FuzzSelection.Random, FuzzSelection.Single],
                        ["computed type"] = [FuzzSelection.All, FuzzSelection.ByKinds],
                        ["drop one of duplicated fks"] = [FuzzSelection.Random],
                        ["drop one of duplicated uniques"] =
                        [
                            FuzzSelection.Default,
                            FuzzSelection.Random,
                            FuzzSelection.ByKinds,
                        ],
                        ["drop tables"] = [FuzzSelection.Random],
                        ["extend pk"] = [FuzzSelection.Single, FuzzSelection.ByKinds],
                        ["fk action"] = [FuzzSelection.Single],
                        ["move pk"] = [FuzzSelection.ByKinds],
                        ["rename table case"] = [FuzzSelection.Single],
                        ["reorder pk"] =
                        [
                            FuzzSelection.Random,
                            FuzzSelection.Single,
                            FuzzSelection.ByKinds,
                        ],
                        ["replace pk"] =
                        [
                            FuzzSelection.All,
                            FuzzSelection.Default,
                            FuzzSelection.Random,
                            FuzzSelection.Single,
                            FuzzSelection.ByKinds,
                        ],
                        ["respell type"] = [FuzzSelection.Default],
                        ["retarget fk"] = [FuzzSelection.All, FuzzSelection.Single],
                        ["shrink pk"] = [FuzzSelection.Default, FuzzSelection.Single],
                    }
                ),
                .. SamplingGaps(
                    "sqlserver",
                    SqlServerSyncPlanFuzzTests.DefaultSeeds,
                    new()
                    {
                        ["add table"] = [FuzzSelection.ByKinds],
                        ["add unique"] = [FuzzSelection.Single],
                        ["alter type duplicated fk"] = [FuzzSelection.Random],
                        ["alter type duplicated unique"] =
                        [
                            FuzzSelection.All,
                            FuzzSelection.Default,
                            FuzzSelection.Random,
                            FuzzSelection.ByKinds,
                        ],
                        ["computed nullable"] =
                        [
                            FuzzSelection.All,
                            FuzzSelection.Default,
                            FuzzSelection.Random,
                            FuzzSelection.Single,
                            FuzzSelection.ByKinds,
                        ],
                        ["computed type"] =
                        [
                            FuzzSelection.All,
                            FuzzSelection.Default,
                            FuzzSelection.Random,
                            FuzzSelection.Single,
                            FuzzSelection.ByKinds,
                        ],
                        ["drop fk"] = [FuzzSelection.Random, FuzzSelection.Single],
                        ["drop one of duplicated fks"] = [FuzzSelection.Random],
                        ["drop one of duplicated uniques"] =
                        [
                            FuzzSelection.All,
                            FuzzSelection.Default,
                            FuzzSelection.Random,
                            FuzzSelection.ByKinds,
                        ],
                        ["drop unique"] = [FuzzSelection.ByKinds],
                        ["extend pk"] =
                        [
                            FuzzSelection.Default,
                            FuzzSelection.Random,
                            FuzzSelection.Single,
                        ],
                        ["move pk"] = [FuzzSelection.All],
                        ["rename column case"] = [FuzzSelection.Single],
                        ["rename table case"] = [FuzzSelection.Single],
                        ["reorder columns"] = [FuzzSelection.Random],
                        ["reorder pk"] = [FuzzSelection.Random, FuzzSelection.Single],
                        ["reorder referenced pk"] =
                        [
                            FuzzSelection.All,
                            FuzzSelection.Default,
                            FuzzSelection.Random,
                            FuzzSelection.Single,
                            FuzzSelection.ByKinds,
                        ],
                        ["replace pk"] =
                        [
                            FuzzSelection.All,
                            FuzzSelection.Default,
                            FuzzSelection.Random,
                        ],
                        ["respell type"] = [FuzzSelection.Default],
                        ["retarget fk"] =
                        [
                            FuzzSelection.All,
                            FuzzSelection.Default,
                            FuzzSelection.Random,
                            FuzzSelection.Single,
                        ],
                        ["shrink pk"] =
                        [
                            FuzzSelection.All,
                            FuzzSelection.Default,
                            FuzzSelection.Random,
                            FuzzSelection.Single,
                            FuzzSelection.ByKinds,
                        ],
                    }
                ),
            ]
        );

    /// <summary>
    /// 変更の種類が一度も出ない方言の欠け（選び方の 5 通りの全部）。<paramref name="fixedIn"/> が null なら受け入れ（製品か DB の事実）
    /// </summary>
    private static CoverageGapEntry MissingMutationGap(
        string dialect,
        string mutation,
        string? fixedIn,
        string reason
    )
    {
        Func<string, string, bool> predicate = (_, kind) => kind == mutation;
        return fixedIn is null
            ? CoverageGap.AcceptedMatching(dialect, "P2-4", reason, Selection, Mutation, predicate)
            : CoverageGap.Matching(
                dialect,
                "P2-4",
                fixedIn,
                reason,
                Selection,
                Mutation,
                predicate
            );
    }

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
                : "前提の形は要らないが、重み 1（全 29）の抽選でしか選ばれない";

            return CoverageGap.Matching(
                dialect,
                "P1-3",
                "P2-末",
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
            SyncPlanScenarios.Table.Count,
            [
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
