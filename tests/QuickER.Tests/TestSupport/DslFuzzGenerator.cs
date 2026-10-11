using System.Globalization;
using QuickER.Model;

namespace QuickER.Tests.TestSupport;

/// <summary>総当たりの 1 単位（図・行・クエリ）</summary>
/// <param name="Name">単位の名前（失敗のキーの入口に使う）</param>
/// <param name="TableName">テーブル名</param>
/// <param name="Columns">列（先頭が主キー）</param>
/// <param name="Rows">行（列名 → C# の値）</param>
/// <param name="Queries">クエリ</param>
/// <param name="ValueObjects">値オブジェクトを生成するか</param>
/// <param name="Seed">作ったシード（失敗の例に載せる）</param>
public sealed record DslFuzzUnit(
    string Name,
    string TableName,
    IReadOnlyList<DslColumn> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    IReadOnlyList<DslQuery> Queries,
    bool ValueObjects,
    int Seed = 0
)
{
    /// <summary>主キーの列</summary>
    public DslColumn Key => Columns[0];

    /// <summary>図（1 テーブル＋クエリ）を組み立てる</summary>
    public ErDiagram BuildDiagram(string targetDbms)
    {
        var entity = new Entity { TableName = TableName };

        foreach (var column in Columns)
        {
            entity.Columns.Add(column.Model);
        }

        var diagram = new ErDiagram { TargetDbms = targetDbms, Entities = { entity } };

        foreach (var query in Queries)
        {
            diagram.Queries.Add(BuildQuery(entity, query));
        }

        return diagram;
    }

    private QueryDefinition BuildQuery(Entity entity, DslQuery query)
    {
        var definition = new QueryDefinition
        {
            EntityId = entity.Id,
            Name = query.Name,
            Returns = query.Returns,
            Condition = DslFuzz.Render(query.Condition),
        };

        foreach (var parameter in query.Parameters)
        {
            definition.Parameters.Add(
                new QueryParameter
                {
                    Name = parameter.Name,
                    IsList = parameter.IsList,
                    Type = parameter.ByColumn ? null : DslFuzz.TypeToken(parameter.Column.Clr),
                    SourceColumnId = parameter.ByColumn ? parameter.Column.Model.Id : null,
                }
            );
        }

        if (query.Returns is QueryReturnShape.Single or QueryReturnShape.Projection)
        {
            definition.OrderBy.Add(
                new QueryOrdering { ColumnId = Key.Model.Id, Descending = query.OrderDescending }
            );
        }

        if (query.Returns == QueryReturnShape.Projection)
        {
            definition.ResultTypeName = query.ResultTypeName;
            definition.Fields.Add(
                new ProjectionField { Name = "Id", SourceColumnId = Key.Model.Id }
            );
            definition.Fields.Add(
                new ProjectionField
                {
                    Name = "Picked",
                    SourceColumnId = query.ProjectedColumn!.Model.Id,
                    IsNullable = query.ProjectedNonNull ? false : null,
                }
            );
        }

        return definition;
    }
}

/// <summary>簡易 DSL の条件の総当たりの生成器</summary>
/// <remarks>
/// docs と型の検査が「書ける」とする組み合わせだけを作る（文字列・Guid の順序比較・日時のリテラル・LIKE のリテラルの途中の
/// <c>%</c> / <c>_</c> は作らない）。整数の型の範囲外のリテラルは値オブジェクトなしの単位でだけ作る（値オブジェクトありでは
/// 型の検査が警告で飛ばす＝CLAUDE.md の名前付きクエリの節）。
/// </remarks>
public sealed class DslFuzzGenerator(int seed, bool valueObjects, bool outOfRangeLiterals)
{
    private readonly Random _random = new(seed);
    private int _parameterIndex;

    /// <summary>行を作る（NULL 許容の列は約 4 分の 1 を NULL にする）</summary>
    public List<IReadOnlyDictionary<string, object?>> Rows(
        IReadOnlyList<DslColumn> columns,
        int count
    )
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();

        for (var i = 0; i < count; i++)
        {
            var row = new Dictionary<string, object?>();

            foreach (var column in columns)
            {
                // 1 行目は必ず NULL・2 行目は必ず値（NULL の行の扱いを毎回確かめる）
                row[column.Name] =
                    column.PrimaryKey ? KeyValue(column, i)
                    : column.Nullable && (i == 0 || (i > 1 && _random.Next(4) == 0)) ? null
                    : Pick(DslFuzz.ValuePool(column.Clr));
            }

            rows.Add(row);
        }

        return rows;
    }

    private static object KeyValue(DslColumn column, int index) =>
        column.Clr switch
        {
            DslClr.Int64 => (long)(index + 1),
            DslClr.GuidKey => new Guid(index + 1, 0, 0, new byte[8]).ToString(),
            _ => throw new ArgumentOutOfRangeException(nameof(column)),
        };

    /// <summary>
    /// 原子のクエリ（列ごとに使える全ての形の原子と、その NOT）・射影の非 NULL 指定のクエリ・合成のクエリを作る
    /// </summary>
    /// <remarks>
    /// 原子の形（演算子・リテラルかパラメータか・否定の有無）は列ごとに全て作り、値だけを乱数で選ぶ
    /// （失敗のキーはシードに依らず同じ形が毎回出る＝既知の失敗の一覧が「起きなかった」で揺れない）。
    /// </remarks>
    public List<DslQuery> Queries(IReadOnlyList<DslColumn> columns, int compounds)
    {
        var queries = new List<DslQuery>();
        var filterable = columns
            .Where(column => !column.PrimaryKey || column.Clr == DslClr.GuidKey)
            .ToList();
        var atoms = filterable.SelectMany(AllAtoms).ToList();

        for (var i = 0; i < atoms.Count; i++)
        {
            queries.Add(AtomQuery($"A{i}", atoms[i]));
            queries.Add(AtomQuery($"A{i}n", new DslNot(Rename(atoms[i]))));
        }

        // 射影のフィールドを非 NULL と指定する形（NULL 許容の参照型・値オブジェクトの列）
        var pinned = columns
            .Skip(1)
            .Where(column => column.Nullable && (column.IsString || valueObjects))
            .ToList();

        for (var i = 0; i < pinned.Count; i++)
        {
            queries.Add(
                new DslQuery
                {
                    Name = $"P{i}",
                    Condition = new DslNullCheck(IsNot: true) { Column = pinned[i] },
                    Returns = QueryReturnShape.Projection,
                    IsAtomQuery = false,
                    ProjectedColumn = pinned[i],
                    ProjectedNonNull = true,
                    ResultTypeName = $"P{i}Row",
                }
            );
        }

        for (var i = 0; i < compounds; i++)
        {
            queries.Add(Compound($"C{i}", columns, filterable));
        }

        return queries;
    }

    /// <summary>列に使える全ての形の原子（値は乱数）</summary>
    private IEnumerable<DslAtom> AllAtoms(DslColumn column)
    {
        foreach (var negated in new[] { false, true })
        {
            // IS [NOT] NULL は NULL を許す列だけ（NOT NULL の列は生成時に断られる）
            if (column.Nullable && !column.PrimaryKey)
            {
                yield return new DslNullCheck(negated) { Column = column };
            }

            yield return new DslIn(negated, ListParam(column)) { Column = column };
        }

        var ops = column.IsOrdered ? Enum.GetValues<DslOp>() : [DslOp.Eq, DslOp.Ne];
        var literalAllowed = column.IsString || column.IsInteger || column.Clr == DslClr.Decimal;

        foreach (var op in ops)
        {
            yield return new DslCompare(op, Param(column)) { Column = column };

            if (literalAllowed)
            {
                yield return new DslCompare(op, Literal(column)) { Column = column };
            }
        }

        if (outOfRangeLiterals && column.IsInteger && OutOfRange(column.Clr) is { } outside)
        {
            foreach (var op in new[] { DslOp.Eq, DslOp.Lt })
            {
                yield return new DslCompare(
                    op,
                    new DslLiteral(outside, DslFuzz.NumberText(outside))
                )
                {
                    Column = column,
                    LiteralOutOfRange = true,
                };
            }
        }

        foreach (var rejected in RejectedLiterals(column))
        {
            yield return new DslCompare(DslOp.Eq, new DslLiteral(rejected, rejected))
            {
                Column = column,
                RejectedByTypeCheck = true,
            };
        }

        if (!column.IsString)
        {
            yield break;
        }

        foreach (var negated in new[] { false, true })
        {
            foreach (var shape in new[] { "exact", "prefix", "suffix", "contains" })
            {
                yield return new DslLike(negated, LikeLiteral(column, shape)) { Column = column };
            }

            yield return new DslLike(negated, Param(column)) { Column = column };

            foreach (var match in Enum.GetValues<DslMatchKind>())
            {
                yield return new DslMatch(match, negated, StringLiteral(column, likeSafe: false))
                {
                    Column = column,
                };
                yield return new DslMatch(match, negated, Param(column)) { Column = column };
            }
        }
    }

    /// <summary>列の型の範囲内のリテラル</summary>
    private DslOperand Literal(DslColumn column)
    {
        if (column.IsString)
        {
            return StringLiteral(column, likeSafe: false);
        }

        var value = Pick(DslFuzz.ValuePool(column.Clr));

        return new DslLiteral(value, DslFuzz.NumberText(value));
    }

    /// <summary>LIKE のリテラル（% は先頭・末尾だけ）</summary>
    private DslLiteral LikeLiteral(DslColumn column, string shape)
    {
        var core = ((DslLiteral)StringLiteral(column, likeSafe: true)).Value.ToString()!;
        var pattern = shape switch
        {
            "exact" => core,
            "prefix" => core + "%",
            "suffix" => "%" + core,
            _ => "%" + core + "%",
        };

        return new DslLiteral(pattern, "'" + pattern + "'");
    }

    private static DslQuery AtomQuery(string name, DslNode condition) =>
        new()
        {
            Name = name,
            Condition = condition,
            Returns = QueryReturnShape.List,
            IsAtomQuery = true,
        };

    private DslQuery Compound(string name, IReadOnlyList<DslColumn> all, List<DslColumn> filterable)
    {
        var condition = Tree(filterable, depth: 3);
        var shape = (QueryReturnShape)
            new[]
            {
                (int)QueryReturnShape.List,
                (int)QueryReturnShape.Single,
                (int)QueryReturnShape.Count,
                (int)QueryReturnShape.Projection,
            }[_random.Next(4)];
        var projected = shape == QueryReturnShape.Projection ? Pick(all.Skip(1).ToList()) : null;

        return new DslQuery
        {
            Name = name,
            Condition = condition,
            Returns = shape,
            IsAtomQuery = false,
            OrderDescending = _random.Next(2) == 0,
            ProjectedColumn = projected,
            ResultTypeName = shape == QueryReturnShape.Projection ? name + "Row" : null,
        };
    }

    private DslNode Tree(List<DslColumn> columns, int depth)
    {
        if (depth == 0 || _random.Next(3) == 0)
        {
            return Atom(Pick(columns));
        }

        return _random.Next(3) switch
        {
            0 => new DslNot(Tree(columns, depth - 1)),
            1 => new DslAnd(
                Enumerable
                    .Range(0, 2 + _random.Next(2))
                    .Select(_ => Tree(columns, depth - 1))
                    .ToList()
            ),
            _ => new DslOr(
                Enumerable
                    .Range(0, 2 + _random.Next(2))
                    .Select(_ => Tree(columns, depth - 1))
                    .ToList()
            ),
        };
    }

    /// <summary>NOT のクエリ用に、原子のパラメータ名を付け直す（同じ名前のパラメータを別のクエリで使い回さない）</summary>
    private DslAtom Rename(DslAtom atom) =>
        atom switch
        {
            DslCompare { Operand: DslParam p } c => c with { Operand = Fresh(p) },
            DslLike { Operand: DslParam p } l => l with { Operand = Fresh(p) },
            DslMatch { Operand: DslParam p } m => m with { Operand = Fresh(p) },
            DslIn i => i with { List = Fresh(i.List) },
            _ => atom,
        };

    private DslParam Fresh(DslParam param) => param with { Name = "p" + _parameterIndex++ };

    /// <summary>列に使える原子を 1 つ作る</summary>
    private DslAtom Atom(DslColumn column)
    {
        var choices = new List<Func<DslAtom>>
        {
            () => new DslIn(_random.Next(2) == 0, ListParam(column)) { Column = column },
        };

        // IS [NOT] NULL は NULL を許す列だけ（NOT NULL の列は生成時に断られる）
        if (column.Nullable && !column.PrimaryKey)
        {
            choices.Add(() => new DslNullCheck(_random.Next(2) == 0) { Column = column });
        }

        if (column.IsOrdered)
        {
            choices.Add(() => Compare(column, PickOp(all: true)));
            choices.Add(() => Compare(column, PickOp(all: true)));
        }
        else
        {
            choices.Add(() => Compare(column, PickOp(all: false)));
        }

        if (column.IsString)
        {
            choices.Add(() =>
                new DslLike(_random.Next(2) == 0, LikeOperand(column)) { Column = column }
            );
            choices.Add(() =>
                new DslMatch(
                    (DslMatchKind)_random.Next(3),
                    _random.Next(2) == 0,
                    _random.Next(2) == 0 ? StringLiteral(column, likeSafe: false) : Param(column)
                )
                {
                    Column = column,
                }
            );
        }

        return Pick(choices)();
    }

    private DslOp PickOp(bool all) => all ? (DslOp)_random.Next(6) : (DslOp)_random.Next(2);

    private DslCompare Compare(DslColumn column, DslOp op)
    {
        // 文字列・数値はリテラルも使える（日時・Guid はリテラルを書けないのでパラメータだけ）
        var literalAllowed = column.IsString || column.IsInteger || column.Clr == DslClr.Decimal;

        if (literalAllowed && _random.Next(2) == 0)
        {
            if (column.IsString)
            {
                return new DslCompare(op, StringLiteral(column, likeSafe: false))
                {
                    Column = column,
                };
            }

            // 範囲外のリテラルは原子のクエリだけで作る（合成の条件に混ぜると、失敗のキーがシードで揺れる）
            var value = Pick(DslFuzz.ValuePool(column.Clr));

            return new DslCompare(op, new DslLiteral(value, DslFuzz.NumberText(value)))
            {
                Column = column,
            };
        }

        return new DslCompare(op, Param(column)) { Column = column };
    }

    /// <summary>型の範囲のすぐ外の整数（long に収まり、型の検査が飛ばさない種類だけ）</summary>
    private static long? OutOfRange(DslClr clr) =>
        clr switch
        {
            DslClr.Byte => 256L,
            DslClr.SByte => 128L,
            DslClr.Int16 => 32768L,
            DslClr.UInt16 => -1L,
            DslClr.Int32 => 3000000000L,
            DslClr.UInt32 => -1L,
            _ => null,
        };

    /// <summary>
    /// 型の検査が断る整数のリテラル（CLAUDE.md の名前付きクエリの節: ulong を超える値・素の ulong の列に負の値・
    /// 素の符号付きの列に long を超える値・値オブジェクトの内包型の範囲外は警告で飛ばす）
    /// </summary>
    private IEnumerable<string> RejectedLiterals(DslColumn column)
    {
        if (!column.IsInteger)
        {
            yield break;
        }

        yield return "18446744073709551616";

        if (valueObjects)
        {
            var outside = column.Clr switch
            {
                DslClr.Int64 => "9223372036854775808",
                DslClr.UInt64 => "-1",
                _ => OutOfRange(column.Clr) is { } value ? DslFuzz.NumberText(value) : null,
            };

            if (outside is not null)
            {
                yield return outside;
            }

            yield break;
        }

        if (column.Clr == DslClr.UInt64)
        {
            yield return "-1";
        }
        else if (column.Clr is DslClr.Int64 or DslClr.Int32 or DslClr.Int16 or DslClr.SByte)
        {
            yield return "9223372036854775808";
        }
    }

    private DslOperand LikeOperand(DslColumn column)
    {
        if (_random.Next(3) == 0)
        {
            return Param(column);
        }

        var core = ((DslLiteral)StringLiteral(column, likeSafe: true)).Value.ToString()!;
        var pattern = _random.Next(4) switch
        {
            0 => core,
            1 => core + "%",
            2 => "%" + core,
            _ => "%" + core + "%",
        };

        return new DslLiteral(pattern, "'" + pattern + "'");
    }

    /// <summary>文字列のリテラル（LIKE のパターンの芯なら % と _ を含まず空でないもの）</summary>
    private DslOperand StringLiteral(DslColumn column, bool likeSafe)
    {
        var pool = DslFuzz
            .ValuePool(column.Clr)
            .Cast<string>()
            .Where(value =>
                !likeSafe || (value.Length > 0 && !value.Contains('%') && !value.Contains('_'))
            )
            .ToList();
        var value = Pick(pool);

        return new DslLiteral(value, "'" + value + "'");
    }

    private DslParam Param(DslColumn column) =>
        new(
            "p" + _parameterIndex++,
            [Pick(DslFuzz.ValuePool(column.Clr))],
            IsList: false,
            ByColumn: !DslFuzz.HasTypeToken(column.Clr) || _random.Next(2) == 0,
            column
        );

    private DslParam ListParam(DslColumn column)
    {
        var pool = DslFuzz.ValuePool(column.Clr);
        var values = Enumerable
            .Range(0, 1 + _random.Next(3))
            .Select(_ => Pick(pool))
            .Distinct()
            .ToList();

        return new DslParam(
            "p" + _parameterIndex++,
            values,
            IsList: true,
            ByColumn: !DslFuzz.HasTypeToken(column.Clr) || _random.Next(2) == 0,
            column
        );
    }

    private T Pick<T>(IReadOnlyList<T> items) => items[_random.Next(items.Count)];
}
