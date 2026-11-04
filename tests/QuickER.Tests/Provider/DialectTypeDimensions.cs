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
    /// 網羅しない組の一覧（すべて「行がまだ無いだけ」で、行を足せば埋まる。方言が原理的に持てない組は <see cref="IsValid"/> が外す）
    /// </summary>
    public static CoverageGapSet Gaps { get; } = CoverageGap.Set(EngineIds.Dialect, PendingGaps());

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

    /// <summary>網羅しない組を見つけた区切り</summary>
    private const string Finding = "P3-5a";

    /// <summary>網羅しない組を埋める区切り</summary>
    private const string FixedIn = "P4";

    /// <summary>観点ごとの、行がまだ無い理由（P3-5a は元の単体テストの入力だけを写し、新しい入力の行を足していない）</summary>
    private static string FacetReason(string facet) =>
        facet switch
        {
            DialectTypeFacets.Parse =>
                "解析の行は元のテストの表記だけ（全種類の代表値は書き出しと往復の観点が数える）。種類ごとの表記の行を足せば埋まる",
            DialectTypeFacets.FormatParam =>
                "パラメータの型の書き出しの行は、列の上限を外す MySQL・PostgreSQL の元のテストの入力だけ（ほかの方言は TryFormat と同じ既定の判定）。種類ごとの行を足せば埋まる",
            DialectTypeFacets.RoundTripText =>
                "表記の往復の行は元のテストの解析できる表記だけ。解析できない表記と種類ごとの行を足せば埋まる",
            DialectTypeFacets.Normalize =>
                "正規化の行は、方言ごとに取込と綴りが食い違った指摘の表記だけ（SQLite は恒等で元のテストが無い）。種類ごとの行を足せば埋まる",
            DialectTypeFacets.SameType =>
                "同じ型の判定の行は、同期の能力の結線の代表の組だけ。種類ごとの綴りの組の行を足せば埋まる",
            DialectTypeFacets.ImportSpelling =>
                "取込の表記の行は、元のテストの不動点の入力（SQL Server・Oracle・PostgreSQL）だけ。種類ごとの DB のカタログの値の行を足せば埋まる",
            DialectTypeFacets.Map =>
                "マッパーの行は、元のテストが CLR 型や印を確かめた表記だけ。種類ごとの行を足せば埋まる",
            DialectTypeFacets.Convert =>
                "方言切替の行は、元のテストの代表の型（主に SQL Server との往復）だけ。移行元の種類ごとの行を足せば埋まる",
            DialectTypeFacets.ParamType =>
                "パラメータの型解決の行は、列の上限を超えるトークン（MySQL・PostgreSQL）と大文字小文字（SQL Server）だけ。種類ごとのトークンの行を足せば埋まる",
            DialectTypeFacets.TokenParse =>
                "型トークンの解析の行は読めないトークンだけ（読めるトークンは書き出しの往復の行が通る）。種類ごとのトークンの行を足せば埋まる",
            _ => throw new ArgumentOutOfRangeException(
                nameof(facet),
                facet,
                "網羅しない組の理由が無い観点"
            ),
        };

    /// <summary>境界の、行がまだ無い理由</summary>
    private static string BoundaryReason(string facet, string boundary) =>
        boundary == DialectTypeBoundaries.InvalidArg
            ? "読めない引数の行は、元のテストの解析・マッパー・正規化・安全性の観点の入力だけ。この観点の行を足せば埋まる"
            : $"境界 {boundary} の行は、元のテストの書き出し（format）の観点の入力だけ（{facet} の観点の元のテストに無い）。この観点の行を足せば埋まる";

    private static IEnumerable<CoverageGapEntry> PendingGaps()
    {
        foreach (var (dialect, facet) in PendingDialectFacets)
        {
            yield return CoverageGap.Tuple(
                Scope,
                Finding,
                FixedIn,
                FacetReason(facet),
                (Dialect, dialect),
                (Facet, facet)
            );
        }

        foreach (var (facet, kinds) in PendingKindFacets)
        {
            foreach (var kind in kinds.Split(' '))
            {
                yield return CoverageGap.Tuple(
                    Scope,
                    Finding,
                    FixedIn,
                    FacetReason(facet),
                    (Kind, kind),
                    (Facet, facet)
                );
            }
        }

        foreach (var (facet, boundary) in PendingFacetBoundaries)
        {
            yield return CoverageGap.Tuple(
                Scope,
                Finding,
                FixedIn,
                BoundaryReason(facet, boundary),
                (Facet, facet),
                (Boundary, boundary)
            );
        }

        foreach (var (facet, dialect, kinds) in PendingDialectKindFacets)
        {
            foreach (var kind in kinds.Split(' '))
            {
                yield return CoverageGap.Tuple(
                    Scope,
                    Finding,
                    FixedIn,
                    FacetReason(facet),
                    (Dialect, dialect),
                    (Kind, kind),
                    (Facet, facet)
                );
            }
        }

        foreach (var (dialect, boundary, kinds) in PendingDialectKindBoundaries)
        {
            foreach (var kind in kinds.Split(' '))
            {
                yield return CoverageGap.Tuple(
                    Scope,
                    Finding,
                    FixedIn,
                    "型トークンの長さ無しの行は string だけ（元のテストの入力）。長さを要するほかの種類の行を足せば埋まる",
                    (Dialect, dialect),
                    (Kind, kind),
                    (Boundary, boundary)
                );
            }
        }
    }

    private static (string Dialect, string Facet)[] PendingDialectFacets =>
        [
            ("sqlserver", "format-param"),
            ("oracle", "format-param"),
            ("sqlite", "format-param"),
            ("sqlite", "same-type"),
            ("mysql", "import-spelling"),
            ("sqlite", "import-spelling"),
            ("oracle", "param-type"),
            ("sqlite", "param-type"),
        ];

    private static (string Facet, string Kinds)[] PendingKindFacets =>
        [
            (
                "format-param",
                "Boolean TinyInt SmallInt Int32 Int64 Float32 Float64 Money AnsiString AnsiFixedString Date Guid Xml Json RowVersion"
            ),
            ("roundtrip-text", "unparsed"),
            (
                "normalize",
                "TinyInt SmallInt Int64 Money FixedString AnsiFixedString Binary FixedBinary Date Guid Json"
            ),
            (
                "same-type",
                "Boolean TinyInt SmallInt Int32 Int64 Float32 Float64 Money String AnsiString FixedString AnsiFixedString Binary FixedBinary Date Time DateTimeOffset Guid Xml Json RowVersion unparsed"
            ),
            (
                "import-spelling",
                "Boolean TinyInt SmallInt Int64 Float32 Money AnsiString FixedString AnsiFixedString Binary FixedBinary Date Guid Xml Json unparsed"
            ),
            ("map", "Json"),
            (
                "param-type",
                "Boolean TinyInt SmallInt Int32 Int64 Float32 Float64 Money AnsiString AnsiFixedString Date Time DateTimeOffset Guid Xml Json RowVersion"
            ),
            (
                "token-parse",
                "Boolean TinyInt SmallInt Int32 Int64 Decimal Float32 Float64 Money String AnsiString FixedString AnsiFixedString Binary FixedBinary Date Time DateTime DateTimeOffset Guid Xml Json RowVersion"
            ),
        ];

    private static (string Facet, string Boundary)[] PendingFacetBoundaries =>
        [
            ("format-param", "at-limit"),
            ("roundtrip-text", "invalid-arg"),
            ("roundtrip-canonical", "lengthless"),
            ("roundtrip-canonical", "negative-scale"),
            ("roundtrip-canonical", "precisionless"),
            ("same-type", "invalid-arg"),
            ("import-spelling", "invalid-arg"),
            ("convert", "invalid-arg"),
            ("param-type", "invalid-arg"),
        ];

    private static (string Facet, string Dialect, string Kinds)[] PendingDialectKindFacets =>
        [
            ("parse", "sqlserver", "FixedString AnsiFixedString FixedBinary"),
            (
                "format-param",
                "sqlserver",
                "Boolean TinyInt SmallInt Int32 Int64 Decimal Float32 Float64 Money String AnsiString FixedString AnsiFixedString Binary FixedBinary Date Time DateTime DateTimeOffset Guid Xml Json RowVersion"
            ),
            (
                "format-param",
                "postgresql",
                "Boolean TinyInt SmallInt Int32 Int64 Float32 Float64 Money AnsiString AnsiFixedString Binary FixedBinary Date Guid Xml Json RowVersion"
            ),
            (
                "format-param",
                "mysql",
                "Boolean TinyInt SmallInt Int32 Int64 Float32 Float64 Money AnsiString AnsiFixedString Date Time DateTimeOffset Guid Xml Json RowVersion"
            ),
            (
                "format-param",
                "oracle",
                "Boolean TinyInt SmallInt Int32 Int64 Decimal Float32 Float64 Money String AnsiString FixedString AnsiFixedString Binary FixedBinary Date Time DateTime DateTimeOffset Guid Xml Json RowVersion"
            ),
            (
                "format-param",
                "sqlite",
                "Boolean TinyInt SmallInt Int32 Int64 Decimal Float32 Float64 Money String AnsiString FixedString AnsiFixedString Binary FixedBinary Date Time DateTime DateTimeOffset Guid Xml Json RowVersion"
            ),
            ("roundtrip-text", "sqlserver", "unparsed"),
            ("roundtrip-text", "postgresql", "unparsed"),
            ("roundtrip-text", "mysql", "unparsed"),
            ("roundtrip-text", "oracle", "unparsed"),
            ("roundtrip-text", "sqlite", "unparsed"),
            (
                "normalize",
                "sqlserver",
                "Boolean TinyInt SmallInt Int64 Money AnsiString FixedString AnsiFixedString Binary FixedBinary Date Guid Xml"
            ),
            (
                "normalize",
                "postgresql",
                "Boolean SmallInt Int64 Float32 Float64 Money FixedString Binary Date Guid Xml Json"
            ),
            (
                "normalize",
                "mysql",
                "TinyInt SmallInt Int64 Float32 Float64 FixedString Binary FixedBinary Date Json"
            ),
            (
                "normalize",
                "oracle",
                "Boolean TinyInt SmallInt Int64 Float32 String FixedString AnsiFixedString Binary"
            ),
            (
                "normalize",
                "sqlite",
                "Boolean TinyInt SmallInt Int32 Int64 Decimal Float64 Money String AnsiString FixedString AnsiFixedString Binary FixedBinary Date Time DateTime DateTimeOffset Guid Xml Json"
            ),
            (
                "same-type",
                "sqlserver",
                "Boolean TinyInt SmallInt Int32 Int64 Float32 Float64 Money String AnsiString FixedString AnsiFixedString Binary FixedBinary Date Time DateTime DateTimeOffset Guid Xml RowVersion unparsed"
            ),
            (
                "same-type",
                "postgresql",
                "Boolean SmallInt Int32 Int64 Float32 Float64 Money String FixedString Binary Date Time DateTimeOffset Guid Xml Json unparsed"
            ),
            (
                "same-type",
                "mysql",
                "Boolean TinyInt SmallInt Int32 Int64 Float32 Float64 String FixedString Binary FixedBinary Date Time DateTime DateTimeOffset Json unparsed"
            ),
            (
                "same-type",
                "oracle",
                "Boolean TinyInt SmallInt Int32 Int64 Decimal Float32 Float64 String AnsiString FixedString AnsiFixedString Binary DateTimeOffset Xml unparsed"
            ),
            (
                "same-type",
                "sqlite",
                "Boolean TinyInt SmallInt Int32 Int64 Decimal Float64 Money String AnsiString FixedString AnsiFixedString Binary FixedBinary Date Time DateTime DateTimeOffset Guid Xml Json unparsed"
            ),
            (
                "import-spelling",
                "sqlserver",
                "Boolean TinyInt SmallInt Int32 Int64 Float32 Float64 Money AnsiString FixedString AnsiFixedString Binary FixedBinary Date DateTimeOffset Guid Xml unparsed"
            ),
            (
                "import-spelling",
                "postgresql",
                "Boolean SmallInt Int32 Int64 Float32 Float64 Money FixedString Binary Date Time DateTimeOffset Guid Xml Json unparsed"
            ),
            (
                "import-spelling",
                "mysql",
                "Boolean TinyInt SmallInt Int32 Int64 Decimal Float32 Float64 String FixedString Binary FixedBinary Date Time DateTime DateTimeOffset Json unparsed"
            ),
            (
                "import-spelling",
                "oracle",
                "Boolean TinyInt SmallInt Int64 Float32 String AnsiString FixedString AnsiFixedString Binary DateTime Xml unparsed"
            ),
            (
                "import-spelling",
                "sqlite",
                "Boolean TinyInt SmallInt Int32 Int64 Decimal Float64 Money String AnsiString FixedString AnsiFixedString Binary FixedBinary Date Time DateTime DateTimeOffset Guid Xml Json unparsed"
            ),
            (
                "map",
                "postgresql",
                "Boolean SmallInt Int64 Decimal Float32 Float64 Money FixedString Date Guid Xml Json"
            ),
            ("map", "mysql", "FixedString Date Time DateTime DateTimeOffset Json"),
            ("map", "oracle", "TinyInt SmallInt Float32 Float64 String FixedString Xml"),
            (
                "map",
                "sqlite",
                "Boolean TinyInt SmallInt Int64 Decimal Money FixedString Date Time DateTime DateTimeOffset Guid Xml Json"
            ),
            (
                "convert",
                "sqlserver",
                "TinyInt Money FixedString AnsiFixedString FixedBinary Date Time Xml RowVersion"
            ),
            (
                "convert",
                "postgresql",
                "TinyInt SmallInt Int64 Float32 Float64 Money AnsiString FixedString AnsiFixedString FixedBinary Date Time Xml Json RowVersion"
            ),
            (
                "convert",
                "mysql",
                "TinyInt SmallInt Int64 Float32 Float64 Money AnsiString FixedString AnsiFixedString FixedBinary Date Time Xml Json RowVersion"
            ),
            (
                "convert",
                "oracle",
                "Decimal Float32 Float64 Money FixedString AnsiFixedString FixedBinary Date DateTimeOffset Xml Json RowVersion"
            ),
            ("convert", "sqlite", "Json"),
            (
                "param-type",
                "sqlserver",
                "Boolean TinyInt SmallInt Int32 Int64 Decimal Float32 Float64 Money AnsiString FixedString AnsiFixedString Binary FixedBinary Date Time DateTime DateTimeOffset Guid Xml Json RowVersion"
            ),
            (
                "param-type",
                "postgresql",
                "Boolean TinyInt SmallInt Int32 Int64 Float32 Float64 Money AnsiString FixedString AnsiFixedString Binary FixedBinary Date Time DateTimeOffset Guid Xml Json RowVersion unparsed"
            ),
            (
                "param-type",
                "mysql",
                "Boolean TinyInt SmallInt Int32 Int64 Decimal Float32 Float64 Money AnsiString AnsiFixedString Date Time DateTime DateTimeOffset Guid Xml Json RowVersion unparsed"
            ),
            (
                "param-type",
                "oracle",
                "Boolean TinyInt SmallInt Int32 Int64 Decimal Float32 Float64 Money String AnsiString FixedString AnsiFixedString Binary FixedBinary Date Time DateTime DateTimeOffset Guid Xml Json RowVersion unparsed"
            ),
            (
                "param-type",
                "sqlite",
                "Boolean TinyInt SmallInt Int32 Int64 Decimal Float32 Float64 Money String AnsiString FixedString AnsiFixedString Binary FixedBinary Date Time DateTime DateTimeOffset Guid Xml Json RowVersion unparsed"
            ),
            (
                "token-parse",
                "-",
                "Boolean TinyInt SmallInt Int32 Int64 Decimal Float32 Float64 Money String AnsiString FixedString AnsiFixedString Binary FixedBinary Date Time DateTime DateTimeOffset Guid Xml Json RowVersion"
            ),
        ];

    private static (string Dialect, string Boundary, string Kinds)[] PendingDialectKindBoundaries =>
        [("-", "lengthless", "AnsiString FixedString AnsiFixedString Binary FixedBinary")];

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
