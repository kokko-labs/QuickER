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

    /// <summary>
    /// 網羅しない組の一覧：行がまだ無い組（<see cref="PendingPairs"/>）・受け入れ（<see cref="AcceptedGaps"/>＝製品の作り方の事実）
    /// </summary>
    public static CoverageGapSet Gaps { get; } =
        CoverageGap.Set(EngineIds.Dialect, [.. PendingGaps(), .. AcceptedGaps()]);

    /// <summary>受け入れを見つけた区切り</summary>
    private const string AcceptedFinding = "P4-2e";

    /// <summary>受け入れ（製品の作り方の事実で、行を足しても埋まらない組）</summary>
    private static IEnumerable<CoverageGapEntry> AcceptedGaps()
    {
        const string sanitize = DialectDdlFacets.CommentSanitize;

        // 説明を -- コメントへ書くのは SQLite の DDL だけ（ほかの 4 方言は説明を文のリテラルに入れる）
        yield return CoverageGap.AcceptedMatching(
            Scope,
            AcceptedFinding,
            "説明を -- コメントへ書くのは SQLite の DDL だけ（SqliteDdlGenerator.cs:138・205）。ほかの 4 方言は説明を文のリテラルへ入れる"
                + "（SqlServerDdlGenerator.cs:44-85 の拡張プロパティ・PostgreSqlDdlGenerator.cs:45-54 と OracleDdlGenerator.cs:56-69 の"
                + " COMMENT ON・MySqlDdlGenerator.cs:57-63 のインラインの COMMENT）ので、コメントの畳みが効く入力が無い",
            Dialect,
            Facet,
            (dialect, facet) =>
                facet == sanitize
                && dialect
                    is DialectNames.SqlServer
                        or DialectNames.PostgreSql
                        or DialectNames.MySql
                        or DialectNames.Oracle
        );

        // コメントへ埋まる名前・型は、全入口が改行・制御文字を先に断る
        const string namesReason =
            "コメントへ埋まる名前・型は、DDL・同期の全入口が改行・制御文字を先に断る（SqlNameText.Validate・SqlTypeText.Validate＝"
            + "DdlGeneratorBase.cs:35・39・SyncScriptBuilderBase.cs:32・36・SqliteDdlGenerator.cs:58・62・SqliteSyncScriptBuilder.cs:55・59。"
            + "判定は SqlComment.ContainsControlCharacter と同じ）ので、畳みの対象になる入力が届かない。説明を -- コメントへ書くのは"
            + " SQLite の DDL の表・列だけ（SqliteDdlGenerator.cs:138・205）で、同期の経路は説明をコメントへ書かない";

        yield return CoverageGap.AcceptedMatching(
            Scope,
            AcceptedFinding,
            namesReason,
            Element,
            Facet,
            (element, facet) =>
                facet == sanitize
                && element
                    is DialectDdlElements.PrimaryKey
                        or DialectDdlElements.ForeignKey
                        or DialectDdlElements.Unique
                        or DialectDdlElements.Computed
                        or DialectDdlElements.Identity
                        or DialectDdlElements.SchemaQualified
                        or DialectDdlElements.ManyToMany
                        or DialectDdlElements.Type
        );

        yield return CoverageGap.AcceptedMatching(
            Scope,
            AcceptedFinding,
            namesReason,
            Facet,
            Path,
            (facet, path) => facet == sanitize && path != DialectDdlPaths.Ddl
        );

        // 多対多は識別子を出さず、同期は多対多を除いて外部キーを組む
        yield return CoverageGap.AcceptedTuple(
            Scope,
            AcceptedFinding,
            "多対多は識別子を出さず、DDL はフリーテキストのコメントだけを書く（DdlGeneratorBase.cs:144-152・名前は SqlComment.Sanitize で畳む）",
            (Element, DialectDdlElements.ManyToMany),
            (Facet, DialectDdlFacets.Quote)
        );

        yield return CoverageGap.AcceptedMatching(
            Scope,
            AcceptedFinding,
            "同期は差分・計画とも外部キーを ForeignKeyColumnPairResolver.ResolveAll（多対多を除く）で組むので、多対多は同期の出力に現れない"
                + "（SyncPlanner.cs:3609・SchemaDiffService.cs:432・446）",
            Element,
            Path,
            (element, path) =>
                element == DialectDdlElements.ManyToMany && path != DialectDdlPaths.Ddl
        );
    }

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
        Observe(counter, row, dialect, row.Facet);

    /// <summary>1 ケースを、行の宣言とは別の観点で数える（全行に掛かる自動の照合が実際に走った観点）</summary>
    /// <param name="counter">計数</param>
    /// <param name="row">行</param>
    /// <param name="dialect">方言</param>
    /// <param name="facet">観点</param>
    public static void Observe(
        CoverageCounter counter,
        DialectDdlCase row,
        string dialect,
        string facet
    ) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Dialect] = dialect,
                [Element] = row.Element,
                [Facet] = facet,
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
        [];

    /// <summary>原理的に起こり得ない組み合わせのときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        assignment.TryGetValue(Dialect, out var dialect);
        assignment.TryGetValue(Path, out var path);
        assignment.TryGetValue(Element, out var element);
        assignment.TryGetValue(Facet, out var facet);

        // 説明と型の表記は制約でないので、制約名の観点を持たない（説明は COMMENT ON・拡張プロパティ・インラインの COMMENT・
        // コメント行で書き、制約を作らない。型の表記は列定義へ素通しするスカラで、制約名は型から作られない）
        if (
            facet == DialectDdlFacets.ConstraintName
            && element is DialectDdlElements.Description or DialectDdlElements.Type
        )
        {
            return false;
        }

        // 型の表記は 1 つのトークンで、並べ替えの対象になる複数の出力を持たない
        if (facet == DialectDdlFacets.Order && element == DialectDdlElements.Type)
        {
            return false;
        }

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
