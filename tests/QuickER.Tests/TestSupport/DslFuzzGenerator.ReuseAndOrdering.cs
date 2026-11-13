using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// パラメータを使い回すクエリと、列で並べ替えるクエリ（種の単位の <c>S{i}</c>・<c>O{i}</c> と、網羅の単位の決まった形）
/// </summary>
/// <remarks>
/// <para>
/// 使い回しの形（次元表の因子 ParameterReuse）：同じ列の 2 つの原子が同じパラメータを使う・ほかの同じ種類の列で使う・より広い種類の
/// 列で使う（int と bigint・decimal など）・IN のリストを同じ列の IN と NOT IN で使う・ほかの列の IN で使う。型の検査が受け付ける組だけを
/// 作る（値オブジェクトの列どうしは、型トークンで型付けしたパラメータか、同じ値オブジェクトの型を共有する列＝GuidKey の主キーと
/// それを参照する外部キーの列だけ）。
/// </para>
/// <para>
/// 並び替え（次元表の因子 Ordering）は、主キーより前に整数・decimal・日時の列を 1〜2 本置き、最後に必ず主キーを置く（同点を作らない）。
/// 文字列の列では並べない（実装先をまたぐ文字列の並びを docs が約束していない）。網羅の単位は EF Core（SQLite）が流さない
/// decimal の列の順序比較と decimal の列での並び替えを作らない。
/// </para>
/// <para>
/// 種の単位では、本流の乱数の列と別の列（<see cref="SideSeed"/>）の生成器のインスタンスで作り、本流のクエリの引き方を変えない。
/// パラメータ名の接頭辞も分ける（<see cref="SideParameterPrefix"/>）。
/// </para>
/// </remarks>
public sealed partial class DslFuzzGenerator
{
    /// <summary>種の単位の追加のクエリ（S・O）の乱数の列の種（本流の列と別にする）</summary>
    public static int SideSeed(int seed) => unchecked(seed * 7919 + 104729);

    /// <summary>追加のクエリのパラメータ名の接頭辞（本流の <c>p</c> と重ねない）</summary>
    public const string SideParameterPrefix = "q";

    /// <summary>使い回しの形 1 つ（水準・1 つめの原子の列・2 つめの原子の列・パラメータを列で型付けするか・リストか）</summary>
    private sealed record ReuseForm(
        string Level,
        DslColumn First,
        DslColumn Second,
        bool ByColumn,
        bool IsList
    );

    /// <summary>使い回しの原子の組（同じパラメータを参照する 2 つの原子）</summary>
    private sealed record ReusePair(string Level, DslAtom First, DslAtom Second);

    /// <summary>フィルタに使える列（主キーは GuidKey のときだけ）</summary>
    private static List<DslColumn> FilterableColumns(IReadOnlyList<DslColumn> columns) =>
        columns.Where(column => !column.PrimaryKey || column.Clr == DslClr.GuidKey).ToList();

    /// <summary>主キーより前に置ける並びの列（主キーでない整数・decimal・日時の列）</summary>
    private static List<DslColumn> OrderableColumns(
        IReadOnlyList<DslColumn> columns,
        bool withDecimal
    ) =>
        columns
            .Where(column =>
                !column.PrimaryKey
                && (
                    column.IsInteger
                    || column.Clr == DslClr.DateTime
                    || (withDecimal && column.Clr == DslClr.Decimal)
                )
            )
            .ToList();

    /// <summary><paramref name="from"/> の値を <paramref name="to"/> の列が暗黙に受け取れるか（C# の暗黙の数値変換）</summary>
    private static bool Widens(DslClr from, DslClr to) =>
        from switch
        {
            DslClr.Byte => to
                is DslClr.Int16
                    or DslClr.UInt16
                    or DslClr.Int32
                    or DslClr.UInt32
                    or DslClr.Int64
                    or DslClr.UInt64
                    or DslClr.Decimal,
            DslClr.SByte => to is DslClr.Int16 or DslClr.Int32 or DslClr.Int64 or DslClr.Decimal,
            DslClr.Int16 => to is DslClr.Int32 or DslClr.Int64 or DslClr.Decimal,
            DslClr.UInt16 => to
                is DslClr.Int32
                    or DslClr.UInt32
                    or DslClr.Int64
                    or DslClr.UInt64
                    or DslClr.Decimal,
            DslClr.Int32 => to is DslClr.Int64 or DslClr.Decimal,
            DslClr.UInt32 => to is DslClr.Int64 or DslClr.UInt64 or DslClr.Decimal,
            DslClr.Int64 or DslClr.UInt64 => to is DslClr.Decimal,
            _ => false,
        };

    /// <summary>
    /// 型の検査が受け付ける使い回しの形をすべて挙げる（値オブジェクトの列どうしは型トークンか、同じ値オブジェクトの型を共有する
    /// GuidKey の列どうし。値オブジェクトなしのリストは同じ種類の列どうし）
    /// </summary>
    private IEnumerable<ReuseForm> ReuseForms(IReadOnlyList<DslColumn> columns)
    {
        var filterable = FilterableColumns(columns);

        foreach (var column in filterable)
        {
            foreach (var byColumn in ScalarTypings(column, column))
            {
                yield return new(
                    DslFuzzDimensions.ReuseSameColumn,
                    column,
                    column,
                    byColumn,
                    false
                );
            }

            foreach (var byColumn in ListTypings(column, column))
            {
                yield return new(
                    DslFuzzDimensions.ReuseListSameColumn,
                    column,
                    column,
                    byColumn,
                    true
                );
            }
        }

        foreach (var first in filterable)
        {
            foreach (var second in filterable.Where(column => column.Name != first.Name))
            {
                var level =
                    first.Clr == second.Clr ? DslFuzzDimensions.ReuseOtherColumnSameType
                    : Widens(first.Clr, second.Clr) ? DslFuzzDimensions.ReuseOtherColumnWidening
                    : null;

                if (level is null)
                {
                    continue;
                }

                foreach (var byColumn in ScalarTypings(first, second))
                {
                    yield return new(level, first, second, byColumn, false);
                }

                foreach (var byColumn in ListTypings(first, second))
                {
                    yield return new(
                        DslFuzzDimensions.ReuseListOtherColumn,
                        first,
                        second,
                        byColumn,
                        true
                    );
                }
            }
        }
    }

    /// <summary>
    /// 1 つめの列で型付けした値のパラメータを 2 つめの列にも使えるときの型付け（true＝列で型付け・false＝型トークン）
    /// </summary>
    private IEnumerable<bool> ScalarTypings(DslColumn first, DslColumn second)
    {
        var sameValueObject =
            first.Name == second.Name
            || (first.Clr == DslClr.GuidKey && second.Clr == DslClr.GuidKey);

        // 値オブジェクトの列へ別の値オブジェクトの型のパラメータは渡せない（型の検査が断る）
        if (!valueObjects || sameValueObject)
        {
            yield return true;
        }

        if (DslFuzz.HasTypeToken(first.Clr))
        {
            yield return false;
        }
    }

    /// <summary>1 つめの列で型付けしたリストを 2 つめの列の IN にも使えるときの型付け</summary>
    private IEnumerable<bool> ListTypings(DslColumn first, DslColumn second)
    {
        if (first.Name == second.Name)
        {
            foreach (var typing in ScalarTypings(first, second))
            {
                yield return typing;
            }

            yield break;
        }

        if (valueObjects)
        {
            // 値オブジェクトの列の IN は、同じ値オブジェクトのリストか、Create へ渡せる要素型のリスト
            if (first.Clr == DslClr.GuidKey && second.Clr == DslClr.GuidKey)
            {
                yield return true;
            }

            if (
                DslFuzz.HasTypeToken(first.Clr)
                && (first.Clr == second.Clr || Widens(first.Clr, second.Clr))
            )
            {
                yield return false;
            }

            yield break;
        }

        // 値オブジェクトなしの IN は、列の型からリストの要素型へ暗黙に変換できるときだけ（同じ種類の列どうしに限る）
        if (first.Clr == second.Clr)
        {
            yield return true;

            if (DslFuzz.HasTypeToken(first.Clr))
            {
                yield return false;
            }
        }
    }

    /// <summary>値のパラメータを参照する原子の作り方（列に書ける形だけ。比較・LIKE・文字列一致）</summary>
    /// <param name="column">原子の列</param>
    /// <param name="compareOnly">比較だけか（型が広がる原子）</param>
    /// <param name="decimalOrdered">decimal の列の順序比較も作るか（網羅の単位は作らない）</param>
    private static List<Func<DslParam, DslAtom>> ScalarShapes(
        DslColumn column,
        bool compareOnly,
        bool decimalOrdered
    )
    {
        var shapes = new List<Func<DslParam, DslAtom>>();
        var ops =
            column.IsOrdered && (decimalOrdered || column.Clr != DslClr.Decimal)
                ? Enum.GetValues<DslOp>()
                : [DslOp.Eq, DslOp.Ne];

        foreach (var op in ops)
        {
            shapes.Add(param => new DslCompare(op, param) { Column = column });
        }

        // GuidKey の値オブジェクトは部分一致を持たず、LIKE @p も部分一致なので断られる
        if (compareOnly || !column.IsString || column.Clr == DslClr.GuidKey)
        {
            return shapes;
        }

        foreach (var negated in new[] { false, true })
        {
            shapes.Add(param => new DslLike(negated, param) { Column = column });

            foreach (var match in Enum.GetValues<DslMatchKind>())
            {
                shapes.Add(param => new DslMatch(match, negated, param) { Column = column });
            }
        }

        return shapes;
    }

    /// <summary>形のリストのパラメータに入れられるリストの形（型トークンで型付けした値オブジェクトのリストは null を持てない）</summary>
    private List<string> SharedListShapes(DslColumn first, bool byColumn) =>
        DslIn
            .ListShapes.Where(shape =>
                shape is DslIn.ListValues or DslIn.ListEmpty
                || (valueObjects ? byColumn : first.IsString)
            )
            .ToList();

    /// <summary>
    /// 使い回すパラメータの値の池（1 つめの列の値の池のうち、2 つめの列の型にも入る値）
    /// </summary>
    /// <remarks>
    /// 型が広がる組で 2 つめの列の型に入らない値（decimal(10,2) の列と比べる int・long の上限など）は、SQL Server の EF Core 版が
    /// 列の型でパラメータを型付けして例外にするか、丸めて誤った行を返す（直す区切りが別にある）ので作らない。
    /// </remarks>
    private static List<object> SharedPool(DslColumn column, DslColumn other) =>
        DslFuzz.ValuePool(column).Where(value => FitsColumn(value, other)).ToList();

    /// <summary>値が列の型に入るか（decimal の列は宣言の精度とスケールで見る。ほかの列は値の池が列の種類の範囲なので入る）</summary>
    private static bool FitsColumn(object value, DslColumn column)
    {
        if (column.Money is { } money)
        {
            var amount = Convert.ToDecimal(
                value,
                System.Globalization.CultureInfo.InvariantCulture
            );

            return money.Fits(amount);
        }

        if (
            column.Clr != DslClr.Decimal
            || DecimalFacets(column.DataType) is not var (precision, scale)
        )
        {
            return true;
        }

        var number = Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);

        return decimal.Round(number, scale) == number
            && Math.Abs(decimal.Truncate(number)) < Pow10(precision - scale);
    }

    /// <summary>decimal の宣言型の精度とスケール（<c>decimal(10,2)</c> の形でなければ null）</summary>
    internal static (int Precision, int Scale)? DecimalFacets(string dataType)
    {
        var open = dataType.IndexOf('(', StringComparison.Ordinal);
        var parts = open < 0 ? [] : dataType[(open + 1)..].TrimEnd(')').Split(',');

        return
            parts.Length == 2
            && int.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var p)
            && int.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var s)
            ? (p, s)
            : null;
    }

    internal static decimal Pow10(int exponent) =>
        Enumerable.Range(0, exponent).Aggregate(1m, (product, _) => product * 10m);

    /// <summary>使い回す値のパラメータ（1 つめの列の値の池のうち、2 つめの列の型にも入る値を選ぶ）</summary>
    private DslParam SharedParam(DslColumn column, DslColumn other, bool byColumn) =>
        new(
            parameterPrefix + _parameterIndex++,
            [Pick(SharedPool(column, other))],
            IsList: false,
            byColumn,
            column
        );

    /// <summary>使い回すリストのパラメータ（値は <see cref="SharedParam"/> と同じ池から選ぶ）</summary>
    private DslParam SharedList(DslColumn column, DslColumn other, string shape, bool byColumn)
    {
        var pool = SharedPool(column, other);
        List<object?> values = shape switch
        {
            DslIn.ListEmpty => [],
            DslIn.ListAllNull => [null],
            _ => Enumerable
                .Range(0, 1 + _random.Next(3))
                .Select(_ => Pick(pool))
                .Distinct()
                .ToList<object?>(),
        };

        if (shape == DslIn.ListWithNull)
        {
            values.Insert(_random.Next(values.Count + 1), null);
        }

        return new DslParam(parameterPrefix + _parameterIndex++, values, true, byColumn, column);
    }

    /// <summary>形から原子の組を作る（形の作り方は番号で選ぶ＝網羅の単位は番号を回し、種の単位は乱数で選ぶ）</summary>
    private ReusePair BuildPair(
        ReuseForm form,
        int firstShape,
        int secondShape,
        bool decimalOrdered
    )
    {
        if (form.IsList)
        {
            var shapes = SharedListShapes(form.First, form.ByColumn);
            var list = SharedList(
                form.First,
                form.Second,
                shapes[firstShape % shapes.Count],
                form.ByColumn
            );

            return new(
                form.Level,
                new DslIn(firstShape % 2 == 1, list) { Column = form.First },
                new DslIn(secondShape % 2 == 0, list) { Column = form.Second }
            );
        }

        var widening = form.Level == DslFuzzDimensions.ReuseOtherColumnWidening;
        var first = ScalarShapes(form.First, compareOnly: false, decimalOrdered);
        var second = ScalarShapes(form.Second, compareOnly: widening, decimalOrdered);
        var param = SharedParam(form.First, form.Second, form.ByColumn);

        return new(
            form.Level,
            first[firstShape % first.Count](param),
            second[secondShape % second.Count](param)
        );
    }

    /// <summary>形の組の作り方の数（網羅の単位で形を回す回数）</summary>
    private static int ShapeVariants(ReuseForm form, bool decimalOrdered) =>
        form.IsList
            ? DslIn.ListShapes.Count * 2
            : Math.Max(
                ScalarShapes(form.First, false, decimalOrdered).Count,
                ScalarShapes(
                    form.Second,
                    form.Level == DslFuzzDimensions.ReuseOtherColumnWidening,
                    decimalOrdered
                ).Count
            );

    // ---------------- 種の単位 ----------------

    /// <summary>
    /// 種の単位の追加のクエリ：パラメータを使い回すクエリ <c>S{i}</c> と、列で並べ替えるクエリ <c>O{i}</c>
    /// （この生成器は <see cref="SideSeed"/> の種と <see cref="SideParameterPrefix"/> で作る）
    /// </summary>
    /// <param name="columns">列（先頭が主キー）</param>
    /// <param name="reuseCount">使い回すクエリの数</param>
    /// <param name="orderingCount">並べ替えるクエリの数</param>
    public List<DslQuery> SideQueries(
        IReadOnlyList<DslColumn> columns,
        int reuseCount,
        int orderingCount
    )
    {
        var queries = new List<DslQuery>();
        var forms = ReuseForms(columns).ToList();
        var filterable = FilterableColumns(columns);
        var projectable = columns.Skip(1).ToList();

        for (var i = 0; i < reuseCount && forms.Count > 0; i++)
        {
            var form = Pick(forms);
            var pair = BuildPair(
                form,
                _random.Next(1000),
                _random.Next(1000),
                decimalOrdered: true
            );
            DslNode condition = _random.Next(4) switch
            {
                0 => new DslOr([pair.First, pair.Second]),
                1 => new DslAnd([pair.First, pair.Second]),
                2 => new DslOr([pair.First, new DslNot(pair.Second)]),
                _ => new DslNot(new DslAnd([pair.First, pair.Second])),
            };

            // 半分は使い回さない原子を 1 つ足す（使い回しの原子が合成の中に入る形）
            if (_random.Next(2) == 0)
            {
                condition = new DslOr([condition, Atom(Pick(filterable))]);
            }

            var shape = (QueryReturnShape)
                new[]
                {
                    (int)QueryReturnShape.List,
                    (int)QueryReturnShape.Single,
                    (int)QueryReturnShape.Count,
                    (int)QueryReturnShape.Projection,
                }[_random.Next(4)];
            queries.Add(SideQuery($"S{i}", condition, shape, projectable, []));
        }

        var orderable = OrderableColumns(columns, withDecimal: true);

        for (var i = 0; i < orderingCount && orderable.Count > 0; i++)
        {
            var shape = (QueryReturnShape)
                new[]
                {
                    (int)QueryReturnShape.List,
                    (int)QueryReturnShape.Single,
                    (int)QueryReturnShape.Projection,
                }[_random.Next(3)];

            // 一覧は主キーだけの並びも作る（単一・射影の主キーだけの並びは本流のクエリが持つ）
            var extra = shape == QueryReturnShape.List ? _random.Next(3) : 1 + _random.Next(2);
            var orderings = orderable
                .OrderBy(_ => _random.Next())
                .Take(extra)
                .Select(column => new DslOrdering(column, _random.Next(2) == 0))
                .ToList();
            queries.Add(
                SideQuery($"O{i}", Tree(filterable, depth: 2), shape, projectable, orderings)
            );
        }

        return queries;
    }

    /// <summary>種の単位の追加のクエリを組み立てる（綴り・括弧の書き方・主キーの向きは乱数）</summary>
    private DslQuery SideQuery(
        string name,
        DslNode condition,
        QueryReturnShape shape,
        IReadOnlyList<DslColumn> projectable,
        IReadOnlyList<DslOrdering> orderings
    ) =>
        new()
        {
            Name = name,
            Condition = condition,
            Returns = shape,
            IsAtomQuery = false,
            OrderDescending = _random.Next(2) == 0,
            ProjectedColumn = shape == QueryReturnShape.Projection ? Pick(projectable) : null,
            ResultTypeName = shape == QueryReturnShape.Projection ? name + "Row" : null,
            Grouping = _random.Next(2) == 0 ? DslGrouping.Minimal : DslGrouping.Explicit,
            Spelling = (DslSpelling)_random.Next(Enum.GetValues<DslSpelling>().Length),
            Orderings = orderings,
            ListOrdered = shape == QueryReturnShape.List && orderings.Count + _random.Next(2) > 0,
        };

    // ---------------- 網羅の単位 ----------------

    /// <summary>使い回しの組を 1 つのクエリにまとめる数</summary>
    private const int ReusePairsPerQuery = 3;

    /// <summary>
    /// 網羅の単位のパラメータの使い回しと並び替えのクエリ：次元表の因子 ParameterReuse・Ordering とほかの因子の組のうち、
    /// <paramref name="baseQueries"/> がまだ持たない組を足すクエリを貪欲に選ぶ
    /// </summary>
    /// <remarks>
    /// <para>
    /// 1 段目は使い回し：型の検査が受け付ける形（<see cref="ReuseForms"/>）ごとに原子の形を回した組を並べ、水準と原子の形・否定・
    /// 列の種類の組を足すものを貪欲に選ぶ。選んだ組を <see cref="ReusePairsPerQuery"/> 個ずつ、NOT の入れ子の 6 水準 × 戻り形 5 つ ×
    /// 括弧の書き方 2 つで包んだクエリの候補から、ParameterReuse とほかの因子の組を足すものを貪欲に選ぶ。
    /// </para>
    /// <para>
    /// 2 段目は並び替え：<paramref name="baseQueries"/> と 1 段目の合成のクエリ（一覧・単一・射影）を、並びの水準を変えた写しの候補にし、
    /// Ordering とほかの因子の組を足すものを貪欲に選ぶ。並びの列は decimal を除く整数・日時の列を番号で回す。
    /// </para>
    /// <para>
    /// 乱数（値とパラメータ名）はこのメソッドの中で引く（網羅の単位は種の指紋の外）。網羅すべき組は次元表から引くので、因子や
    /// 水準を足すと選ぶクエリも自動で増える。
    /// </para>
    /// </remarks>
    /// <param name="columns">列（先頭が主キー。同じ種類の相方の列を含む）</param>
    /// <param name="baseQueries">網羅の単位のほかのクエリ（既に持つ組の計算に使う）</param>
    public List<DslQuery> ReuseAndOrderingCoverageQueries(
        IReadOnlyList<DslColumn> columns,
        IReadOnlyList<DslQuery> baseQueries
    )
    {
        var covered = new HashSet<string>(
            baseQueries.SelectMany(query => PairKeys(query)),
            StringComparer.Ordinal
        );
        var reuseQueries = SelectReuseQueries(columns, covered);
        var orderingQueries = SelectOrderingQueries(
            columns,
            [.. baseQueries, .. reuseQueries],
            covered
        );

        return [.. reuseQueries, .. orderingQueries];
    }

    /// <summary>
    /// 網羅の単位の並び替えのクエリだけ（<see cref="ReuseAndOrderingCoverageQueries"/> の 2 段目）：<paramref name="baseQueries"/>
    /// の並びの水準を変えた写しのうち、因子 Ordering とほかの因子の組を足すものを貪欲に選ぶ
    /// </summary>
    /// <remarks>
    /// 使い回しのクエリを選ばない網羅の単位（Unicode でない列の網羅の単位 <see cref="DslFuzzUnits.SqlServerAnsiCoverage"/>）が使う。
    /// 乱数は引かない。
    /// </remarks>
    /// <param name="columns">列（先頭が主キー。主キーより前に置ける並びの列を 2 本以上含む）</param>
    /// <param name="baseQueries">網羅の単位のほかのクエリ</param>
    public List<DslQuery> OrderingCoverageQueries(
        IReadOnlyList<DslColumn> columns,
        IReadOnlyList<DslQuery> baseQueries
    )
    {
        var covered = new HashSet<string>(
            baseQueries.SelectMany(query => PairKeys(query)),
            StringComparer.Ordinal
        );

        return SelectOrderingQueries(columns, baseQueries, covered);
    }

    /// <summary>1 段目：使い回しのクエリを選ぶ</summary>
    private List<DslQuery> SelectReuseQueries(
        IReadOnlyList<DslColumn> columns,
        HashSet<string> covered
    )
    {
        // 形ごとに原子の形を回した組を並べ、水準×原子の形・否定・列の種類の組を足すものを選ぶ
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pairs = new List<ReusePair>();

        foreach (var form in ReuseForms(columns))
        {
            var variants = ShapeVariants(form, decimalOrdered: false);

            for (var v = 0; v < variants; v++)
            {
                var pair = BuildPair(form, v, v + 1, decimalOrdered: false);
                string[] keys =
                [
                    .. new[] { pair.First, pair.Second }.SelectMany(atom =>
                        new[]
                        {
                            $"shape:{DslFuzzDimensions.ShapeOf(atom)}",
                            $"negated:{atom.InherentNegation}",
                            $"clr:{atom.Column.Clr}",
                        }.Select(key =>
                            $"{DslFuzzDimensions.ReuseOf(atom, [pair.First, pair.Second])}:{key}"
                        )
                    ),
                ];

                if (keys.Count(seen.Add) > 0)
                {
                    pairs.Add(pair);
                }
            }
        }

        var projectable = columns.Skip(1).ToList();
        var pinned = columns
            .Skip(1)
            .First(column => column.Nullable && (column.IsString || valueObjects));
        var groups = pairs.Chunk(ReusePairsPerQuery).ToList();
        var candidates = new List<DslQuery>();
        QueryReturnShape?[] kinds =
        [
            QueryReturnShape.List,
            QueryReturnShape.Single,
            QueryReturnShape.Count,
            QueryReturnShape.Projection,
            null, // 射影の非 NULL 指定
        ];

        foreach (var group in groups)
        {
            foreach (var nesting in DslFuzzDimensions.NestingLevels)
            {
                foreach (var kind in kinds)
                {
                    foreach (var grouping in new[] { DslGrouping.Explicit, DslGrouping.Minimal })
                    {
                        var condition = WrapPairs(nesting, group);
                        var index = candidates.Count;
                        candidates.Add(
                            kind is null
                                ? new DslQuery
                                {
                                    Name = string.Empty,
                                    Condition = new DslAnd([
                                        new DslNullCheck(IsNot: true) { Column = pinned },
                                        condition,
                                    ]),
                                    Returns = QueryReturnShape.Projection,
                                    IsAtomQuery = false,
                                    ProjectedColumn = pinned,
                                    ProjectedNonNull = true,
                                    Grouping = grouping,
                                    Spelling = SpellingAt(index),
                                }
                                : new DslQuery
                                {
                                    Name = string.Empty,
                                    Condition = condition,
                                    Returns = kind.Value,
                                    IsAtomQuery = false,
                                    OrderDescending = index % 2 == 0,
                                    ProjectedColumn =
                                        kind == QueryReturnShape.Projection
                                            ? projectable[index % projectable.Count]
                                            : null,
                                    Grouping = grouping,
                                    Spelling = SpellingAt(index),
                                }
                        );
                    }
                }
            }
        }

        var required = RequiredPairKeys(
            DslFuzzDimensions.ParameterReuse,
            exclude: DslFuzzDimensions.Ordering
        );

        return Greedy(candidates, required, covered)
            .Select((query, i) => Finish(query, $"KR{i}", query.Orderings))
            .ToList();
    }

    /// <summary>2 段目：並びの水準を変えた写しを選ぶ</summary>
    private List<DslQuery> SelectOrderingQueries(
        IReadOnlyList<DslColumn> columns,
        IReadOnlyList<DslQuery> bases,
        HashSet<string> covered
    )
    {
        var orderable = OrderableColumns(columns, withDecimal: false);
        var candidates = new List<DslQuery>();

        foreach (
            var query in bases.Where(query =>
                !query.IsAtomQuery && query.Returns != QueryReturnShape.Count
            )
        )
        {
            foreach (var extra in new[] { -1, 0, 1, 2 })
            {
                // -1 は並べない一覧（単一・射影はいつも並べる）・0 は主キーだけ
                if (extra == -1 && query.Returns != QueryReturnShape.List)
                {
                    continue;
                }

                var index = candidates.Count;
                var orderings = Enumerable
                    .Range(0, Math.Max(extra, 0))
                    .Select(n => new DslOrdering(
                        orderable[(index + n) % orderable.Count],
                        (index + n) % 2 == 1
                    ))
                    .DistinctBy(ordering => ordering.Column.Name)
                    .ToList();

                if (orderings.Count != Math.Max(extra, 0))
                {
                    continue;
                }

                candidates.Add(
                    query.WithOrdering(
                        orderings,
                        listOrdered: extra >= 0,
                        descending: index % 3 == 0
                    )
                );
            }
        }

        var required = RequiredPairKeys(DslFuzzDimensions.Ordering, exclude: null);

        return Greedy(candidates, required, covered)
            .Select((query, i) => Finish(query, $"KB{i}", query.Orderings))
            .ToList();
    }

    /// <summary>選んだクエリに名前を付け、パラメータ名を付け直す（同じパラメータを参照する原子は同じ新しい名前にそろえる）</summary>
    private DslQuery Finish(DslQuery query, string name, IReadOnlyList<DslOrdering> orderings)
    {
        var renamed = new Dictionary<string, DslParam>(StringComparer.Ordinal);
        DslParam Same(DslParam param) =>
            renamed.TryGetValue(param.Name, out var fresh)
                ? fresh
                : renamed[param.Name] = param with { Name = parameterPrefix + _parameterIndex++ };
        DslNode Copy(DslNode node) =>
            node switch
            {
                DslCompare { Operand: DslParam p } c => c with { Operand = Same(p) },
                DslLike { Operand: DslParam p } l => l with { Operand = Same(p) },
                DslMatch { Operand: DslParam p } m => m with { Operand = Same(p) },
                DslIn i => i with { List = Same(i.List) },
                DslAtom atom => atom with { },
                DslNot not => new DslNot(Copy(not.Inner)),
                DslAnd and => new DslAnd(and.Items.Select(Copy).ToList()),
                DslOr or => new DslOr(or.Items.Select(Copy).ToList()),
                _ => throw new ArgumentOutOfRangeException(nameof(node)),
            };

        return new DslQuery
        {
            Name = name,
            Condition = Copy(query.Condition),
            Grouping = query.Grouping,
            Spelling = query.Spelling,
            Returns = query.Returns,
            IsAtomQuery = false,
            OrderDescending = query.OrderDescending,
            ProjectedColumn = query.ProjectedColumn,
            ProjectedNonNull = query.ProjectedNonNull,
            ResultTypeName = query.Returns == QueryReturnShape.Projection ? name + "Row" : null,
            Orderings = orderings,
            ListOrdered = query.ListOrdered,
        };
    }

    /// <summary>組をそれぞれ NOT の入れ子の水準の形で包み、OR でまとめる（組の 2 つの原子は同じ水準になる）</summary>
    private static DslNode WrapPairs(string nesting, IReadOnlyList<ReusePair> pairs)
    {
        static DslNode Not(DslNode node, int times) =>
            times == 0 ? node : Not(new DslNot(node), times - 1);

        // 同じ原子のインスタンスを 2 つのクエリで共有しない（使い回しの水準は参照の同一性で数える）
        static DslAtom Fresh(DslAtom atom) => atom with { };

        return new DslOr(
            pairs
                .SelectMany(pair =>
                    nesting switch
                    {
                        DslFuzzDimensions.NestingNone => new DslNode[]
                        {
                            Fresh(pair.First),
                            Fresh(pair.Second),
                        },
                        DslFuzzDimensions.NestingSingle =>
                        [
                            Not(Fresh(pair.First), 1),
                            Not(Fresh(pair.Second), 1),
                        ],
                        DslFuzzDimensions.NestingDouble =>
                        [
                            Not(Fresh(pair.First), 2),
                            Not(Fresh(pair.Second), 2),
                        ],
                        DslFuzzDimensions.NestingTriple =>
                        [
                            Not(Fresh(pair.First), 3),
                            Not(Fresh(pair.Second), 3),
                        ],
                        DslFuzzDimensions.NestingGroupAnd =>
                        [
                            new DslNot(new DslAnd([Fresh(pair.First), Fresh(pair.Second)])),
                        ],
                        _ => [new DslNot(new DslOr([Fresh(pair.First), Fresh(pair.Second)]))],
                    }
                )
                .ToList()
        );
    }

    /// <summary>
    /// 次元表の網羅すべき 2 因子の組のうち、<paramref name="factor"/> を含み <paramref name="exclude"/> を含まないものの鍵
    /// </summary>
    private static HashSet<string> RequiredPairKeys(string factor, string? exclude) =>
        DslFuzzDimensions
            .Table.RequiredPairs()
            .Where(tuple =>
                tuple.Items.Any(item => item.Factor == factor)
                && (exclude is null || tuple.Items.All(item => item.Factor != exclude))
            )
            .Select(tuple => tuple.Key)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>クエリの原子ごとの割り当てが含む 2 因子の組の鍵（次元表の因子の並びで組む）</summary>
    private static IEnumerable<string> PairKeys(DslQuery query)
    {
        var factors = DslFuzzDimensions.Table.Factors.Select(factor => factor.Name).ToList();

        foreach (var assignment in DslFuzzDimensions.Assignments(query))
        {
            for (var i = 0; i < factors.Count; i++)
            {
                for (var j = i + 1; j < factors.Count; j++)
                {
                    yield return new CoverageTuple([
                        (factors[i], assignment[factors[i]]),
                        (factors[j], assignment[factors[j]]),
                    ]).Key;
                }
            }
        }
    }

    /// <summary>
    /// 網羅すべき組のうちまだ持たない組を最も多く足す候補を、足すものが無くなるまで順に選ぶ（同点は候補の並びの先頭。決定的）
    /// </summary>
    private static List<DslQuery> Greedy(
        IReadOnlyList<DslQuery> candidates,
        HashSet<string> required,
        HashSet<string> covered
    )
    {
        var gains = candidates
            .Select(query =>
                PairKeys(query).Where(required.Contains).ToHashSet(StringComparer.Ordinal)
            )
            .ToList();
        var picked = new List<DslQuery>();

        while (true)
        {
            var best = -1;
            var bestGain = 0;

            for (var i = 0; i < gains.Count; i++)
            {
                var gain = gains[i].Count(key => !covered.Contains(key));

                if (gain > bestGain)
                {
                    best = i;
                    bestGain = gain;
                }
            }

            if (best < 0)
            {
                return picked;
            }

            picked.Add(candidates[best]);
            covered.UnionWith(gains[best]);
        }
    }
}
