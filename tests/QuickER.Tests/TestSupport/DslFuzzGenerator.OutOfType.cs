using QuickER.Model;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 列の型に入らない値と比べる原子（種の単位の <c>X{i}</c> と、網羅の単位の <c>KX*</c>）
/// </summary>
/// <remarks>
/// <para>
/// 作る形（次元表の因子 ValueFit の beyond-scale・beyond-range・beyond-charset）：
/// decimal の列と、小数の桁がスケールを超える値・整数部が精度−スケールの桁を超える値（パラメータは列で型付けするものと、広い型トークン
/// 〔<c>decimal(18,6)</c>・<c>int64</c>〕で型付けするもの・リテラル・列で型付けした IN のリスト）。NULL を許す byte・short・int の列と、
/// 小数の値・<c>decimal(18,2)</c> の整数部に入らない値（decimal の型トークンで型付けしたパラメータ）。NULL を許す整数の列と、整数の型の
/// 範囲の外の小数のリテラル。Unicode でない文字列の列（<see cref="DslColumn.NonUnicode"/>）と、ASCII の外の文字を含む値（比較・LIKE・
/// 文字列一致のリテラルとパラメータ、IN のリスト、パラメータの使い回し）。比較の演算子は 6 つ全て。
/// </para>
/// <para>
/// 値は、列の型へ丸めたり置き換えたりすると行の値に重なるものを選ぶ。小数は丸めると下がる値（<c>2.254</c> → <c>2.25</c>・
/// <c>1.004</c> → <c>1.00</c>＝等値だけでなく順序比較の答えも変わる）、ASCII の外の文字は <c>?</c> に化けた行の値。
/// 値オブジェクトの単位では作らない（値オブジェクトの <c>Create</c> が精度・スケールの外の値を検証エラーにし、整数を包む値オブジェクトの
/// 列は decimal のパラメータを型の検査が断る）。ほかの作らない形は <see cref="DslFuzzNotGenerated"/> に宣言する。
/// </para>
/// <para>
/// 種の単位では、本流と使い回し・並び替えの列と別の乱数の列（<see cref="OutOfTypeSeed"/>）の生成器のインスタンスで作り、パラメータ名の
/// 接頭辞も分ける（<see cref="OutOfTypeParameterPrefix"/>）。
/// </para>
/// </remarks>
public sealed partial class DslFuzzGenerator
{
    /// <summary>列の型に入らない値の原子の乱数の列の種（本流・使い回しと並び替えの列と別にする）</summary>
    public static int OutOfTypeSeed(int seed) => unchecked(seed * 6007 + 15485863);

    /// <summary>列の型に入らない値のパラメータ名の接頭辞（本流の <c>p</c>・使い回しの <c>q</c> と重ねない）</summary>
    public const string OutOfTypeParameterPrefix = "r";

    /// <summary>
    /// <c>decimal(10,2)</c> の列のスケールの外の値（スケールへ丸めると下がって、行の値の池の 2.25・0.01・10・-1.5・1.5 に重なる）
    /// </summary>
    private static readonly object[] BeyondScaleDecimals =
    [
        2.254m,
        0.0149m,
        10.004m,
        -1.4951m,
        1.5049m,
    ];

    /// <summary><c>decimal(10,2)</c> の列の整数部の桁の外の値（列で型付けする）</summary>
    private static readonly object[] BeyondRangeDecimals =
    [
        100000000m,
        -123456789.5m,
        1000000000000m,
    ];

    /// <summary><c>decimal(10,2)</c> の列の整数部の桁の外の値（<c>int64</c> で型付けする）</summary>
    private static readonly object[] BeyondRangeLongs = [long.MaxValue, long.MinValue, 100000000L];

    /// <summary>
    /// SQL Server の money・smallmoney の列の小数 4 桁の外の値（4 桁へ丸めると行の値の池の 2.25・0.01・10・-1.5・1.5 に重なる）
    /// </summary>
    private static readonly object[] BeyondScaleMoney =
    [
        2.25004m,
        0.01004m,
        10.00004m,
        -1.50004m,
        1.49996m,
    ];

    /// <summary>SQL Server の money の列の範囲の外の値（列で型付けする）</summary>
    private static readonly object[] BeyondRangeMoney =
    [
        922_337_203_685_478m,
        -922_337_203_685_478m,
        1_000_000_000_000_000m,
    ];

    /// <summary>SQL Server の money の列の範囲の外の値（<c>int64</c> で型付けする）</summary>
    private static readonly object[] BeyondRangeMoneyLongs =
    [
        long.MaxValue,
        long.MinValue,
        1_000_000_000_000_000L,
    ];

    /// <summary>SQL Server の smallmoney の列の範囲の外の値（列で型付けする）</summary>
    private static readonly object[] BeyondRangeSmallMoney = [214_749m, -214_749m, 1_000_000m];

    /// <summary>SQL Server の smallmoney の列の範囲の外の値（<c>int64</c> で型付けする）</summary>
    private static readonly object[] BeyondRangeSmallMoneyLongs =
    [
        214_749L,
        -214_749L,
        long.MaxValue,
    ];

    /// <summary>
    /// decimal の列と比べる、列の型に入らない値の池（スケールの外・範囲の外・<c>int64</c> で型付けする範囲の外）。money・smallmoney の列は
    /// その型の小数 4 桁と範囲、ほかは <c>decimal(10,2)</c> の列の池
    /// </summary>
    private static (
        object[] BeyondScale,
        object[] BeyondRange,
        object[] BeyondRangeLongs
    ) OutOfTypePools(DslColumn column) =>
        column.Money == DslMoneyType.Money
            ? (BeyondScaleMoney, BeyondRangeMoney, BeyondRangeMoneyLongs)
        : column.Money == DslMoneyType.SmallMoney
            ? (BeyondScaleMoney, BeyondRangeSmallMoney, BeyondRangeSmallMoneyLongs)
        : (BeyondScaleDecimals, BeyondRangeDecimals, BeyondRangeLongs);

    /// <summary>
    /// 整数の列と比べる小数（<c>decimal(18,2)</c> へ丸めると下がって、行の値の池の 1・0・-1・5 に重なる）
    /// </summary>
    private static readonly object[] BeyondScaleFractions = [1.004m, 0.0001m, -0.9951m, 5.0049m];

    /// <summary>整数の列と比べる、整数の型と <c>decimal(18,2)</c> の整数部に入らない値</summary>
    private static readonly object[] BeyondRangeFractions =
    [
        10000000000000000000m,
        -10000000000000000000m,
        12345678901234567.5m,
    ];

    /// <summary>
    /// Unicode でない文字列の列と比べる、ASCII の外の文字を含む値（列の値の池のうち ASCII の外の文字を含むもの。LIKE のパターンの芯にも
    /// するので、列の値の池は空でなく % と _ を含まない値を、<c>'</c> を含むものと含まないものの両方持つこと）
    /// </summary>
    private static List<string> NonAsciiValuesOf(DslColumn column) =>
        DslFuzz
            .ValuePool(column)
            .OfType<string>()
            .Where(value => !value.All(char.IsAscii))
            .ToList();

    /// <summary>種の単位の末尾に足す原子のクエリ <c>X{i}</c>（綴りは番号で回す）</summary>
    /// <remarks>
    /// 形は網羅の単位（<see cref="OutOfTypeCoverageQueries"/>）より絞る：decimal の列はリテラルの 6 つの演算子 × スケールの外・整数部の外と
    /// IN・NOT IN（EF Core〔SQLite〕が流さない decimal の列の順序比較は、網羅の単位の SQLite の単位が作らないので種の単位が持つ）、
    /// 整数の列は decimal の型トークンのパラメータの &lt; と &gt;=（小数）と =（範囲の外）、Unicode でない文字列の列は網羅の単位と同じ全ての形。
    /// </remarks>
    /// <param name="columns">列（先頭が主キー）</param>
    public List<DslQuery> OutOfTypeQueries(IReadOnlyList<DslColumn> columns) =>
        OutOfTypeAtoms(columns, decimalOrdered: true, bothQuotes: false, seedSubset: true)
            .Select((atom, i) => AtomQuery($"X{i}", atom, SpellingAt(i)))
            .ToList();

    /// <summary>
    /// 列の型に入らない値の原子 1 つずつのクエリ（網羅の単位の全ての形。結果が赤になったとき、どの経路〔列で型付けしたパラメータ・
    /// 広い型トークン・リテラル・IN / NOT IN〕の原子かを 1 つのクエリで読めるようにする）
    /// </summary>
    /// <param name="columns">列（先頭が主キー）</param>
    /// <param name="prefix">クエリ名の接頭辞</param>
    public List<DslQuery> OutOfTypeAtomQueries(IReadOnlyList<DslColumn> columns, string prefix) =>
        OutOfTypeAtoms(columns, decimalOrdered: true, bothQuotes: false, seedSubset: false)
            .Select((atom, i) => AtomQuery($"{prefix}{i}", atom, SpellingAt(i)))
            .ToList();

    /// <summary>
    /// 網羅の単位の、列の型に入らない値の原子を使う合成のクエリ <c>KX*</c>（戻り形ごと・射影の非 NULL 指定・NOT の入れ子の全水準と、
    /// 優先順位で括弧を省いた描画の写し）。原子は形・自身の否定・列の種類・文字列のリテラルと、値と列の型の合い方の組を足すものを選ぶ
    /// </summary>
    /// <param name="columns">列（先頭が主キー）</param>
    /// <param name="decimalOrdered">
    /// decimal の列の順序比較も作るか（SQLite の単位は作らない：EF Core〔SQLite〕が流さず〔<c>DslConditionFuzzTests</c> の
    /// ExcludedOnEfCoreSqlite〕、網羅の単位のクエリはどの実装先でも照合したクエリでなければならないため。その形は種の単位の
    /// <see cref="OutOfTypeQueries"/> が作る）
    /// </param>
    public List<DslQuery> OutOfTypeCoverageQueries(
        IReadOnlyList<DslColumn> columns,
        bool decimalOrdered
    )
    {
        var forms = OutOfTypeAtoms(columns, decimalOrdered, bothQuotes: true, seedSubset: false)
            .ToList();

        return forms.Count == 0
            ? []
            :
            [
                .. WithCompoundCoverageQueries([], forms, columns, "KX", withFit: true),
                .. NonAsciiReuseQueries(columns),
            ];
    }

    /// <summary>
    /// Unicode でない文字列の列で、ASCII の外の文字を含むパラメータを使い回すクエリ <c>KXR0</c>（同じ列の比較と CONTAINS・同じ列の IN と
    /// NOT IN・同じ種類のほかの Unicode でない列の比較と IN を OR でつなぐ）。Unicode でない列が無ければ作らない
    /// </summary>
    /// <remarks>
    /// 使い回しの網羅の単位の形（<see cref="ReuseAndOrderingCoverageQueries"/>）は値を列の値の池から乱数で選ぶので、ASCII の外の文字を
    /// 選ぶとは限らない。ここでは使い回すパラメータの値を必ず ASCII の外の文字を含む値にする。
    /// </remarks>
    private List<DslQuery> NonAsciiReuseQueries(IReadOnlyList<DslColumn> columns)
    {
        var text = FilterableColumns(columns).Where(column => column.NonUnicode).ToList();
        var atoms = new List<DslNode>();

        foreach (var column in text)
        {
            var shared = NonAsciiParam(column, byColumn: true);
            var list = NonAsciiList(column);
            atoms.Add(new DslCompare(DslOp.Eq, shared) { Column = column });
            atoms.Add(new DslMatch(DslMatchKind.Contains, false, shared) { Column = column });
            atoms.Add(new DslIn(false, list) { Column = column });
            atoms.Add(new DslIn(true, list) { Column = column });

            if (
                text.FirstOrDefault(other =>
                    other.Name != column.Name && other.Clr == column.Clr
                ) is
                { } partner
            )
            {
                var sharedWithPartner = NonAsciiParam(column, byColumn: true);
                var listWithPartner = NonAsciiList(column);
                atoms.Add(new DslCompare(DslOp.Eq, sharedWithPartner) { Column = column });
                atoms.Add(new DslCompare(DslOp.Ne, sharedWithPartner) { Column = partner });
                atoms.Add(new DslIn(false, listWithPartner) { Column = column });
                atoms.Add(new DslIn(false, listWithPartner) { Column = partner });
            }
        }

        return atoms.Count == 0
            ? []
            :
            [
                new DslQuery
                {
                    Name = "KXR0",
                    Condition = new DslOr(atoms),
                    Returns = QueryReturnShape.List,
                    IsAtomQuery = false,
                },
            ];
    }

    /// <summary>列で型付けした、ASCII の外の文字を含む値 1 つのリストのパラメータ</summary>
    private DslParam NonAsciiList(DslColumn column) =>
        new(
            parameterPrefix + _parameterIndex++,
            [Pick(NonAsciiValuesOf(column))],
            IsList: true,
            ByColumn: true,
            column
        );

    /// <summary>列の型に入らない値と比べる原子（値オブジェクトの単位では作らない）</summary>
    /// <param name="columns">列（先頭が主キー）</param>
    /// <param name="decimalOrdered">decimal の値と比べる順序比較も作るか</param>
    /// <param name="bothQuotes">文字列のリテラルの原子を、<c>'</c> を含む値と含まない値の 2 つずつ作るか</param>
    /// <param name="seedSubset">種の単位の絞った形にするか（<see cref="OutOfTypeQueries"/>）</param>
    private IEnumerable<DslAtom> OutOfTypeAtoms(
        IReadOnlyList<DslColumn> columns,
        bool decimalOrdered,
        bool bothQuotes,
        bool seedSubset
    )
    {
        if (valueObjects)
        {
            yield break;
        }

        foreach (var column in FilterableColumns(columns))
        {
            var atoms =
                column.Clr == DslClr.Decimal
                && (column.Money is not null || DecimalFacets(column.DataType) is not null)
                    ? DecimalOutOfType(column, decimalOrdered, seedSubset)
                : column.Nullable && column.IsInteger ? IntegerOutOfType(column, seedSubset)
                : column.NonUnicode ? NonAsciiAtoms(column, bothQuotes)
                : column.TimeRange is { } range ? TimeSpanOutOfRange(column, range)
                : column.Clr == DslClr.Single ? FloatBeyondPrecision(column, seedSubset)
                : [];

            foreach (var atom in atoms)
            {
                yield return atom;
            }
        }
    }

    /// <summary>
    /// decimal の列のスケール・整数部の桁の外の値（パラメータは列で型付け・広い型トークン、リテラル、列で型付けした IN のリストと
    /// その否定）
    /// </summary>
    private IEnumerable<DslAtom> DecimalOutOfType(DslColumn column, bool ordered, bool seedSubset)
    {
        var (beyondScale, beyondRange, beyondRangeLongs) = OutOfTypePools(column);

        foreach (
            var op in Enum.GetValues<DslOp>().Where(op => ordered || op is DslOp.Eq or DslOp.Ne)
        )
        {
            if (!seedSubset)
            {
                yield return OutOfTypeCompare(column, op, beyondScale, typeToken: null);
                yield return OutOfTypeCompare(column, op, beyondScale, "decimal(18,6)");
                yield return OutOfTypeCompare(column, op, beyondRange, typeToken: null);
                yield return OutOfTypeCompare(column, op, beyondRangeLongs, "int64");
            }

            yield return OutOfTypeLiteral(column, op, beyondScale);
            yield return OutOfTypeLiteral(column, op, beyondRange);
        }

        foreach (var negated in new[] { false, true })
        {
            // 種の単位は IN をスケールの外、NOT IN を整数部の外の 1 つずつにする
            var pools = seedSubset
                ? [negated ? beyondRange : beyondScale]
                : new[] { beyondScale, beyondRange };

            foreach (var pool in pools)
            {
                yield return new DslIn(
                    negated,
                    new DslParam(
                        parameterPrefix + _parameterIndex++,
                        [Pick(pool), Pick(DslFuzz.ValuePool(column))],
                        IsList: true,
                        ByColumn: true,
                        column
                    )
                )
                {
                    Column = column,
                };
            }
        }
    }

    /// <summary>
    /// 整数の列と、decimal の型トークンで型付けした小数・範囲の外の値（NULL を許す byte・short・int の列）と、整数の型の範囲の外の
    /// 小数のリテラル（NULL を許すどの整数の列も。リテラルは double で比べる＝docs の整数の列と小数のリテラル）
    /// </summary>
    private IEnumerable<DslAtom> IntegerOutOfType(DslColumn column, bool seedSubset)
    {
        var withParameters = column.Clr is DslClr.Byte or DslClr.Int16 or DslClr.Int32;

        if (withParameters && seedSubset)
        {
            yield return OutOfTypeCompare(column, DslOp.Lt, BeyondScaleFractions, "decimal(18,6)");
            yield return OutOfTypeCompare(column, DslOp.Ge, BeyondScaleFractions, "decimal(18,6)");
            yield return OutOfTypeCompare(column, DslOp.Eq, BeyondRangeFractions, "decimal(38,6)");
        }
        else if (withParameters)
        {
            foreach (var op in Enum.GetValues<DslOp>())
            {
                yield return OutOfTypeCompare(column, op, BeyondScaleFractions, "decimal(18,6)");
                yield return OutOfTypeCompare(column, op, BeyondRangeFractions, "decimal(38,6)");
            }
        }

        if (seedSubset)
        {
            yield break;
        }

        foreach (var op in DslFuzzDimensions.FractionalOps)
        {
            yield return OutOfTypeLiteral(column, op, BeyondRangeFractionalLiterals);
        }
    }

    /// <summary>
    /// TimeSpan の列と、列の型の範囲（<see cref="DslColumn.TimeRange"/>）の外の値の原子：6 つの演算子 × 上の外・下の外の値 × 列で型付けと
    /// 型トークン <c>time</c>、IN と NOT IN（上の外だけ・下の外と範囲の中・上の外と下の外）。比べる値の池（<see cref="DslColumn.OperandValues"/>）に
    /// 範囲の外の値が無い列（種の単位の列）は作らない
    /// </summary>
    private IEnumerable<DslAtom> TimeSpanOutOfRange(DslColumn column, DslTimeRange range)
    {
        var operands = (column.OperandValues ?? []).OfType<TimeSpan>().ToList();
        var above = operands.Where(value => value > range.Max).Cast<object>().ToList();
        var below = operands.Where(value => value < range.Min).Cast<object>().ToList();
        var inside = DslFuzz.ValuePool(column);

        if (above.Count == 0 || below.Count == 0)
        {
            yield break;
        }

        foreach (var op in Enum.GetValues<DslOp>())
        {
            foreach (var pool in new[] { above, below })
            {
                yield return OutOfTypeCompare(column, op, pool, typeToken: null);
                yield return OutOfTypeCompare(column, op, pool, DslFuzz.TypeToken(DslClr.TimeSpan));
            }
        }

        foreach (var negated in new[] { false, true })
        {
            foreach (
                var values in new[]
                {
                    new[] { Pick(above) },
                    [Pick(below), Pick(inside)],
                    [Pick(above), Pick(below)],
                }
            )
            {
                yield return new DslIn(
                    negated,
                    new DslParam(
                        parameterPrefix + _parameterIndex++,
                        values,
                        IsList: true,
                        ByColumn: _random.Next(2) == 0,
                        column
                    )
                )
                {
                    Column = column,
                };
            }
        }
    }

    /// <summary>整数の列と比べる、整数の型の範囲の外の小数のリテラル（double へ丸めても行の値の池との大小が変わらない値）</summary>
    private static readonly object[] BeyondRangeFractionalLiterals =
    [
        10000000000000000000.5m,
        -10000000000000000000.5m,
    ];

    /// <summary>
    /// float の列と、float に入らない double の値（<see cref="DslFuzz.FloatOperandDoubles"/> のうち float へ変換すると値が変わるもの）を
    /// <c>float64</c> の型トークンで型付けしたパラメータとの比較と、NOT NULL の列の double のリストの IN・NOT IN（C# は列を double へ
    /// 広げて比べる。NULL を許す列の IN は要素型の一致を型の検査が求めるので作らない）
    /// </summary>
    /// <remarks>種の単位は = と &gt; と IN の 3 つにする。</remarks>
    private IEnumerable<DslAtom> FloatBeyondPrecision(DslColumn column, bool seedSubset)
    {
        var token = DslFuzz.TypeToken(DslClr.Double);
        var beyond = DslFuzz
            .OperandPool(column)
            .OfType<double>()
            .Where(value => (double)(float)value != value)
            .Cast<object>()
            .ToList();
        var stored = DslFuzz
            .ValuePool(column)
            .OfType<float>()
            .Select(value => (object)(double)value)
            .ToList();

        foreach (
            var op in Enum.GetValues<DslOp>().Where(op => !seedSubset || op is DslOp.Eq or DslOp.Gt)
        )
        {
            yield return OutOfTypeCompare(column, op, beyond, token);
        }

        if (column.Nullable)
        {
            yield break;
        }

        foreach (var negated in seedSubset ? [false] : new[] { false, true })
        {
            yield return new DslIn(
                negated,
                new DslParam(
                    parameterPrefix + _parameterIndex++,
                    [Pick(beyond), Pick(stored)],
                    IsList: true,
                    ByColumn: false,
                    column
                )
                {
                    TypeToken = token,
                }
            )
            {
                Column = column,
            };
        }
    }

    /// <summary>値の池から 1 つ選んだリテラルとの比較</summary>
    private DslCompare OutOfTypeLiteral(DslColumn column, DslOp op, IReadOnlyList<object> pool)
    {
        var value = Pick(pool);

        return new DslCompare(op, new DslLiteral(value, DslFuzz.NumberText(value)))
        {
            Column = column,
        };
    }

    /// <summary>値の池から 1 つ選んだパラメータとの比較（型トークンが null なら列で型付けする）</summary>
    private DslCompare OutOfTypeCompare(
        DslColumn column,
        DslOp op,
        IReadOnlyList<object> pool,
        string? typeToken
    ) =>
        new(
            op,
            new DslParam(
                parameterPrefix + _parameterIndex++,
                [Pick(pool)],
                IsList: false,
                ByColumn: typeToken is null,
                column
            )
            {
                TypeToken = typeToken,
            }
        )
        {
            Column = column,
        };

    /// <summary>
    /// Unicode でない文字列の列と、ASCII の外の文字を含む値の原子（= &lt;&gt; のリテラルとパラメータ・LIKE のリテラルの 4 つの形と
    /// パラメータ・文字列一致のリテラルとパラメータ・IN のリスト。それぞれ自身の否定も）
    /// </summary>
    private IEnumerable<DslAtom> NonAsciiAtoms(DslColumn column, bool bothQuotes)
    {
        bool?[] quotes = bothQuotes ? [false, true] : [null];

        foreach (var op in new[] { DslOp.Eq, DslOp.Ne })
        {
            yield return new DslCompare(op, NonAsciiParam(column, byColumn: true))
            {
                Column = column,
            };
            yield return new DslCompare(op, NonAsciiParam(column, byColumn: false))
            {
                Column = column,
            };

            foreach (var quote in quotes)
            {
                yield return new DslCompare(op, NonAsciiLiteral(column, quote)) { Column = column };
            }
        }

        foreach (var negated in new[] { false, true })
        {
            foreach (var shape in new[] { "exact", "prefix", "suffix", "contains" })
            {
                foreach (var quote in quotes)
                {
                    var core = (string)NonAsciiLiteral(column, quote).Value;
                    var pattern = shape switch
                    {
                        "exact" => core,
                        "prefix" => core + "%",
                        "suffix" => "%" + core,
                        _ => "%" + core + "%",
                    };

                    yield return new DslLike(
                        negated,
                        new DslLiteral(pattern, DslFuzz.QuoteText(pattern))
                    )
                    {
                        Column = column,
                    };
                }
            }

            yield return new DslLike(negated, NonAsciiParam(column, _random.Next(2) == 0))
            {
                Column = column,
            };

            foreach (var match in Enum.GetValues<DslMatchKind>())
            {
                foreach (var quote in quotes)
                {
                    yield return new DslMatch(match, negated, NonAsciiLiteral(column, quote))
                    {
                        Column = column,
                    };
                }

                yield return new DslMatch(
                    match,
                    negated,
                    NonAsciiParam(column, _random.Next(2) == 0)
                )
                {
                    Column = column,
                };
            }

            foreach (var withNull in new[] { false, true })
            {
                List<object?> values = [Pick(NonAsciiValuesOf(column))];

                if (withNull)
                {
                    values.Insert(_random.Next(values.Count + 1), null);
                }

                yield return new DslIn(
                    negated,
                    new DslParam(
                        parameterPrefix + _parameterIndex++,
                        values,
                        IsList: true,
                        ByColumn: _random.Next(2) == 0,
                        column
                    )
                )
                {
                    Column = column,
                };
            }
        }
    }

    /// <summary>ASCII の外の文字を含む文字列のリテラル（<paramref name="quote"/> が null なら値の池全体から選ぶ）</summary>
    private DslLiteral NonAsciiLiteral(DslColumn column, bool? quote)
    {
        var value = Pick(
            NonAsciiValuesOf(column)
                .Where(value => quote is null || value.Contains('\'') == quote)
                .ToList()
        );

        return new DslLiteral(value, DslFuzz.QuoteText(value));
    }

    /// <summary>ASCII の外の文字を含む値のパラメータ（列で型付けするか、型トークン <c>string</c>）</summary>
    private DslParam NonAsciiParam(DslColumn column, bool byColumn) =>
        new(
            parameterPrefix + _parameterIndex++,
            [Pick(NonAsciiValuesOf(column))],
            IsList: false,
            byColumn,
            column
        );
}
