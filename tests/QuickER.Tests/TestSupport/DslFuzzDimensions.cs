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
                    NotNesting,
                    NestingLevels.Select(level => new Level(level)).ToList(),
                    "原子を囲む NOT の段数と NOT が包む合成の種類（直上の NOT の連鎖が 1・2・3 段以上か、直上に NOT が無ければ最も近い NOT が包む AND / OR。NOT が無ければ none）"
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
                    "原子を含むクエリ（原子だけのクエリ＝原子 1 つかその NOT 1 つ・合成のクエリの戻り形・射影の非 NULL 指定）"
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
                .. UnmappedClrGaps(
                    "mysql:inmemory",
                    UnmappedInMySql,
                    "MySQL に UUID の型は無く、src/QuickER.Provider.MySql/MySqlTypeCatalog.cs は Guid を char(36) で書き、src/QuickER.Provider.MySql/MySqlCSharpTypeMapper.cs の Map は char を string へ写す（Guid へ写す型が無い）ので、Guid の列を作れない"
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

    /// <summary>列の種類を含む因子の組ごとの、指定した種類に当たる述語</summary>
    private static IEnumerable<(string Other, Func<string, string, bool> Predicate)> ClrPairs(
        DslClr[] kinds
    )
    {
        var names = kinds.Select(clr => clr.ToString()).ToHashSet(StringComparer.Ordinal);
        return new[] { Shape, Negated, UnderNot, NotNesting, Query }.Select(other =>
            (other, (Func<string, string, bool>)((clr, _) => names.Contains(clr)))
        );
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
        var queryKind =
            query.IsAtomQuery ? AtomQuery
            : query.ProjectedNonNull ? ProjectionNonNull
            : $"compound-{query.Returns}";

        foreach (
            var (atom, underNot, nesting) in AtomsWithPolarity(
                query.Condition,
                underNot: false,
                chain: 0,
                group: null
            )
        )
        {
            counter.Observe(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Clr] = atom.Column.Clr.ToString(),
                    [Shape] = ShapeOf(atom),
                    [Negated] = atom.InherentNegation ? "Yes" : "No",
                    [UnderNot] = underNot ? "Yes" : "No",
                    [NotNesting] = nesting,
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

    /// <summary>
    /// 原子と、それを囲む NOT の数が奇数か・NOT の入れ子の水準（出現順）
    /// </summary>
    /// <param name="node">節</param>
    /// <param name="underNot">ここまでの NOT の数が奇数か</param>
    /// <param name="chain">この節の直上に続く NOT の段数</param>
    /// <param name="group">最も近い NOT が包む合成の水準（NOT の直下の AND / OR を通ったとき。無ければ null）</param>
    private static IEnumerable<(DslAtom Atom, bool UnderNot, string Nesting)> AtomsWithPolarity(
        DslNode node,
        bool underNot,
        int chain,
        string? group
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
                    }
                ),
            ],
            DslNot not => AtomsWithPolarity(not.Inner, !underNot, chain + 1, group),
            DslAnd and => and.Items.SelectMany(item =>
                AtomsWithPolarity(item, underNot, 0, chain > 0 ? NestingGroupAnd : group)
            ),
            DslOr or => or.Items.SelectMany(item =>
                AtomsWithPolarity(item, underNot, 0, chain > 0 ? NestingGroupOr : group)
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
