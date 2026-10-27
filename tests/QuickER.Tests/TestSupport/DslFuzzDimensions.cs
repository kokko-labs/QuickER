using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 簡易 DSL の条件の総当たり（<see cref="DslFuzz"/>）の次元表・網羅しない組の一覧・台帳の行
/// </summary>
/// <remarks>
/// <para>
/// ケースは、実装先が結果を照合したクエリ（生成時に断られたクエリ・コンパイルの診断で外したクエリ・実装先が流さないクエリ・
/// 例外で終わったクエリを除いた「照合したクエリ」）の原子 1 つ。合成のクエリの原子も数える。観測は実装先がクエリを照合した位置
/// （<c>DslFuzzExecution</c> の照合の数を数える位置）で、クエリの構文木を読むだけ（乱数を引かない）。
/// </para>
/// <para>
/// 範囲は「方言:実装先」（例 <c>sqlite:ado</c>）。SQLite と MySQL の単位は QuickER.Tests の
/// <c>DslConditionFuzzTests</c>、SQL Server の単位は Integration の <c>DslConditionFuzzSqlServerRuntimeTests</c> が流す。
/// 網羅しない組の照合は既定の種の数のときだけ行う（SQL Server は <c>QUICKER_FUZZ_SEEDS</c> を指定しないとき＝先頭の種 1 つ）。
/// </para>
/// <para>
/// 実体は QuickER.Tests の TestSupport で、Integration も <c>DslFuzz*.cs</c> の glob で同じファイルをコンパイルする。
/// </para>
/// </remarks>
public static class DslFuzzDimensions
{
    /// <summary>列の C# 側の種類</summary>
    public const string Clr = "Clr";

    /// <summary>原子の形（演算子とオペランドの種類）</summary>
    public const string Shape = "Shape";

    /// <summary>原子自身の否定（NOT LIKE・NOT IN・IS NOT NULL など）</summary>
    public const string Negated = "Negated";

    /// <summary>原子を囲む NOT の数が奇数か</summary>
    public const string UnderNot = "UnderNot";

    /// <summary>原子を含むクエリの種類</summary>
    public const string Query = "Query";

    /// <summary>原子だけのクエリ（一覧を返す）</summary>
    public const string AtomQuery = "atom";

    /// <summary>射影の 2 つめのフィールドを非 NULL と指定するクエリ</summary>
    public const string ProjectionNonNull = "projection-nonnull";

    /// <summary>SQLite の 3 実装先と MySQL のインメモリ（QuickER.Tests）の範囲</summary>
    public static IReadOnlyList<string> SqliteMySqlScopes { get; } =
    ["sqlite:ado", "sqlite:efcore", "sqlite:inmemory", "mysql:inmemory"];

    /// <summary>SQL Server の 2 実装先（Integration）の範囲</summary>
    public static IReadOnlyList<string> SqlServerScopes { get; } =
    ["sqlserver:ado", "sqlserver:efcore"];

    /// <summary>比較の演算子（DslOp の小文字）</summary>
    private static readonly string[] CompareOps = ["eq", "ne", "lt", "le", "gt", "ge"];

    /// <summary>順序比較の演算子</summary>
    private static readonly string[] OrderedOps = ["lt", "le", "gt", "ge"];

    /// <summary>LIKE のリテラルのパターンの形（DslLike.LikeShape）</summary>
    private static readonly string[] LikeLiteralShapes = ["exact", "prefix", "suffix", "contains"];

    /// <summary>原子の形の水準</summary>
    private static IReadOnlyList<string> ShapeLevels { get; } =
    [
        .. CompareOps.SelectMany(op => new[] { $"compare-{op}-param", $"compare-{op}-literal" }),
        "null-check",
        .. LikeLiteralShapes.Select(shape => $"like-{shape}"),
        "like-param",
        "in",
        .. Enum.GetValues<DslMatchKind>()
            .SelectMany(match =>
                new[]
                {
                    $"{match.ToString().ToLowerInvariant()}-literal",
                    $"{match.ToString().ToLowerInvariant()}-param",
                }
            ),
    ];

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.Dsl,
            [
                new Factor(
                    Clr,
                    Enum.GetValues<DslClr>().Select(value => new Level(value.ToString())).ToList(),
                    "対象の列の C# 側の種類"
                ),
                new Factor(
                    Shape,
                    ShapeLevels.Select(shape => new Level(shape)).ToList(),
                    "原子の形（比較は演算子×パラメータかリテラル・LIKE はパターンの形・文字列一致はメソッド×リテラルかパラメータ）"
                ),
                new Factor(
                    Negated,
                    [new Level("No"), new Level("Yes")],
                    "原子自身の否定（NOT LIKE・NOT IN・IS NOT NULL・NOT CONTAINS など）"
                ),
                new Factor(
                    UnderNot,
                    [new Level("No"), new Level("Yes")],
                    "原子を囲む NOT の数が奇数か（エミッタとオラクルが NOT を原子まで押し下げる向き）"
                ),
                new Factor(
                    Query,
                    [
                        new Level(AtomQuery),
                        .. new[]
                        {
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        }.Select(shape => new Level($"compound-{shape}", AxisMember.Of(shape))),
                        new Level(ProjectionNonNull),
                    ],
                    "原子を含むクエリ（原子だけのクエリ・合成のクエリの戻り形・射影の非 NULL 指定）"
                ),
            ],
            isValid: IsValid
        );

    /// <summary>
    /// SQLite と SQL Server の型の対応に無い列の種類（符号なし整数と sbyte＝どちらの方言も列の型として持てない）
    /// </summary>
    private static readonly DslClr[] UnmappedInSqliteAndSqlServer =
    [
        DslClr.SByte,
        DslClr.UInt16,
        DslClr.UInt32,
        DslClr.UInt64,
    ];

    /// <summary>MySQL の型の対応に無い列の種類（MySQL に UUID の型が無く、Guid へ写す列の型が無い）</summary>
    private static readonly DslClr[] UnmappedInMySql = [DslClr.Guid];

    /// <summary>
    /// MySQL が列の型として持てるのに、MySQL の単位（DslFuzzUnits の MySqlColumns）が列を定義していない種類
    /// （MySQL の単位は符号なし整数・sbyte・byte と文字列 1 列だけを持ち、主キーの bigint は条件に使わない）
    /// </summary>
    private static readonly DslClr[] UndefinedInMySqlUnit =
    [
        DslClr.FixedString,
        DslClr.Int64,
        DslClr.Int32,
        DslClr.Int16,
        DslClr.Decimal,
        DslClr.DateTime,
        DslClr.GuidKey,
    ];

    /// <summary>網羅しない組の一覧（範囲は「方言:実装先」）</summary>
    /// <remarks>
    /// 合成のクエリの原子と戻り形の組の欠け（<c>SamplingGaps</c>）は、固定した既定の種の並びに合わせて書いている。
    /// 生成器を変えて種の指紋が変わったら、指紋と一緒にこの一覧も書き直す。
    /// </remarks>
    public static CoverageGapSet Gaps { get; } =
        CoverageGap.Set(
            EngineIds.Dsl,
            [
                .. new[] { "sqlite:ado", "sqlite:efcore", "sqlite:inmemory" }.SelectMany(scope =>
                    UnmappedClrGaps(
                            scope,
                            UnmappedInSqliteAndSqlServer,
                            "SQLite の型の対応（src/QuickER.Provider.Sqlite/SqliteCSharpTypeMapper.cs の Map）は tinyint→byte・smallint→short・int→int・bigint→long だけを持ち、unsigned を読まない（int unsigned は知らない型として string へ倒れる）ので、sbyte・ushort・uint・ulong の列を作れない"
                        )
                        .Concat(GuidKeyPrimaryKeyGaps(scope))
                ),
                .. new[] { "sqlserver:ado", "sqlserver:efcore" }.SelectMany(scope =>
                    UnmappedClrGaps(
                            scope,
                            UnmappedInSqliteAndSqlServer,
                            "SQL Server には符号つきの tinyint と、符号なしの smallint・int・bigint の型が無い（src/QuickER.Provider.SqlServer/SqlServerCSharpTypeMapper.cs の Map は tinyint→byte・smallint→short・int→int・bigint→long だけを持つ）ので、sbyte・ushort・uint・ulong の列を作れない"
                        )
                        .Concat(GuidKeyPrimaryKeyGaps(scope))
                ),
                .. UnmappedClrGaps(
                    "mysql:inmemory",
                    UnmappedInMySql,
                    "MySQL に UUID の型は無く、src/QuickER.Provider.MySql/MySqlTypeCatalog.cs は Guid を char(36) で書き、src/QuickER.Provider.MySql/MySqlCSharpTypeMapper.cs の Map は char を string へ写す（Guid へ写す型が無い）ので、Guid の列を作れない"
                ),
                .. UndefinedClrGaps(
                    "mysql:inmemory",
                    UndefinedInMySqlUnit,
                    "MySQL の単位（DslFuzzUnits の MySqlColumns）が列を定義していない（MySQL は列として持てる＝MySqlCSharpTypeMapper の Map は char(n)→string・bigint→long・int→int・smallint→short・decimal→decimal・datetime→DateTime へ写し、文字列の主キーは GuidKey にできる。単位に列を足せば作れる）"
                ),
                .. new[]
                {
                    "sqlite:ado",
                    "sqlite:efcore",
                    "sqlite:inmemory",
                    "mysql:inmemory",
                    "sqlserver:ado",
                    "sqlserver:efcore",
                }.SelectMany(ProjectionNonNullGaps),
                CoverageGap.AcceptedMatching(
                    "sqlite:efcore",
                    "P1-3",
                    "EF Core の SQLite プロバイダ（Microsoft.EntityFrameworkCore.Sqlite）は decimal のサーバー側の順序比較を翻訳できず例外にする（プロバイダの事実で、エンジンを直しても流せない）ので、DslConditionFuzzTests の ExcludedOnEfCoreSqlite が decimal の列の順序比較を含むクエリを EF Core（SQLite）で流さない",
                    Clr,
                    Shape,
                    (clr, shape) =>
                        clr == nameof(DslClr.Decimal)
                        && OrderedOps.Any(op =>
                            shape.StartsWith($"compare-{op}-", StringComparison.Ordinal)
                        )
                ),
                .. new[] { "sqlserver:ado", "sqlserver:efcore" }.Select(scope =>
                    CoverageGap.Tuple(
                        scope,
                        "P1-3",
                        "P2",
                        "合成のクエリの原子の列は乱数で選び、SQL Server の既定（先頭の種 1 つ）では byte の列（i8）の原子が一覧を返す合成のクエリにたまたま入らない（戻り形ごとに列を割り付ける生成をしていない）",
                        (Clr, nameof(DslClr.Byte)),
                        (Query, $"compound-{QueryReturnShape.List}")
                    )
                ),
                .. SamplingGaps(
                    "sqlite:ado",
                    new()
                    {
                        ["compare-ge-param"] = [QueryReturnShape.Projection],
                        ["compare-le-literal"] = [QueryReturnShape.Projection],
                        ["compare-le-param"] = [QueryReturnShape.List, QueryReturnShape.Projection],
                        ["compare-lt-literal"] = [QueryReturnShape.List],
                        ["contains-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                        ],
                        ["contains-param"] = [QueryReturnShape.Single],
                        ["endswith-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["endswith-param"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["like-contains"] = [QueryReturnShape.Count, QueryReturnShape.Projection],
                        ["like-exact"] = [QueryReturnShape.Projection],
                        ["like-param"] = [QueryReturnShape.Projection],
                        ["like-prefix"] = [QueryReturnShape.Single, QueryReturnShape.Projection],
                        ["like-suffix"] = [QueryReturnShape.List],
                        ["startswith-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                        ],
                        ["startswith-param"] = [QueryReturnShape.List, QueryReturnShape.Count],
                    }
                ),
                .. SamplingGaps(
                    "sqlite:efcore",
                    new()
                    {
                        ["compare-ge-param"] =
                        [
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["compare-gt-literal"] = [QueryReturnShape.List],
                        ["compare-le-literal"] =
                        [
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["compare-le-param"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["compare-lt-literal"] = [QueryReturnShape.List],
                        ["contains-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                        ],
                        ["contains-param"] = [QueryReturnShape.Single, QueryReturnShape.Projection],
                        ["endswith-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["endswith-param"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["like-contains"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["like-exact"] = [QueryReturnShape.Projection],
                        ["like-param"] = [QueryReturnShape.Projection],
                        ["like-prefix"] = [QueryReturnShape.Single, QueryReturnShape.Projection],
                        ["like-suffix"] = [QueryReturnShape.List],
                        ["startswith-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                        ],
                        ["startswith-param"] = [QueryReturnShape.List, QueryReturnShape.Count],
                    }
                ),
                .. SamplingGaps(
                    "sqlite:inmemory",
                    new()
                    {
                        ["compare-ge-param"] = [QueryReturnShape.Projection],
                        ["compare-le-literal"] = [QueryReturnShape.Projection],
                        ["compare-le-param"] = [QueryReturnShape.List, QueryReturnShape.Projection],
                        ["compare-lt-literal"] = [QueryReturnShape.List],
                        ["contains-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                        ],
                        ["contains-param"] = [QueryReturnShape.Single],
                        ["endswith-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["endswith-param"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["like-contains"] = [QueryReturnShape.Count, QueryReturnShape.Projection],
                        ["like-exact"] = [QueryReturnShape.Projection],
                        ["like-param"] = [QueryReturnShape.Projection],
                        ["like-prefix"] = [QueryReturnShape.Single, QueryReturnShape.Projection],
                        ["like-suffix"] = [QueryReturnShape.List],
                        ["startswith-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                        ],
                        ["startswith-param"] = [QueryReturnShape.List, QueryReturnShape.Count],
                    }
                ),
                .. SamplingGaps(
                    "mysql:inmemory",
                    new()
                    {
                        ["compare-ge-literal"] = [QueryReturnShape.List],
                        ["compare-ge-param"] = [QueryReturnShape.List],
                        ["compare-gt-literal"] = [QueryReturnShape.Single],
                        ["compare-le-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["compare-ne-param"] = [QueryReturnShape.Projection],
                        ["contains-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["contains-param"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["endswith-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                        ],
                        ["endswith-param"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Projection,
                        ],
                        ["like-contains"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["like-exact"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["like-param"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Projection,
                        ],
                        ["like-prefix"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["like-suffix"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["startswith-literal"] =
                        [
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["startswith-param"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                    }
                ),
                .. SamplingGaps(
                    "sqlserver:ado",
                    new()
                    {
                        ["compare-eq-literal"] = [QueryReturnShape.Single],
                        ["compare-ge-literal"] = [QueryReturnShape.Single],
                        ["compare-ge-param"] = [QueryReturnShape.Projection],
                        ["compare-gt-literal"] =
                        [
                            QueryReturnShape.Single,
                            QueryReturnShape.Projection,
                        ],
                        ["compare-gt-param"] = [QueryReturnShape.List],
                        ["compare-le-literal"] = [QueryReturnShape.Projection],
                        ["compare-le-param"] = [QueryReturnShape.List, QueryReturnShape.Projection],
                        ["compare-lt-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Projection,
                        ],
                        ["compare-lt-param"] = [QueryReturnShape.List, QueryReturnShape.Single],
                        ["contains-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["contains-param"] = [QueryReturnShape.Single],
                        ["endswith-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["endswith-param"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["like-contains"] =
                        [
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["like-exact"] = [QueryReturnShape.Count, QueryReturnShape.Projection],
                        ["like-param"] = [QueryReturnShape.Projection],
                        ["like-prefix"] = [QueryReturnShape.Single, QueryReturnShape.Projection],
                        ["like-suffix"] = [QueryReturnShape.List, QueryReturnShape.Single],
                        ["startswith-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                        ],
                        ["startswith-param"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                        ],
                    }
                ),
                .. SamplingGaps(
                    "sqlserver:efcore",
                    new()
                    {
                        ["compare-eq-literal"] = [QueryReturnShape.Single],
                        ["compare-ge-literal"] = [QueryReturnShape.Single],
                        ["compare-ge-param"] = [QueryReturnShape.Projection],
                        ["compare-gt-literal"] =
                        [
                            QueryReturnShape.Single,
                            QueryReturnShape.Projection,
                        ],
                        ["compare-gt-param"] = [QueryReturnShape.List],
                        ["compare-le-literal"] = [QueryReturnShape.Projection],
                        ["compare-le-param"] = [QueryReturnShape.List, QueryReturnShape.Projection],
                        ["compare-lt-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Projection,
                        ],
                        ["compare-lt-param"] = [QueryReturnShape.List, QueryReturnShape.Single],
                        ["contains-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["contains-param"] = [QueryReturnShape.Single],
                        ["endswith-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["endswith-param"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["like-contains"] =
                        [
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                            QueryReturnShape.Projection,
                        ],
                        ["like-exact"] = [QueryReturnShape.Count, QueryReturnShape.Projection],
                        ["like-param"] = [QueryReturnShape.Projection],
                        ["like-prefix"] = [QueryReturnShape.Single, QueryReturnShape.Projection],
                        ["like-suffix"] = [QueryReturnShape.List, QueryReturnShape.Single],
                        ["startswith-literal"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                        ],
                        ["startswith-param"] =
                        [
                            QueryReturnShape.List,
                            QueryReturnShape.Single,
                            QueryReturnShape.Count,
                        ],
                    }
                ),
            ]
        );

    /// <summary>台帳の行（SQLite と MySQL・QuickER.Tests）</summary>
    public static EngineDescriptor SqliteMySqlDescriptor { get; } =
        Descriptor(
            "SqliteMySql",
            "簡易 DSL の条件の総当たり（SQLite の 3 実装先・MySQL のインメモリ）",
            "tests/QuickER.Tests/CodeGen.CSharp/DslConditionFuzzTests.cs"
        );

    /// <summary>台帳の行（SQL Server・Integration）</summary>
    public static EngineDescriptor SqlServerDescriptor { get; } =
        Descriptor(
            "SqlServer",
            "簡易 DSL の条件の総当たり（SQL Server の QuickER 版 Repository と EF Core）",
            "tests/QuickER.Tests.Integration/GeneratedRuntime/DslConditionFuzzSqlServerRuntimeTests.cs"
        );

    /// <summary>
    /// 方言が列の型として持てない種類を、列の種類を含む因子の組ごとに 1 項目にする（受け入れた組＝単位を直しても作れない）
    /// </summary>
    private static IEnumerable<CoverageGapEntry> UnmappedClrGaps(
        string scope,
        DslClr[] unmapped,
        string reason
    ) =>
        ClrPairs(unmapped)
            .Select(pair =>
                CoverageGap.AcceptedMatching(scope, "P1-3", reason, Clr, pair.Other, pair.Predicate)
            );

    /// <summary>
    /// 方言は持てるのに単位が列を定義していない種類を、列の種類を含む因子の組ごとに 1 項目にする（まだ作れない組）
    /// </summary>
    private static IEnumerable<CoverageGapEntry> UndefinedClrGaps(
        string scope,
        DslClr[] undefined,
        string reason
    ) => ClrPairs(undefined).Select(pair => Gap(scope, reason, Clr, pair.Other, pair.Predicate));

    /// <summary>列の種類を含む因子の組ごとの、指定した種類に当たる述語</summary>
    private static IEnumerable<(string Other, Func<string, string, bool> Predicate)> ClrPairs(
        DslClr[] kinds
    )
    {
        var names = kinds.Select(clr => clr.ToString()).ToHashSet(StringComparer.Ordinal);
        return new[] { Shape, Negated, UnderNot, Query }.Select(other =>
            (other, (Func<string, string, bool>)((clr, _) => names.Contains(clr)))
        );
    }

    /// <summary>GuidKey の列は主キー（NOT NULL）だけなので、NULL 判定と射影の非 NULL 指定に使われない</summary>
    private static IEnumerable<CoverageGapEntry> GuidKeyPrimaryKeyGaps(string scope)
    {
        const string reason =
            "単位の GuidKey の列は sqlite-vo-guidkey・sqlserver-vo-guidkey の主キー（NOT NULL）だけで、生成器は NULL 判定を NULL を許す列にだけ作り、射影の非 NULL 指定も NULL を許す列だけに作る（GuidKey の値オブジェクトを共有する NULL 許容の外部キー列を持つ単位が無い）";

        yield return CoverageGap.Tuple(
            scope,
            "P1-3",
            "P2",
            reason,
            (Clr, nameof(DslClr.GuidKey)),
            (Shape, "null-check")
        );
        yield return CoverageGap.Tuple(
            scope,
            "P1-3",
            "P2",
            reason,
            (Clr, nameof(DslClr.GuidKey)),
            (Query, ProjectionNonNull)
        );
    }

    /// <summary>射影の非 NULL 指定のクエリは、条件がいつも IS NOT NULL の原子 1 つ</summary>
    private static IEnumerable<CoverageGapEntry> ProjectionNonNullGaps(string scope)
    {
        const string reason =
            "生成器（DslFuzzGenerator.Queries）は射影の非 NULL 指定のクエリの条件を、その列の IS NOT NULL の原子 1 つ（NOT で囲まない）だけで作る";

        yield return Gap(
            scope,
            reason,
            Shape,
            Query,
            (shape, query) => query == ProjectionNonNull && shape != "null-check"
        );
        yield return Gap(
            scope,
            reason,
            Negated,
            Query,
            (negated, query) => query == ProjectionNonNull && negated == "No"
        );
        yield return Gap(
            scope,
            reason,
            UnderNot,
            Query,
            (underNot, query) => query == ProjectionNonNull && underNot == "Yes"
        );
    }

    /// <summary>
    /// 合成のクエリの原子の形と戻り形の組のうち、既定の種で出揃わないものを、原子の形ごとに 1 項目にする
    /// </summary>
    /// <param name="scope">範囲</param>
    /// <param name="missing">原子の形ごとの、組まない戻り形</param>
    private static IEnumerable<CoverageGapEntry> SamplingGaps(
        string scope,
        Dictionary<string, QueryReturnShape[]> missing
    ) =>
        missing.Select(pair =>
        {
            var queries = pair
                .Value.Select(shape => $"compound-{shape}")
                .ToHashSet(StringComparer.Ordinal);
            var efCore =
                scope == "sqlite:efcore"
                    ? "。EF Core（SQLite）は decimal の列の順序比較を含む合成のクエリを流さないので、さらに減る"
                    : "";

            return Gap(
                scope,
                $"合成のクエリは単位ごとに 30 件で、戻り形は 4 つから乱数で選び、原子は列ごとの候補から乱数で 1 つ選ぶ（文字列の列は IN・IS NULL・比較・LIKE・文字列一致の 5 つから選び、LIKE はさらにパラメータと 4 つのパターンの形、文字列一致は 3 つのメソッド×リテラルかパラメータに分かれる）。既定の種では {pair.Key} の原子が {string.Join("・", pair.Value)} の合成のクエリにたまたま入らない（戻り形ごとに原子の形を割り付ける生成をしていない）{efCore}",
                Shape,
                Query,
                (shape, query) => shape == pair.Key && queries.Contains(query)
            );
        });

    /// <summary>網羅しない組の項目（指摘 P1-3・直す区切り P2）</summary>
    private static CoverageGapEntry Gap(
        string scope,
        string reason,
        string factorA,
        string factorB,
        Func<string, string, bool> predicate
    ) => CoverageGap.Matching(scope, "P1-3", "P2", reason, factorA, factorB, predicate);

    /// <summary>範囲の名前（「方言:実装先」）</summary>
    /// <param name="dbms">図の対象 DBMS</param>
    /// <param name="backend">実装先の名前</param>
    public static string ScopeOf(string dbms, string backend) => $"{dbms}:{backend}";

    /// <summary>照合したクエリの原子を、それぞれケースとして数える（構文木を読むだけ）</summary>
    /// <param name="counter">数える先</param>
    /// <param name="query">照合したクエリ</param>
    public static void Observe(CoverageCounter counter, DslQuery query)
    {
        var queryKind =
            query.IsAtomQuery ? AtomQuery
            : query.ProjectedNonNull ? ProjectionNonNull
            : $"compound-{query.Returns}";

        foreach (var (atom, underNot) in AtomsWithPolarity(query.Condition, false))
        {
            counter.Observe(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Clr] = atom.Column.Clr.ToString(),
                    [Shape] = ShapeOf(atom),
                    [Negated] = atom.InherentNegation ? "Yes" : "No",
                    [UnderNot] = underNot ? "Yes" : "No",
                    [Query] = queryKind,
                }
            );
        }
    }

    /// <summary>原子の形（型の検査が断るリテラルの比較は宣言していない水準＝照合したクエリに現れたら観測が断る）</summary>
    internal static string ShapeOf(DslAtom atom) =>
        atom switch
        {
            DslCompare compare =>
                $"compare-{compare.Op.ToString().ToLowerInvariant()}-{(compare.Operand is DslParam ? "param" : compare.RejectedByTypeCheck ? "literal-rejected" : "literal")}",
            DslNullCheck => "null-check",
            DslLike { Operand: DslLiteral literal } => $"like-{DslLike.LikeShape(literal.Text)}",
            DslLike => "like-param",
            DslIn => "in",
            DslMatch match =>
                $"{match.Match.ToString().ToLowerInvariant()}-{(match.Operand is DslLiteral ? "literal" : "param")}",
            _ => atom.GetType().Name,
        };

    /// <summary>原子と、それを囲む NOT の数が奇数か（出現順）</summary>
    private static IEnumerable<(DslAtom Atom, bool UnderNot)> AtomsWithPolarity(
        DslNode node,
        bool underNot
    ) =>
        node switch
        {
            DslAtom atom => [(atom, underNot)],
            DslNot not => AtomsWithPolarity(not.Inner, !underNot),
            DslAnd and => and.Items.SelectMany(item => AtomsWithPolarity(item, underNot)),
            DslOr or => or.Items.SelectMany(item => AtomsWithPolarity(item, underNot)),
            _ => throw new ArgumentOutOfRangeException(nameof(node), node.GetType().Name, null),
        };

    private static EngineDescriptor Descriptor(string part, string title, string test) =>
        new(
            EngineIds.Dsl,
            part,
            title,
            Table,
            0,
            [
                "tests/QuickER.Tests/TestSupport/DslFuzz.cs",
                "tests/QuickER.Tests/TestSupport/DslFuzzDimensions.cs",
                "tests/QuickER.Tests/TestSupport/DslFuzzGenerator.cs",
                "tests/QuickER.Tests/TestSupport/DslFuzzKnownFailures.cs",
                "tests/QuickER.Tests/TestSupport/DslFuzzUnits.cs",
                "tests/QuickER.Tests/CodeGen.CSharp/DslFuzzExecution.cs",
                "tests/QuickER.Tests/CodeGen.CSharp/DslFuzzRunner.cs",
                test,
            ],
            [
                "src/QuickER.CodeGen.CSharp/Queries",
                "src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime",
                "src/QuickER.Runtime",
                "src/QuickER.Runtime.Sqlite",
                "src/QuickER.Runtime.SqlServer",
                "src/QuickER.Runtime.EntityFrameworkCore",
                "src/QuickER.Runtime.InMemory",
            ]
        );

    /// <summary>原理的に起こり得ない組み合わせのときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    /// <remarks>
    /// 照合したクエリは生成時の型の検査（<c>QueryConditionTypeChecker</c>）を通ったものだけなので、型の検査が必ず断る
    /// 組み合わせは照合したクエリに現れない。NULL 判定は列ごとの NULL 許容で決まり、型では決まらないので規則にしない。
    /// </remarks>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        assignment.TryGetValue(Clr, out var clr);
        assignment.TryGetValue(Shape, out var shape);
        assignment.TryGetValue(Negated, out var negated);

        // 比較の原子は自身の否定を持たない（否定の向きは <> の演算子か、外側の NOT で書く）
        if (shape is not null && shape.StartsWith("compare-", StringComparison.Ordinal))
        {
            if (negated == "Yes")
            {
                return false;
            }
        }

        if (shape is null || clr is null)
        {
            return true;
        }

        var kind = Enum.Parse<DslClr>(clr);
        var isString = kind is DslClr.String or DslClr.FixedString or DslClr.GuidKey;
        var isOrdered =
            kind
            is DslClr.Int64
                or DslClr.Int32
                or DslClr.Int16
                or DslClr.Byte
                or DslClr.SByte
                or DslClr.UInt16
                or DslClr.UInt32
                or DslClr.UInt64
                or DslClr.Decimal
                or DslClr.DateTime;

        // 順序比較は順序を持つ型（整数・decimal・日時）だけ（型の検査が文字列・Guid・GuidKey の順序比較を断る＝OrderedOperatorUnsupported）
        if (OrderedOps.Any(op => shape.StartsWith($"compare-{op}-", StringComparison.Ordinal)))
        {
            if (!isOrdered)
            {
                return false;
            }
        }

        // 日時と Guid はリテラルを書けない（型の検査は文字列リテラルを文字列の列だけ・数値リテラルを数値の列だけに許す）
        if (
            shape.EndsWith("-literal", StringComparison.Ordinal)
            && shape.StartsWith("compare-", StringComparison.Ordinal)
            && kind is DslClr.DateTime or DslClr.Guid
        )
        {
            return false;
        }

        var isLike = shape.StartsWith("like-", StringComparison.Ordinal);
        var isMatch = Enum.GetValues<DslMatchKind>()
            .Any(match =>
                shape.StartsWith(
                    $"{match.ToString().ToLowerInvariant()}-",
                    StringComparison.Ordinal
                )
            );

        // 文字列一致（LIKE・CONTAINS 系）は文字列の列だけ（型の検査が StringMatchUnsupported で断る）
        if ((isLike || isMatch) && !isString)
        {
            return false;
        }

        // GuidKey の値オブジェクトは部分一致のメソッドを持たないので、ワイルドカードの無い LIKE（完全一致）以外の文字列一致を
        // 型の検査が断る（LIKE @p も部分一致）
        if (kind == DslClr.GuidKey && (isMatch || (isLike && shape != "like-exact")))
        {
            return false;
        }

        return true;
    }
}

/// <summary>簡易 DSL の総当たりの網羅を範囲（「方言:実装先」）ごとに数える</summary>
/// <param name="scopes">このテストが受け持つ範囲（観測した範囲がここに無ければ構成の誤りとして断る）</param>
public sealed class DslFuzzCoverage(IReadOnlyList<string> scopes)
{
    private readonly Dictionary<string, CoverageCounter> _counters = scopes.ToDictionary(
        scope => scope,
        _ => new CoverageCounter(DslFuzzDimensions.Table),
        StringComparer.Ordinal
    );

    /// <summary>実装先が照合したクエリを数える</summary>
    /// <param name="dbms">図の対象 DBMS</param>
    /// <param name="backend">実装先の名前</param>
    /// <param name="query">照合したクエリ</param>
    public void Observe(string dbms, string backend, DslQuery query)
    {
        var scope = DslFuzzDimensions.ScopeOf(dbms, backend);

        if (!_counters.TryGetValue(scope, out var counter))
        {
            throw new InvalidOperationException(
                $"網羅の範囲 {scope} を受け持っていない（受け持つ範囲: {string.Join(", ", _counters.Keys)}）"
            );
        }

        DslFuzzDimensions.Observe(counter, query);
    }

    /// <summary>範囲ごとの結果（宣言の誤りがあれば、ここで宣言の誤りとして投げる）</summary>
    public IReadOnlyList<(string Scope, CoverageReport Report)> Reports() =>
        scopes.Select(scope => (scope, _counters[scope].Report())).ToList();

    /// <summary>範囲ごとの要約と、網羅しない組の一覧の当たり方をテストの出力へ書く</summary>
    /// <param name="reports"><see cref="Reports"/> の結果</param>
    public static void WriteSummaries(IReadOnlyList<(string Scope, CoverageReport Report)> reports)
    {
        foreach (var (scope, report) in reports)
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"[Dsl] 範囲 {scope}");
            report.WriteSummary();
            report.WriteGapUsage(DslFuzzDimensions.Gaps, scope);
        }
    }

    /// <summary>範囲ごとに網羅しない組を照合する（表明の失敗はすべて並べ、宣言の誤りはそのまま投げる）</summary>
    /// <param name="reports"><see cref="Reports"/> の結果</param>
    /// <param name="plan">流した種（null なら常に照合する）</param>
    public static void AssertGaps(
        IReadOnlyList<(string Scope, CoverageReport Report)> reports,
        SeedPlan? plan
    )
    {
        var problems = new List<string>();

        foreach (var (scope, report) in reports)
        {
            try
            {
                report.AssertGaps(DslFuzzDimensions.Gaps, scope, plan);
            }
            catch (XunitException ex)
            {
                problems.Add(ex.Message);
            }
        }

        if (problems.Count > 0)
        {
            throw new XunitException(
                string.Join(Environment.NewLine + Environment.NewLine, problems)
            );
        }
    }
}
