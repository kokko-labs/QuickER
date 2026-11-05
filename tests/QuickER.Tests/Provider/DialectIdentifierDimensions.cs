using QuickER.Dialects;
using QuickER.Tests.TestSupport.Engines;
using F = QuickER.Tests.Provider.DialectIdentifierFunctions;
using S = QuickER.Tests.Provider.DialectIdentifierShapes;

namespace QuickER.Tests.Provider;

/// <summary>方言の識別子の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// 1 ケース＝表の 1 行を 1 方言で流したもの。観測は行の宣言（関数・入力の形）と方言だけを読む。種を持たないので、網羅しない組は毎回照合する。
/// </remarks>
internal static class DialectIdentifierDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "dialect-identifiers";

    /// <summary>方言</summary>
    public const string Dialect = "Dialect";

    /// <summary>関数</summary>
    public const string Function = "Function";

    /// <summary>入力の形</summary>
    public const string Shape = "Shape";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.Dialect,
            [
                new Factor(
                    Dialect,
                    [
                        .. DialectNames.All.Select(name => new Level(name)),
                        new Level(DialectDdlAxis.None),
                    ],
                    "方言（方言を持たない関数は -）"
                ),
                new Factor(Function, F.All.Select(name => new Level(name)).ToList(), "関数"),
                new Factor(Shape, S.All.Select(name => new Level(name)).ToList(), "入力の形"),
            ],
            null,
            IsValid
        );

    /// <summary>網羅しない組の一覧（すべて「その関数にその形の入力の行がまだ無い」だけで、行を足せば埋まる）</summary>
    public static CoverageGapSet Gaps { get; } = CoverageGap.Set(EngineIds.Dialect, PendingGaps());

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.Dialect,
            "Identifiers",
            "方言の識別子の表（クォート・分割・エスケープ・動的 SQL 用のクォート・生成ランタイムの引用・スキーマ修飾名の分け方）",
            Table,
            0,
            [
                "tests/QuickER.Tests/Provider/DialectDdlAdapters.cs",
                "tests/QuickER.Tests/Provider/DialectIdentifierCase.cs",
                "tests/QuickER.Tests/Provider/DialectIdentifierRows.cs",
                "tests/QuickER.Tests/Provider/DialectIdentifierDimensions.cs",
                "tests/QuickER.Tests/Provider/DialectIdentifierTableTests.cs",
            ],
            [
                "src/QuickER.Dialects",
                "src/QuickER.Provider.SqlServer",
                "src/QuickER.Provider.PostgreSql",
                "src/QuickER.Provider.MySql",
                "src/QuickER.Provider.Oracle",
                "src/QuickER.Provider.Sqlite",
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
    public static void Observe(
        CoverageCounter counter,
        DialectIdentifierCase row,
        string dialect
    ) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Dialect] = dialect,
                [Function] = row.Function,
                [Shape] = row.Shape,
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
                    "この組の入力の行がまだ無い（方言ごとに持つ入力が違い、全方言へ広げていない）。行を足せば埋まる",
                    (factorA, levelA),
                    (factorB, levelB)
                );
            }
        }
    }

    /// <summary>2 因子の組の、行がまだ無い組（実行の結果から書いた。行を足したら消す）</summary>
    private static (string FactorA, string LevelA, string FactorB, string LevelsB)[] PendingPairs =>
        [
            (Dialect, "sqlserver", Shape, "backslash"),
            (Dialect, "postgresql", Shape, "empty-part backslash quote-chars-in-parts"),
            (Dialect, "mysql", Shape, "empty-part quote-chars-in-parts"),
            (Dialect, "oracle", Shape, "empty-part backslash quote-chars-in-parts"),
            (Dialect, "sqlite", Shape, "backslash"),
            (
                Dialect,
                "-",
                Shape,
                "closing-quote dot-closing-quote single-quote newline non-ascii null backslash quote-chars-in-parts"
            ),
            (
                Function,
                "quote",
                Shape,
                "empty-part opening-quote single-quote newline backslash quote-chars-in-parts"
            ),
            (
                Function,
                "quote-simple",
                Shape,
                "multi-dot empty-part dot-closing-quote opening-quote backslash quote-chars-in-parts"
            ),
            (
                Function,
                "escape",
                Shape,
                "plain dot multi-dot empty-part dot-closing-quote single-quote newline backslash quote-chars-in-parts"
            ),
            (
                Function,
                "dynamic",
                Shape,
                "plain dot multi-dot empty-part dot-closing-quote opening-quote newline non-ascii empty null quote-chars-in-parts"
            ),
            (
                Function,
                "runtime-quote",
                Shape,
                "closing-quote dot-closing-quote opening-quote single-quote newline non-ascii null backslash"
            ),
            (
                Function,
                "split",
                Shape,
                "closing-quote dot-closing-quote single-quote newline non-ascii null backslash quote-chars-in-parts"
            ),
        ];

    /// <summary>原理的に起こり得ない組み合わせのときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        assignment.TryGetValue(Dialect, out var dialect);
        assignment.TryGetValue(Function, out var function);
        assignment.TryGetValue(Shape, out var shape);

        // スキーマ修飾名の分け方は方言に依らない関数（QuickER.Dialects）で、ほかの関数は方言の部品
        if (dialect is not null && function is not null)
        {
            if ((function == F.Split) != (dialect == DialectDdlAxis.None))
            {
                return false;
            }

            // 動的 SQL 用のクォートと生成ランタイムを持たない方言（アダプタの宣言＝製品に関数が無い）
            if (dialect != DialectDdlAxis.None)
            {
                var adapter = DialectDdlAxis.Get(dialect);

                if (function == F.Dynamic && adapter.QuoteForDynamicSql is null)
                {
                    return false;
                }

                if (function == F.RuntimeQuote && adapter.RuntimeQuoteTableName is null)
                {
                    return false;
                }
            }
        }

        // クォート開始文字の形は、開始と終端の文字が違う方言にだけある（同じ文字の方言では終端の文字の形になる。方言なしはクォートしない）
        if (function == F.Split && shape == S.OpeningQuote)
        {
            return false;
        }

        if (dialect is not null && shape == S.OpeningQuote)
        {
            if (dialect == DialectDdlAxis.None)
            {
                return false;
            }

            var adapter = DialectDdlAxis.Get(dialect);

            if (adapter.OpenQuote == adapter.CloseQuote)
            {
                return false;
            }
        }

        return true;
    }
}
