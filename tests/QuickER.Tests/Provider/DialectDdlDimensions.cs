using QuickER.Dialects;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Provider;

/// <summary>方言の DDL の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// 1 ケース＝表の 1 行を 1 方言で流したもの。観測は行の宣言（経路・要素・観点）と方言だけを読む。種を持たないので、網羅しない組は毎回照合する。
/// </remarks>
internal static class DialectDdlDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "dialect-ddl";

    /// <summary>方言</summary>
    public const string Dialect = "Dialect";

    /// <summary>要素</summary>
    public const string Element = "Element";

    /// <summary>観点</summary>
    public const string Facet = "Facet";

    /// <summary>経路</summary>
    public const string Path = "Path";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.Dialect,
            [
                new Factor(
                    Dialect,
                    DialectNames.All.Select(name => new Level(name)).ToList(),
                    "方言"
                ),
                new Factor(
                    Element,
                    DialectDdlElements.All.Select(name => new Level(name)).ToList(),
                    "要素"
                ),
                new Factor(
                    Facet,
                    DialectDdlFacets.All.Select(name => new Level(name)).ToList(),
                    "観点"
                ),
                new Factor(
                    Path,
                    DialectDdlPaths.All.Select(name => new Level(name)).ToList(),
                    "経路"
                ),
            ],
            null,
            IsValid
        );

    /// <summary>網羅しない組の一覧（すべて「その組の行がまだ無い」だけで、行を足せば埋まる）</summary>
    public static CoverageGapSet Gaps { get; } = CoverageGap.Set(EngineIds.Dialect, PendingGaps());

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.Dialect,
            "DdlText",
            "方言の DDL の表（DDL・同期スクリプトの文の形・識別子・制約名・注記・扱い・固定文の英語・カルチャ）",
            Table,
            0,
            [
                "tests/QuickER.Tests/Provider/DialectDdlAdapters.cs",
                "tests/QuickER.Tests/Provider/DialectDdlCase.cs",
                "tests/QuickER.Tests/Provider/DialectDdlReader.cs",
                "tests/QuickER.Tests/Provider/DialectDdlRunner.cs",
                "tests/QuickER.Tests/Provider/DialectDdlRows.cs",
                "tests/QuickER.Tests/Provider/DialectDdlDimensions.cs",
                "tests/QuickER.Tests/Provider/DialectDdlTableTests.cs",
            ],
            [
                "src/QuickER.Provider",
                "src/QuickER.Provider.SqlServer",
                "src/QuickER.Provider.PostgreSql",
                "src/QuickER.Provider.MySql",
                "src/QuickER.Provider.Oracle",
                "src/QuickER.Provider.Sqlite",
                "src/QuickER.Model",
            ]
        );

    /// <summary>網羅しない組を見つけた区切り</summary>
    private const string Finding = "P3-5b";

    /// <summary>網羅しない組を埋める区切り</summary>
    private const string FixedIn = "P4";

    /// <summary>1 ケースを数える</summary>
    /// <param name="counter">計数</param>
    /// <param name="row">行</param>
    /// <param name="dialect">方言</param>
    public static void Observe(CoverageCounter counter, DialectDdlCase row, string dialect) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Dialect] = dialect,
                [Element] = row.Element,
                [Facet] = row.Facet,
                [Path] = row.Path,
            }
        );

    private static IEnumerable<CoverageGapEntry> PendingGaps()
    {
        foreach (var (factorA, levelA, factorB, levels) in PendingPairs)
        {
            foreach (var levelB in levels.Split(' '))
            {
                yield return CoverageGap.Tuple(
                    Scope,
                    Finding,
                    FixedIn,
                    "この組の行がまだ無い（行は一部の方言・入力だけを持つ）。行を足せば埋まる",
                    (factorA, levelA),
                    (factorB, levelB)
                );
            }
        }
    }

    /// <summary>2 因子の組の、行がまだ無い組（実行の結果から書いた。行を足したら消す）</summary>
    private static (string FactorA, string LevelA, string FactorB, string LevelsB)[] PendingPairs =>
        [
            (Dialect, "sqlserver", Facet, "comment-sanitize"),
            (Dialect, "postgresql", Facet, "comment-sanitize"),
            (Dialect, "mysql", Facet, "comment-sanitize"),
            (Dialect, "oracle", Facet, "comment-sanitize"),
            (Element, "table", Facet, "constraint-name note order comment-sanitize"),
            (
                Element,
                "column",
                Facet,
                "quote constraint-name note omit order culture comment-sanitize"
            ),
            (Element, "column", Path, "sync-add-table sync-add-column sync-rebuild"),
            (Element, "pk", Facet, "quote note omit reject english culture comment-sanitize"),
            (Element, "pk", Path, "sync-add-column sync-rebuild"),
            (Element, "fk", Facet, "quote english culture comment-sanitize"),
            (Element, "fk", Path, "sync-add-column"),
            (Element, "unique", Facet, "quote note order english culture comment-sanitize"),
            (Element, "unique", Path, "sync-add-column sync-other"),
            (
                Element,
                "computed",
                Facet,
                "statement-shape quote constraint-name omit order reject english culture comment-sanitize"
            ),
            (Element, "computed", Path, "sync-other"),
            (
                Element,
                "identity",
                Facet,
                "statement-shape quote constraint-name omit reject english culture comment-sanitize"
            ),
            (Element, "identity", Path, "sync-other"),
            (Element, "description", Facet, "constraint-name order reject english culture"),
            (Element, "description", Path, "sync-add-table sync-rebuild"),
            (
                Element,
                "schema-qualified",
                Facet,
                "statement-shape quote omit order reject english culture comment-sanitize"
            ),
            (
                Element,
                "schema-qualified",
                Path,
                "sync-add-table sync-add-column sync-rebuild sync-other"
            ),
            (
                Element,
                "many-to-many",
                Facet,
                "statement-shape quote constraint-name omit order reject english culture comment-sanitize"
            ),
            (
                Element,
                "many-to-many",
                Path,
                "sync-add-table sync-add-column sync-rebuild sync-other"
            ),
            (Element, "type", Facet, "quote constraint-name note order english comment-sanitize"),
            (Element, "type", Path, "sync-add-column sync-rebuild"),
            (Facet, "statement-shape", Path, "sync-add-table sync-add-column sync-rebuild"),
            (Facet, "quote", Path, "sync-add-table sync-rebuild sync-other"),
            (Facet, "constraint-name", Path, "sync-add-column"),
            (Facet, "note", Path, "sync-other"),
            (Facet, "order", Path, "sync-add-table sync-rebuild"),
            (Facet, "reject", Path, "sync-add-column"),
            (Facet, "english", Path, "sync-add-table sync-add-column"),
            (Facet, "culture", Path, "sync-add-column sync-rebuild"),
            (
                Facet,
                "comment-sanitize",
                Path,
                "sync-add-table sync-add-column sync-rebuild sync-other"
            ),
        ];

    /// <summary>原理的に起こり得ない組み合わせのときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        assignment.TryGetValue(Dialect, out var dialect);
        assignment.TryGetValue(Path, out var path);

        // テーブルの再構築は、ALTER COLUMN か外部キーの後付けを持たない方言の同期だけが使う（SyncPlanner と同じ宣言の読み方）
        if (dialect is not null && path == DialectDdlPaths.SyncRebuild)
        {
            var capabilities = DialectDdlAxis.Get(dialect).Provider.SyncCapabilities;

            if (capabilities.SupportsAlterColumn && capabilities.SupportsForeignKeyAlter)
            {
                return false;
            }
        }

        return true;
    }
}
