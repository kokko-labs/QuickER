using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Provider;

/// <summary>方言の型の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// <para>
/// 1 ケース＝表の 1 行を 1 方言で流したもの。観測は行の宣言（観点・境界）と、入力から決まる正規型の種類だけを読む
/// （<see cref="DialectTypeRunner.ObservedKind"/>）。種を持たないので、網羅しない組は毎回照合する。
/// </para>
/// <para>
/// 境界（<see cref="Boundary"/>）は書き出しの入力（正規型）の性質で、上限の「ちょうど」と「1 つ超え」は書き出しの観点だけが持つ。
/// 読めない引数（<see cref="DialectTypeBoundaries.InvalidArg"/>）だけは型の表記を入力にする観点の性質。
/// </para>
/// </remarks>
internal static class DialectTypeDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "dialect-types";

    /// <summary>方言</summary>
    public const string Dialect = "Dialect";

    /// <summary>正規型の種類</summary>
    public const string Kind = "Kind";

    /// <summary>観点</summary>
    public const string Facet = "Facet";

    /// <summary>境界の種類</summary>
    public const string Boundary = "Boundary";

    /// <summary>解析できない型の表記の種類の水準</summary>
    public const string Unparsed = "unparsed";

    /// <summary>種類を持たない観点（型カタログの事実・型の表記の安全性）の種類の水準</summary>
    public const string NoKind = "-";

    /// <summary>長さを要する種類（<see cref="CanonicalTypeToken.HasLengthArgument"/> が真の種類）</summary>
    private static IReadOnlySet<string> LengthKinds { get; } =
        Enum.GetValues<CanonicalTypeKind>()
            .Where(CanonicalTypeToken.HasLengthArgument)
            .Select(kind => kind.ToString())
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>正規型を入力にする観点（境界は正規型の性質）</summary>
    private static IReadOnlySet<string> CanonicalInputFacets { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            DialectTypeFacets.Format,
            DialectTypeFacets.FormatParam,
            DialectTypeFacets.RoundTripCanonical,
            DialectTypeFacets.TokenFormat,
        };

    /// <summary>その方言の型カタログで表記を解析して種類を決める観点（方言切替は移行元の方言で解析するので含めない）</summary>
    private static IReadOnlySet<string> DialectParsedFacets { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            DialectTypeFacets.Parse,
            DialectTypeFacets.RoundTripText,
            DialectTypeFacets.Normalize,
            DialectTypeFacets.SameType,
            DialectTypeFacets.ImportSpelling,
            DialectTypeFacets.Map,
        };

    /// <summary>種類を持たない観点</summary>
    private static IReadOnlySet<string> KindlessFacets { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            DialectTypeFacets.Catalog,
            DialectTypeFacets.SafeText,
        };

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.Dialect,
            [
                new Factor(
                    Dialect,
                    [
                        .. DialectNames.All.Select(name => new Level(name)),
                        new Level(DialectTypeAxis.None),
                    ],
                    "方言（方言を持たない観点は -）"
                ),
                new Factor(
                    Kind,
                    [
                        .. Enum.GetValues<CanonicalTypeKind>()
                            .Select(value => new Level(value.ToString(), AxisMember.Of(value))),
                        new Level(Unparsed),
                        new Level(NoKind),
                    ],
                    "正規型の種類（型の表記を入力にする行は、その方言の解析の結果）"
                ),
                new Factor(
                    Facet,
                    DialectTypeFacets.All.Select(name => new Level(name)).ToList(),
                    "観点"
                ),
                new Factor(
                    Boundary,
                    DialectTypeBoundaries.All.Select(name => new Level(name)).ToList(),
                    "境界の種類"
                ),
            ],
            [new FactorGroup(Dialect, Kind, Facet), new FactorGroup(Dialect, Kind, Boundary)],
            IsValid
        );

    /// <summary>
    /// 網羅しない組の一覧（受け入れた組だけ。方言が原理的に持てない組は <see cref="IsValid"/> が外す）
    /// </summary>
    /// <remarks>
    /// 宣言のセルから期待を導く行（<c>matrix-*</c>）は、製品が宣言どおりに動くかを確かめるだけで、宣言そのものの誤りは捕まえない
    /// （同語反復になる面がある）。宣言の正しさは docs の方言の表の照合（DialectDocsTableTests）と実 DB の表（DialectTypeRealDb）が
    /// 受け持つ。マッパーの行の方言横断の表は、5 方言の独立した実装どうしの突き合わせ。
    /// </remarks>
    public static CoverageGapSet Gaps { get; } =
        CoverageGap.Set(
            EngineIds.Dialect,
            [
                CoverageGap.AcceptedTuple(
                    Scope,
                    "P3-5a",
                    "取込の入力は DB のカタログの値で、読めない引数（負の長さ・int の範囲外・全角の数字）を作る入力ではない"
                        + "（カタログは長さ・精度を数で返し、取込はそれを検証せずに表記へ組む："
                        + "src/QuickER.Provider.SqlServer/SqlServerSchemaImporter.cs:924 の FormatDataType・"
                        + "src/QuickER.Provider.Oracle/OracleSchemaImporter.cs:928 の FormatDataType。"
                        + "PostgreSQL・MySQL・SQLite はカタログの表記を正規化へ通すだけ）",
                    (Facet, DialectTypeFacets.ImportSpelling),
                    (Boundary, DialectTypeBoundaries.InvalidArg)
                ),
            ]
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.Dialect,
            "Types",
            "方言の型の表（解析・書き出しの境界・正規化・マッパー・方言切替・型トークン・型の表記の安全性）",
            Table,
            0,
            [
                "tests/QuickER.Tests/Provider/DialectTypeAdapters.cs",
                "tests/QuickER.Tests/Provider/DialectTypeCase.cs",
                "tests/QuickER.Tests/Provider/DialectTypeRunner.cs",
                "tests/QuickER.Tests/Provider/DialectTypeRows.cs",
                "tests/QuickER.Tests/Provider/DialectTypeDimensions.cs",
                "tests/QuickER.Tests/Provider/DialectTypeTableTests.cs",
            ],
            [
                "src/QuickER.Provider",
                "src/QuickER.Provider.SqlServer",
                "src/QuickER.Provider.PostgreSql",
                "src/QuickER.Provider.MySql",
                "src/QuickER.Provider.Oracle",
                "src/QuickER.Provider.Sqlite",
            ]
        );

    /// <summary>1 ケースを数える</summary>
    /// <param name="counter">数える先</param>
    /// <param name="row">行</param>
    /// <param name="dialect">方言</param>
    public static void Observe(CoverageCounter counter, DialectTypeCase row, string dialect) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Dialect] = dialect,
                [Kind] = DialectTypeRunner.ObservedKind(row, dialect),
                [Facet] = row.Facet,
                [Boundary] = row.Boundary,
            }
        );

    /// <summary>原理的に起こり得ない組み合わせのときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        assignment.TryGetValue(Dialect, out var dialect);
        assignment.TryGetValue(Kind, out var kind);
        assignment.TryGetValue(Facet, out var facet);
        assignment.TryGetValue(Boundary, out var boundary);

        // 型トークンと型の表記の安全性は方言に依らない関数で、ほかの観点は方言の部品を呼ぶ
        if (dialect is not null && facet is not null)
        {
            var neutral = DialectTypeFacets.Neutral.Contains(facet, StringComparer.Ordinal);

            if (neutral != (dialect == DialectTypeAxis.None))
            {
                return false;
            }
        }

        // 型カタログの事実と型の表記の安全性は正規型の種類を持たず、ほかの観点は必ず種類（か解析できない表記）を持つ
        if (
            kind is not null
            && facet is not null
            && (kind == NoKind) != KindlessFacets.Contains(facet)
        )
        {
            return false;
        }

        // 正規型を入力にする観点には、解析できない表記が無い
        if (kind == Unparsed && facet is not null && CanonicalInputFacets.Contains(facet))
        {
            return false;
        }

        // その方言の表記を解析して種類を決める観点では、その方言の解析が返し得ない種類は現れない
        // （アダプタの宣言は DialectTypeTableTests.ParsedKinds_MatchCatalog が型カタログの解析と照合する）
        if (
            dialect is not null
            && dialect != DialectTypeAxis.None
            && facet is not null
            && DialectParsedFacets.Contains(facet)
            && Enum.TryParse<CanonicalTypeKind>(kind, out var parsedKind)
            && !DialectTypeAxis.Get(dialect).ParsedKinds.Contains(parsedKind)
        )
        {
            return false;
        }

        // 型カタログの事実は入力を持たないので境界が無い（方言のある観点で種類を持たないのは型カタログの事実だけ）
        if (boundary is not null && boundary != DialectTypeBoundaries.None)
        {
            if (facet == DialectTypeFacets.Catalog)
            {
                return false;
            }

            if (kind == NoKind && dialect is not null && dialect != DialectTypeAxis.None)
            {
                return false;
            }
        }

        return boundary is null || IsValidBoundary(dialect, kind, facet, boundary);
    }

    /// <summary>境界の種類の規則</summary>
    private static bool IsValidBoundary(
        string? dialect,
        string? kind,
        string? facet,
        string boundary
    )
    {
        switch (boundary)
        {
            case DialectTypeBoundaries.None:
                return true;

            // 上限は書き出しの判定にだけある。上限を持つのは、その方言が上限を宣言した種類だけ
            // （アダプタの宣言は DialectTypeTableTests.WriteLimitedKinds_MatchCatalog が型カタログと照合する）
            case DialectTypeBoundaries.AtLimit:
            case DialectTypeBoundaries.OverLimit:
                if (
                    facet is not null
                    && facet != DialectTypeFacets.Format
                    && facet != DialectTypeFacets.FormatParam
                )
                {
                    return false;
                }

                if (dialect == DialectTypeAxis.None || kind is Unparsed or NoKind)
                {
                    return false;
                }

                if (dialect is not null && kind is not null)
                {
                    return DialectTypeAxis
                        .Get(dialect)
                        .WriteLimitedKinds.Contains(Enum.Parse<CanonicalTypeKind>(kind));
                }

                if (dialect is not null)
                {
                    return DialectTypeAxis.Get(dialect).WriteLimitedKinds.Count > 0;
                }

                return kind is null
                    || DialectTypeAxis.Axis.Adapters.Any(adapter =>
                        adapter.WriteLimitedKinds.Contains(Enum.Parse<CanonicalTypeKind>(kind))
                    );

            // 長さ無し・負のスケール・精度なしは正規型の値の性質（正規型を入力にする観点だけ）
            case DialectTypeBoundaries.Lengthless:
                return (facet is null || CanonicalInputFacets.Contains(facet))
                    && (kind is null || LengthKinds.Contains(kind));

            case DialectTypeBoundaries.NegativeScale:
            case DialectTypeBoundaries.Precisionless:
                return (facet is null || CanonicalInputFacets.Contains(facet))
                    && (kind is null || kind == nameof(CanonicalTypeKind.Decimal));

            // 読めない引数は型の表記の性質で、読めない表記は種類を持たない
            case DialectTypeBoundaries.InvalidArg:
                return (facet is null || !CanonicalInputFacets.Contains(facet))
                    && (kind is null || kind is Unparsed or NoKind);

            default:
                return true;
        }
    }
}
