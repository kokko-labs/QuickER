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

    /// <summary>原子を囲む NOT の段数と、NOT が包む合成の種類</summary>
    public const string NotNesting = "NotNesting";

    /// <summary>NOT に囲まれない原子</summary>
    public const string NestingNone = "none";

    /// <summary>直上の NOT が 1 段（<c>NOT a</c>）</summary>
    public const string NestingSingle = "single";

    /// <summary>直上の NOT が 2 段（<c>NOT NOT a</c>）</summary>
    public const string NestingDouble = "double";

    /// <summary>直上の NOT が 3 段以上（<c>NOT NOT NOT a</c>）</summary>
    public const string NestingTriple = "triple";

    /// <summary>直上に NOT が無く、最も近い NOT が AND を包む（<c>NOT (a AND b)</c>）</summary>
    public const string NestingGroupAnd = "group-and";

    /// <summary>直上に NOT が無く、最も近い NOT が OR を包む（<c>NOT (a OR b)</c>）</summary>
    public const string NestingGroupOr = "group-or";

    /// <summary>NOT の入れ子の水準（並びは網羅の単位のクエリの名前の番号に使う）</summary>
    public static IReadOnlyList<string> NestingLevels { get; } =
    [NestingNone, NestingSingle, NestingDouble, NestingTriple, NestingGroupAnd, NestingGroupOr];

    /// <summary>優先順位が括弧の代わりをし得る組の描き方</summary>
    public const string Grouping = "Grouping";

    /// <summary>祖先に優先順位が括弧の代わりをし得る組（OR の項目の AND・AND / OR の項目の NOT）が無い原子</summary>
    public const string GroupingNone = "none";

    /// <summary>その組を括弧で書いた原子（<see cref="DslGrouping.Explicit"/>）</summary>
    public const string GroupingParenthesized = "parenthesized";

    /// <summary>その組の括弧を省き、優先順位で読ませた原子（<see cref="DslGrouping.Minimal"/>）</summary>
    public const string GroupingPrecedence = "precedence";

    /// <summary>原子の文字列のリテラルの水準（none・plain・escaped）</summary>
    public const string Quote = "Quote";

    /// <summary>キーワードと不等号の綴り（<see cref="DslSpelling"/> の小文字）</summary>
    public const string Spelling = "Spelling";

    /// <summary>原子を含むクエリの種類</summary>
    public const string Query = "Query";

    /// <summary>原子だけのクエリ（一覧を返す）</summary>
    public const string AtomQuery = "atom";

    /// <summary>射影の 2 つめのフィールドを非 NULL と指定するクエリ</summary>
    public const string ProjectionNonNull = "projection-nonnull";

    /// <summary>原子のパラメータを、同じクエリのほかの原子と使い回すか（使い回す相手の列と型）</summary>
    public const string ParameterReuse = "ParameterReuse";

    /// <summary>パラメータを使い回さない原子（リテラル・NULL 判定を含む）</summary>
    public const string ReuseNone = "none";

    /// <summary>同じ列のほかの原子と同じパラメータを使う</summary>
    public const string ReuseSameColumn = "same-column";

    /// <summary>ほかの列の原子と同じパラメータを使い、パラメータの型がこの原子の列と同じ種類</summary>
    public const string ReuseOtherColumnSameType = "other-column-same-type";

    /// <summary>ほかの列の原子と同じパラメータを使い、パラメータの型がこの原子の列へ暗黙に広がる（int と bigint・decimal など）</summary>
    public const string ReuseOtherColumnWidening = "other-column-widening";

    /// <summary>同じ列のほかの IN と同じリストを使う</summary>
    public const string ReuseListSameColumn = "in-list-same-column";

    /// <summary>ほかの列の IN と同じリストを使う</summary>
    public const string ReuseListOtherColumn = "in-list-other-column";

    /// <summary>パラメータの使い回しの水準</summary>
    public static IReadOnlyList<string> ReuseLevels { get; } =
    [
        ReuseNone,
        ReuseSameColumn,
        ReuseOtherColumnSameType,
        ReuseOtherColumnWidening,
        ReuseListSameColumn,
        ReuseListOtherColumn,
    ];

    /// <summary>原子を含むクエリの並び替え</summary>
    public const string Ordering = "Ordering";

    /// <summary>並び替えを持たない（件数・並べない一覧・原子だけのクエリ）</summary>
    public const string OrderingNone = "none";

    /// <summary>主キーだけで並べる</summary>
    public const string OrderingKey = "key";

    /// <summary>列 1 つで並べてから主キーで並べる</summary>
    public const string OrderingOneColumn = "one-column-then-key";

    /// <summary>列 2 つで並べてから主キーで並べる</summary>
    public const string OrderingTwoColumns = "two-columns-then-key";

    /// <summary>並び替えの水準</summary>
    public static IReadOnlyList<string> OrderingLevels { get; } =
    [OrderingNone, OrderingKey, OrderingOneColumn, OrderingTwoColumns];

    /// <summary>原子が比べる値と、列の型の合い方</summary>
    public const string ValueFit = "ValueFit";

    /// <summary>比べる値を持たない原子（NULL 判定・空のリスト・null だけのリスト）</summary>
    public const string FitNone = "none";

    /// <summary>比べる値がどれも列の型に入る</summary>
    public const string FitFits = "fits";

    /// <summary>小数の桁が列のスケールを超える値がある（整数の列はスケール 0）</summary>
    public const string FitBeyondScale = "beyond-scale";

    /// <summary>整数部が列の型の範囲を超える値がある（decimal の列は精度−スケールの桁・整数の列は型の範囲）</summary>
    public const string FitBeyondRange = "beyond-range";

    /// <summary>Unicode でない文字列の列に、ASCII の外の文字を含む値がある</summary>
    public const string FitBeyondCharset = "beyond-charset";

    /// <summary>値と列の型の合い方の水準</summary>
    public static IReadOnlyList<string> FitLevels { get; } =
    [FitNone, FitFits, FitBeyondScale, FitBeyondRange, FitBeyondCharset];

    /// <summary>SQLite の 3 実装先と MySQL のインメモリ（QuickER.Tests）の範囲</summary>
    public static IReadOnlyList<string> SqliteMySqlScopes { get; } =
    ["sqlite:ado", "sqlite:efcore", "sqlite:inmemory", "mysql:inmemory"];

    /// <summary>SQL Server の 2 実装先（Integration）の範囲</summary>
    public static IReadOnlyList<string> SqlServerScopes { get; } =
    ["sqlserver:ado", "sqlserver:efcore"];

    /// <summary>比較の演算子（DslOp の小文字）</summary>
    private static readonly string[] CompareOps = ["eq", "ne", "lt", "le", "gt", "ge"];

    /// <summary>整数の列と小数のリテラルの比較で作る演算子（x.5 の値と整数で結果の違う 3 つ）</summary>
    public static IReadOnlyList<DslOp> FractionalOps { get; } = [DslOp.Eq, DslOp.Lt, DslOp.Ge];

    /// <summary>順序比較の演算子</summary>
    private static readonly string[] OrderedOps = ["lt", "le", "gt", "ge"];

    /// <summary>LIKE のリテラルのパターンの形（DslLike.LikeShape）</summary>
    private static readonly string[] LikeLiteralShapes = ["exact", "prefix", "suffix", "contains"];

    /// <summary>原子の形の水準</summary>
    private static IReadOnlyList<string> ShapeLevels { get; } =
    [
        .. CompareOps.SelectMany(op => new[] { $"compare-{op}-param", $"compare-{op}-literal" }),
        .. FractionalOps.Select(op =>
            $"compare-{op.ToString().ToLowerInvariant()}-literal-fractional"
        ),
        "null-check",
        .. LikeLiteralShapes.Select(shape => $"like-{shape}"),
        "like-param",
        .. DslIn.ListShapes.Select(list => list == DslIn.ListValues ? "in" : $"in-{list}"),
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
                    "原子の形（比較は演算子×パラメータかリテラル・LIKE はパターンの形・IN はリストの形〔値・空・null を含む・null だけ〕・文字列一致はメソッド×リテラルかパラメータ）"
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
                    NotNesting,
                    NestingLevels.Select(level => new Level(level)).ToList(),
                    "原子を囲む NOT の段数と NOT が包む合成の種類（直上の NOT の連鎖が 1・2・3 段以上か、直上に NOT が無ければ最も近い NOT が包む AND / OR。NOT が無ければ none）"
                ),
                new Factor(
                    Grouping,
                    [
                        new Level(GroupingNone),
                        new Level(GroupingParenthesized),
                        new Level(GroupingPrecedence),
                    ],
                    "原子の祖先の、優先順位が括弧の代わりをし得る組（OR の項目の AND・AND / OR の項目の NOT）の描き方（組が無い・括弧で書いた・括弧を省いて優先順位で読ませた）"
                ),
                new Factor(
                    Quote,
                    [new Level("none"), new Level("plain"), new Level("escaped")],
                    "原子の文字列のリテラル（持たない・' を含まない・' を含む＝DSL では '' と書く）"
                ),
                new Factor(
                    Spelling,
                    Enum.GetValues<DslSpelling>()
                        .Select(value => new Level(value.ToString().ToLowerInvariant()))
                        .ToList(),
                    "キーワードと不等号の綴り（大文字と <>・小文字と !=・先頭だけ大文字と <>）"
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
                    "原子を含むクエリ（原子だけのクエリ＝原子 1 つかその NOT 1 つで並べない一覧・合成のクエリの戻り形・射影の非 NULL 指定）"
                ),
                new Factor(
                    ParameterReuse,
                    ReuseLevels.Select(level => new Level(level)).ToList(),
                    "原子のパラメータを同じクエリのほかの原子と使い回すか（使い回さない・同じ列・ほかの列で型が同じ・ほかの列で型が広がる・IN のリストを同じ列・IN のリストをほかの列）"
                ),
                new Factor(
                    Ordering,
                    OrderingLevels.Select(level => new Level(level)).ToList(),
                    "原子を含むクエリの並び替え（並べない・主キーだけ・列 1 つの後に主キー・列 2 つの後に主キー）"
                ),
                new Factor(
                    ValueFit,
                    FitLevels.Select(level => new Level(level)).ToList(),
                    "原子が比べる値（リテラル・パラメータ・リストの要素）と列の型の合い方（値を持たない・入る・小数の桁がスケールの外・整数部が範囲の外・Unicode でない列に ASCII の外の文字）"
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

    /// <summary>網羅しない組の一覧（範囲は「方言:実装先」）</summary>
    /// <remarks>
    /// 残るのは受け入れた組（方言が列の型として持てない種類・EF Core の SQLite プロバイダが翻訳できない比較）だけ。
    /// 既定の種で出揃わない組・種の単位が作らない組（NULL を許す GuidKey の列・MySQL の単位に無い列の種類・射影の非 NULL 指定の
    /// 条件の形・合成のクエリの原子の形と戻り形・NOT の入れ子の 2 段以上と合成を包む NOT）は、網羅の単位
    /// （<see cref="DslFuzzUnits.Coverage"/>）が種の前に埋める。
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
                ),
                .. new[] { "sqlserver:ado", "sqlserver:efcore" }.SelectMany(scope =>
                    UnmappedClrGaps(
                        scope,
                        UnmappedInSqliteAndSqlServer,
                        "SQL Server には符号つきの tinyint と、符号なしの smallint・int・bigint の型が無い（src/QuickER.Provider.SqlServer/SqlServerCSharpTypeMapper.cs の Map は tinyint→byte・smallint→short・int→int・bigint→long だけを持つ）ので、sbyte・ushort・uint・ulong の列を作れない"
                    )
                ),
                .. new[]
                {
                    "sqlite:ado",
                    "sqlite:efcore",
                    "sqlite:inmemory",
                    "mysql:inmemory",
                }.SelectMany(scope => CharsetUnavailableGaps(scope)),
                .. SqliteMySqlScopes
                    .Concat(SqlServerScopes)
                    .Select(scope =>
                        CoverageGap.Matching(
                            scope,
                            "EP",
                            ReuseOutOfTypeFixedIn,
                            "パラメータを使い回す形は、型が広がる組で相手の列の型に入る値だけを選ぶ（DslFuzzGenerator.SharedPool）ので、使い回すパラメータの値が列の小数の桁・整数部の外になる形を作らない",
                            ParameterReuse,
                            ValueFit,
                            (reuse, fit) =>
                                reuse != ReuseNone && fit is FitBeyondScale or FitBeyondRange
                        )
                    ),
                .. SqlServerScopes.Select(scope =>
                    CoverageGap.Tuple(
                        scope,
                        "EP",
                        "きっかけ待ち（Unicode でない列の値オブジェクトの単位）",
                        "GuidKey の列は値オブジェクトの単位にしか無く、Unicode でない文字列の列の単位（sqlserver-ansi・coverage-sqlserver-ansi）は値オブジェクトを持たない（varchar(36) の GuidKey の主キーを作っていない）",
                        (Clr, nameof(DslClr.GuidKey)),
                        (ValueFit, FitBeyondCharset)
                    )
                ),
                CoverageGap.AcceptedMatching(
                    "sqlite:efcore",
                    "EP",
                    "EF Core の SQLite プロバイダは decimal のサーバー側の順序比較を翻訳できない（DslConditionFuzzTests の ExcludedOnEfCoreSqlite が流さない）。小数の桁・整数部が列の型の外のリテラル（decimal の値）の順序比較は decimal の列にしか書けない（整数の列と小数のリテラルは形 compare-*-literal-fractional）ので、EF Core（SQLite）では作れない",
                    Shape,
                    ValueFit,
                    (shape, fit) =>
                        fit is FitBeyondScale or FitBeyondRange
                        && OrderedOps.Any(op => shape == $"compare-{op}-literal")
                ),
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
    /// 使い回すパラメータの値が列の型の外になる形を作る区切り（使い回しでない形は ValueFit の水準が作る）
    /// </summary>
    private const string ReuseOutOfTypeFixedIn =
        "きっかけ待ち（簡易 DSL の使い回しと列の型の外の値）";

    /// <summary>
    /// 列の文字集合の外の文字（<see cref="FitBeyondCharset"/>）を、ValueFit を含む因子の組ごとに 1 項目にする（受け入れた組＝
    /// SQLite・MySQL の列は Unicode で文字を持つので、単位を直しても作れない）
    /// </summary>
    private static IEnumerable<CoverageGapEntry> CharsetUnavailableGaps(string scope) =>
        Table
            .Factors.Select(factor => factor.Name)
            .Where(name => name != ValueFit)
            .Select(other =>
                CoverageGap.AcceptedMatching(
                    scope,
                    "EP",
                    "SQLite は varchar・char の列も文字列を Unicode のまま持ち、インメモリは .NET の文字列で持つ（MySQL の構成はインメモリだけ）ので、列の文字集合の外の文字は無い（照合順序のコードページで持つのは SQL Server の varchar・char だけで、Integration の DslConditionFuzzSqlServerRuntimeTests の単位 sqlserver-ansi が流す）",
                    other,
                    ValueFit,
                    (_, fit) => fit == FitBeyondCharset
                )
            );

    /// <summary>列の種類を含む因子の組ごとの、指定した種類に当たる述語</summary>
    private static IEnumerable<(string Other, Func<string, string, bool> Predicate)> ClrPairs(
        DslClr[] kinds
    )
    {
        var names = kinds.Select(clr => clr.ToString()).ToHashSet(StringComparer.Ordinal);
        return new[]
        {
            Shape,
            Negated,
            UnderNot,
            NotNesting,
            Grouping,
            Quote,
            Spelling,
            Query,
            ParameterReuse,
            Ordering,
            ValueFit,
        }.Select(other => (other, (Func<string, string, bool>)((clr, _) => names.Contains(clr))));
    }

    /// <summary>範囲の名前（「方言:実装先」）</summary>
    /// <param name="dbms">図の対象 DBMS</param>
    /// <param name="backend">実装先の名前</param>
    public static string ScopeOf(string dbms, string backend) => $"{dbms}:{backend}";

    /// <summary>照合したクエリの原子を、それぞれケースとして数える（構文木を読むだけ）</summary>
    /// <param name="counter">数える先</param>
    /// <param name="query">照合したクエリ</param>
    public static void Observe(CoverageCounter counter, DslQuery query)
    {
        foreach (var assignment in Assignments(query))
        {
            counter.Observe(assignment);
        }
    }

    /// <summary>クエリの原子ごとの割り当て（<see cref="Observe"/> が数える値。網羅の単位の生成器も組の選び方に使う）</summary>
    /// <param name="query">クエリ</param>
    public static IEnumerable<IReadOnlyDictionary<string, string>> Assignments(DslQuery query)
    {
        var queryKind =
            query.IsAtomQuery ? AtomQuery
            : query.ProjectedNonNull ? ProjectionNonNull
            : $"compound-{query.Returns}";
        var atoms = query.Atoms.ToList();
        var ordering = OrderingOf(query);

        foreach (
            var (atom, underNot, nesting, precedenceEdge) in AtomsWithPolarity(
                query.Condition,
                underNot: false,
                chain: 0,
                group: null,
                precedenceEdge: false
            )
        )
        {
            yield return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Clr] = atom.Column.Clr.ToString(),
                [Shape] = ShapeOf(atom),
                [Negated] = atom.InherentNegation ? "Yes" : "No",
                [UnderNot] = underNot ? "Yes" : "No",
                [NotNesting] = nesting,
                [Grouping] =
                    !precedenceEdge ? GroupingNone
                    : query.Grouping == DslGrouping.Minimal ? GroupingPrecedence
                    : GroupingParenthesized,
                [Quote] = DslFuzz.QuoteOf(atom),
                [Spelling] = query.Spelling.ToString().ToLowerInvariant(),
                [Query] = queryKind,
                [ParameterReuse] = ReuseOf(atom, atoms),
                [Ordering] = ordering,
                [ValueFit] = ValueFitOf(atom),
            };
        }
    }

    /// <summary>
    /// 原子が比べる値と列の型の合い方（値ごとに判定し、範囲の外 → スケールの外 → 文字の外 → 入るの順に強いものを採る）
    /// </summary>
    /// <param name="atom">原子</param>
    internal static string ValueFitOf(DslAtom atom)
    {
        IEnumerable<object?> values = atom switch
        {
            DslCompare { Operand: DslLiteral literal } => [literal.Value],
            DslLike { Operand: DslLiteral literal } => [literal.Value],
            DslMatch { Operand: DslLiteral literal } => [literal.Value],
            _ => DslFuzz.ParameterOf(atom)?.Values ?? [],
        };
        var fits = values.OfType<object>().Select(value => FitOf(value, atom.Column)).ToList();

        return fits.Count == 0 ? FitNone
            : fits.Contains(FitBeyondRange) ? FitBeyondRange
            : fits.Contains(FitBeyondScale) ? FitBeyondScale
            : fits.Contains(FitBeyondCharset) ? FitBeyondCharset
            : FitFits;
    }

    /// <summary>値 1 つと列の型の合い方（数値の列は値を decimal にして、文字列の列は Unicode でない列だけを見る）</summary>
    private static string FitOf(object value, DslColumn column)
    {
        if (column.IsString)
        {
            return column.NonUnicode && value is string text && !text.All(char.IsAscii)
                ? FitBeyondCharset
                : FitFits;
        }

        (decimal Min, decimal Max)? range = column.Clr switch
        {
            DslClr.Int64 => (long.MinValue, long.MaxValue),
            DslClr.Int32 => (int.MinValue, int.MaxValue),
            DslClr.Int16 => (short.MinValue, short.MaxValue),
            DslClr.SByte => (sbyte.MinValue, sbyte.MaxValue),
            DslClr.Byte => (byte.MinValue, byte.MaxValue),
            DslClr.UInt16 => (ushort.MinValue, ushort.MaxValue),
            DslClr.UInt32 => (uint.MinValue, uint.MaxValue),
            DslClr.UInt64 => (0m, ulong.MaxValue),
            _ => null,
        };
        var facets =
            column.Clr == DslClr.Decimal ? DslFuzzGenerator.DecimalFacets(column.DataType) : null;

        if (range is null && facets is null)
        {
            return FitFits;
        }

        // 型の検査が断るリテラル（整数の範囲の外）は綴りのまま持つので、数として読めなければ範囲の外
        if (!TryNumber(value, out var number))
        {
            return FitBeyondRange;
        }

        var (scale, outOfRange) = facets is var (precision, digits)
            ? (
                digits,
                Math.Abs(decimal.Truncate(number)) >= DslFuzzGenerator.Pow10(precision - digits)
            )
            : (
                0,
                decimal.Truncate(number) < range!.Value.Min
                    || decimal.Truncate(number) > range.Value.Max
            );

        return outOfRange ? FitBeyondRange
            : decimal.Round(number, scale) != number ? FitBeyondScale
            : FitFits;
    }

    /// <summary>数値の値（文字列は綴りを読む）を decimal にする</summary>
    private static bool TryNumber(object value, out decimal number)
    {
        if (value is string text)
        {
            return decimal.TryParse(
                text,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out number
            );
        }

        number = Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>クエリの並び替えの水準</summary>
    internal static string OrderingOf(DslQuery query) =>
        !query.IsOrdered
            ? OrderingNone
            : query.Orderings.Count switch
            {
                0 => OrderingKey,
                1 => OrderingOneColumn,
                _ => OrderingTwoColumns,
            };

    /// <summary>
    /// 原子のパラメータの使い回しの水準（同じパラメータ名をほかの原子が参照するか・その相手の列・パラメータの型とこの原子の列の種類）
    /// </summary>
    /// <param name="atom">原子</param>
    /// <param name="atoms">同じクエリの原子（出現順。同じ原子のインスタンスを 2 回置かない）</param>
    internal static string ReuseOf(DslAtom atom, IReadOnlyList<DslAtom> atoms)
    {
        if (DslFuzz.ParameterOf(atom) is not { } parameter)
        {
            return ReuseNone;
        }

        var others = atoms
            .Where(other =>
                !ReferenceEquals(other, atom) && DslFuzz.ParameterOf(other)?.Name == parameter.Name
            )
            .ToList();

        if (others.Count == 0)
        {
            return ReuseNone;
        }

        var sameColumn = others.Any(other => other.Column.Name == atom.Column.Name);

        if (parameter.IsList)
        {
            return sameColumn ? ReuseListSameColumn : ReuseListOtherColumn;
        }

        return sameColumn ? ReuseSameColumn
            : parameter.Column.Clr == atom.Column.Clr ? ReuseOtherColumnSameType
            : ReuseOtherColumnWidening;
    }

    /// <summary>原子の形（型の検査が断るリテラルの比較は宣言していない水準＝照合したクエリに現れたら観測が断る）</summary>
    internal static string ShapeOf(DslAtom atom) =>
        atom switch
        {
            DslCompare compare =>
                $"compare-{compare.Op.ToString().ToLowerInvariant()}-{(compare.Operand is DslParam ? "param" : compare.RejectedByTypeCheck ? "literal-rejected" : compare.Fractional ? "literal-fractional" : "literal")}",
            DslNullCheck => "null-check",
            DslLike { Operand: DslLiteral literal } => $"like-{DslLike.LikeShape(literal.Text)}",
            DslLike => "like-param",
            DslIn @in => @in.ShapeName,
            DslMatch match =>
                $"{match.Match.ToString().ToLowerInvariant()}-{(match.Operand is DslLiteral ? "literal" : "param")}",
            _ => atom.GetType().Name,
        };

    /// <summary>
    /// 原子と、それを囲む NOT の数が奇数か・NOT の入れ子の水準（出現順）
    /// </summary>
    /// <param name="node">節</param>
    /// <param name="underNot">ここまでの NOT の数が奇数か</param>
    /// <param name="chain">この節の直上に続く NOT の段数</param>
    /// <param name="group">最も近い NOT が包む合成の水準（NOT の直下の AND / OR を通ったとき。無ければ null）</param>
    /// <param name="precedenceEdge">ここまでの祖先に、優先順位が括弧の代わりをし得る組（<see cref="DslFuzz.IsPrecedenceEdge"/>）があるか</param>
    private static IEnumerable<(
        DslAtom Atom,
        bool UnderNot,
        string Nesting,
        bool PrecedenceEdge
    )> AtomsWithPolarity(
        DslNode node,
        bool underNot,
        int chain,
        string? group,
        bool precedenceEdge
    ) =>
        node switch
        {
            DslAtom atom =>
            [
                (
                    atom,
                    underNot,
                    chain switch
                    {
                        0 => group ?? NestingNone,
                        1 => NestingSingle,
                        2 => NestingDouble,
                        _ => NestingTriple,
                    },
                    precedenceEdge
                ),
            ],
            DslNot not => AtomsWithPolarity(not.Inner, !underNot, chain + 1, group, precedenceEdge),
            DslAnd and => and.Items.SelectMany(item =>
                AtomsWithPolarity(
                    item,
                    underNot,
                    0,
                    chain > 0 ? NestingGroupAnd : group,
                    precedenceEdge || DslFuzz.IsPrecedenceEdge(and, item)
                )
            ),
            DslOr or => or.Items.SelectMany(item =>
                AtomsWithPolarity(
                    item,
                    underNot,
                    0,
                    chain > 0 ? NestingGroupOr : group,
                    precedenceEdge || DslFuzz.IsPrecedenceEdge(or, item)
                )
            ),
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
    /// NOT の入れ子は、NOT が無ければ NOT の数が偶数であることと、原子だけのクエリ（定義が原子 1 つかその NOT 1 つ）が
    /// 2 段以上の NOT・合成を包む NOT を持たないことだけを規則にする。
    /// </remarks>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        assignment.TryGetValue(Clr, out var clr);
        assignment.TryGetValue(Shape, out var shape);
        assignment.TryGetValue(Negated, out var negated);
        assignment.TryGetValue(NotNesting, out var nesting);
        assignment.TryGetValue(UnderNot, out var underNot);
        assignment.TryGetValue(Query, out var queryKind);
        assignment.TryGetValue(Grouping, out var grouping);

        if (!IsValidReuseAndOrdering(assignment) || !IsValidValueFit(assignment))
        {
            return false;
        }

        // 原子だけのクエリ（原子 1 つかその NOT 1 つ）は AND / OR を持たないので、優先順位が括弧の代わりをし得る組を持たない
        if (queryKind == AtomQuery && grouping is not (null or GroupingNone))
        {
            return false;
        }

        // NOT に囲まれない原子を囲む NOT の数は 0（偶数）
        if (nesting == NestingNone && underNot == "Yes")
        {
            return false;
        }

        // 原子だけのクエリは原子 1 つかその NOT 1 つ（クエリの種類の定義）なので、NOT の 2 段以上と合成を包む NOT を持たない
        if (queryKind == AtomQuery && nesting is not (null or NestingNone or NestingSingle))
        {
            return false;
        }

        // 比較の原子は自身の否定を持たない（否定の向きは <> の演算子か、外側の NOT で書く）
        if (shape is not null && shape.StartsWith("compare-", StringComparison.Ordinal))
        {
            if (negated == "Yes")
            {
                return false;
            }
        }

        assignment.TryGetValue(Quote, out var quote);
        var hasQuote = quote is not (null or "none");

        if (shape is not null)
        {
            var stringLiteralOnly =
                shape is "like-exact" or "like-prefix" or "like-suffix" or "like-contains"
                || (
                    !shape.StartsWith("compare-", StringComparison.Ordinal)
                    && shape.EndsWith("-literal", StringComparison.Ordinal)
                );
            var mayCarryStringLiteral =
                stringLiteralOnly || shape.EndsWith("-literal", StringComparison.Ordinal);

            // 文字列のリテラルの水準は形の定義で決まる：パラメータ・IN・NULL 判定・小数のリテラルは持たず、
            // LIKE のリテラルと文字列一致のリテラルは必ず持つ
            if ((hasQuote && !mayCarryStringLiteral) || (quote == "none" && stringLiteralOnly))
            {
                return false;
            }

            // 順序比較は文字列の列に書けない（型の検査が断る）ので、文字列のリテラルを持たない
            if (
                hasQuote
                && OrderedOps.Any(op =>
                    shape.StartsWith($"compare-{op}-", StringComparison.Ordinal)
                )
            )
            {
                return false;
            }
        }

        if (clr is not null)
        {
            var clrKind = Enum.Parse<DslClr>(clr);

            // 文字列のリテラルを書けるのは文字列の列だけ（型の検査が数値・日時・Guid の列への文字列リテラルを断る）
            if (hasQuote && clrKind is not (DslClr.String or DslClr.FixedString or DslClr.GuidKey))
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

        // 文字列の列の比較のリテラルは文字列のリテラル
        if (
            isString
            && quote == "none"
            && shape.StartsWith("compare-", StringComparison.Ordinal)
            && shape.EndsWith("-literal", StringComparison.Ordinal)
        )
        {
            return false;
        }

        // 小数のリテラルの形は「整数の列と小数のリテラルの比較」の定義なので、整数の列だけ
        if (
            shape.EndsWith("-literal-fractional", StringComparison.Ordinal)
            && kind
                is not (
                    DslClr.Int64
                    or DslClr.Int32
                    or DslClr.Int16
                    or DslClr.Byte
                    or DslClr.SByte
                    or DslClr.UInt16
                    or DslClr.UInt32
                    or DslClr.UInt64
                )
        )
        {
            return false;
        }
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

    /// <summary>
    /// 型が暗黙に広がる先になる列の種類（C# の暗黙の数値変換で、より狭い <see cref="DslClr"/> の値を受け取れる種類）
    /// </summary>
    private static readonly string[] WideningTargets =
    [
        nameof(DslClr.Int16),
        nameof(DslClr.Int32),
        nameof(DslClr.Int64),
        nameof(DslClr.UInt16),
        nameof(DslClr.UInt32),
        nameof(DslClr.UInt64),
        nameof(DslClr.Decimal),
    ];

    /// <summary>値と列の型の合い方の水準が、ほかの因子と原理的に組めるか（判断に要る因子が無ければ真）</summary>
    private static bool IsValidValueFit(IReadOnlyDictionary<string, string> assignment)
    {
        if (!assignment.TryGetValue(ValueFit, out var fit))
        {
            return true;
        }

        assignment.TryGetValue(Shape, out var shape);
        assignment.TryGetValue(Clr, out var clr);
        assignment.TryGetValue(Quote, out var quote);
        assignment.TryGetValue(ParameterReuse, out var reuse);
        var numeric = fit is FitBeyondScale or FitBeyondRange;

        if (shape is not null)
        {
            // 比べる値を持たないのは NULL 判定・空のリスト・null だけのリストだけ（形の定義）
            var valueless = shape is "null-check" or "in-empty" or "in-all-null";

            if ((fit == FitNone) != valueless)
            {
                return false;
            }

            // 小数のリテラルの形は整数の列と小数の値の比較なので、値は必ず整数の列の型に入らない
            if (shape.EndsWith("-literal-fractional", StringComparison.Ordinal) && !numeric)
            {
                return false;
            }

            // 文字列一致は文字列の列だけ（型の検査）なので、数値の外れ方を持たない
            var stringMatch =
                shape.StartsWith("like-", StringComparison.Ordinal)
                || Enum.GetValues<DslMatchKind>()
                    .Any(match =>
                        shape.StartsWith(
                            $"{match.ToString().ToLowerInvariant()}-",
                            StringComparison.Ordinal
                        )
                    );

            // 数値の列の IN のリストで null を持てるのは値オブジェクトのリストだけ（値型の要素は null を持てない）で、値オブジェクトの
            // Create は列の型に入らない値を検証エラーにする（docs の値オブジェクトの節）
            if ((stringMatch || shape == "in-with-null") && numeric)
            {
                return false;
            }

            // 順序比較は文字列の列に書けない（型の検査）ので、文字の外れ方を持たない
            if (
                fit == FitBeyondCharset
                && OrderedOps.Any(op =>
                    shape.StartsWith($"compare-{op}-", StringComparison.Ordinal)
                )
            )
            {
                return false;
            }
        }

        // 数値の外れ方は数値の列だけ、文字の外れ方は文字列の列だけ（水準の定義）
        if (clr is not null)
        {
            var kind = Enum.Parse<DslClr>(clr);
            var isNumber =
                kind
                is DslClr.Int64
                    or DslClr.Int32
                    or DslClr.Int16
                    or DslClr.Byte
                    or DslClr.SByte
                    or DslClr.UInt16
                    or DslClr.UInt32
                    or DslClr.UInt64
                    or DslClr.Decimal;
            var isText = kind is DslClr.String or DslClr.FixedString or DslClr.GuidKey;

            if ((numeric && !isNumber) || (fit == FitBeyondCharset && !isText))
            {
                return false;
            }
        }

        // 文字列のリテラルは値（文字列）を持ち、数値の列の値ではない
        if (quote is not (null or "none") && (fit == FitNone || numeric))
        {
            return false;
        }

        // 値のパラメータは値を 1 つ持ち、型が広がる列は数値の列
        if (
            (
                reuse is ReuseSameColumn or ReuseOtherColumnSameType or ReuseOtherColumnWidening
                && fit == FitNone
            ) || (reuse == ReuseOtherColumnWidening && fit == FitBeyondCharset)
        )
        {
            return false;
        }

        return true;
    }

    /// <summary>パラメータの使い回しと並び替えの水準が、ほかの因子と原理的に組めるか（判断に要る因子が無ければ真）</summary>
    private static bool IsValidReuseAndOrdering(IReadOnlyDictionary<string, string> assignment)
    {
        assignment.TryGetValue(ParameterReuse, out var reuse);
        assignment.TryGetValue(Ordering, out var ordering);
        assignment.TryGetValue(Query, out var queryKind);
        assignment.TryGetValue(Shape, out var shape);
        assignment.TryGetValue(Quote, out var quote);
        assignment.TryGetValue(Negated, out var negated);
        assignment.TryGetValue(Clr, out var clr);

        // 件数は並べない（並び順を指定できる戻り形は一覧・単一・射影＝QueryDefinitionRules.SupportsOrderBy）。単一・射影は
        // いつも主キーで並べる。原子だけのクエリは並べない一覧（クエリの種類の定義）
        if (ordering is not null && queryKind is not null)
        {
            var ordered = ordering != OrderingNone;

            if (
                (queryKind is AtomQuery or "compound-Count" && ordered)
                || (
                    queryKind is "compound-Single" or "compound-Projection" or ProjectionNonNull
                    && !ordered
                )
            )
            {
                return false;
            }
        }

        if (reuse is null or ReuseNone)
        {
            return true;
        }

        // 使い回すには原子が 2 つ以上要るので、原子だけのクエリ（原子 1 つかその NOT 1 つ）には無い
        if (queryKind == AtomQuery)
        {
            return false;
        }

        // パラメータは文字列のリテラルを持たない
        if (quote is not (null or "none"))
        {
            return false;
        }

        var isList = reuse is ReuseListSameColumn or ReuseListOtherColumn;

        if (shape is not null)
        {
            // リストを使い回すのは IN だけ、値のパラメータを使い回すのはパラメータを持つ比較・LIKE・文字列一致だけ。
            // 型が広がるのは数値の比較だけ（文字列一致は文字列の列にしか書けず、文字列の型は広がらない）
            var isIn = shape.StartsWith("in", StringComparison.Ordinal);
            var isParam = shape.EndsWith("-param", StringComparison.Ordinal);

            if (
                (isList && !isIn)
                || (!isList && !isParam)
                || (
                    reuse == ReuseOtherColumnWidening
                    && !shape.StartsWith("compare-", StringComparison.Ordinal)
                )
            )
            {
                return false;
            }
        }

        // 型が広がる原子は比較で、比較は自身の否定を持たない
        if (reuse == ReuseOtherColumnWidening && negated == "Yes")
        {
            return false;
        }

        // 型が広がるのは、より狭い種類の値を暗黙に受け取れる数値の列だけ（文字列・GuidKey・Guid・日時と、byte・sbyte は受け取れない）
        if (reuse == ReuseOtherColumnWidening && clr is not null && !WideningTargets.Contains(clr))
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

        lock (_checked)
        {
            _checked.Add((scope, query));
        }
    }

    /// <summary>照合したクエリ（範囲ごと・クエリの参照で引く）</summary>
    private readonly HashSet<(string Scope, DslQuery Query)> _checked = [];

    /// <summary>
    /// 網羅の単位のクエリのうち、構成のどれかの実装先で照合したクエリにならなかったもの（生成時・コンパイルで外れた・
    /// 実装先が流さない）を名指しする
    /// </summary>
    /// <param name="unit">網羅の単位</param>
    /// <param name="dbms">図の対象 DBMS</param>
    /// <param name="backends">構成の実装先の名前</param>
    public IEnumerable<string> UncheckedProblems(
        DslFuzzUnit unit,
        string dbms,
        IEnumerable<string> backends
    )
    {
        foreach (var backend in backends)
        {
            var scope = DslFuzzDimensions.ScopeOf(dbms, backend);
            // 型の検査が断ると決まっている原子（範囲外のリテラル・GuidKey への部分一致）のクエリは、断られることを
            // DslFuzzRunner が別に表明する（照合したクエリにならないのが正しい）
            var missing = unit
                .Queries.Where(query =>
                    !query.Atoms.Any(atom => atom.IsRejectedByTypeCheck)
                    && !_checked.Contains((scope, query))
                )
                .Select(query => query.Name)
                .ToList();

            if (missing.Count > 0)
            {
                yield return $"coverage unit {unit.Name}: {scope} did not check [{string.Join(", ", missing)}] "
                    + "(rejected at generation or compilation, or not run by the backend: the coverage queries must all be checked)";
            }
        }
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
