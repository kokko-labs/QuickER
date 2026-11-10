using System.Globalization;
using System.Numerics;
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
/// <param name="SelfReference">
/// 主キーを参照する自己参照の外部キーの列の名前（無ければ null）。外部キーの列は主キーの値オブジェクトの型を共有する
/// （GuidKey の主キーなら NULL 許容の GuidKey の列になる）。行の値は、その行より前の行のキーか NULL にしておくこと
/// </param>
public sealed record DslFuzzUnit(
    string Name,
    string TableName,
    IReadOnlyList<DslColumn> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    IReadOnlyList<DslQuery> Queries,
    bool ValueObjects,
    int Seed = 0,
    string? SelfReference = null
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

        if (SelfReference is not null)
        {
            var child = Columns.Single(column => column.Name == SelfReference);
            diagram.Relationships.Add(
                new Relationship
                {
                    SourceEntityId = entity.Id,
                    TargetEntityId = entity.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = { new RelationshipColumnPair(Key.Model.Id, child.Model.Id) },
                }
            );
        }

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
            Condition = query.Text,
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
/// <c>%</c> / <c>_</c> は作らない）。型の検査が断る整数のリテラル（列の型〔値オブジェクトなら内包型〕の範囲の外と
/// <c>ulong</c> を超える値）は、値オブジェクトの有無に依らず原子のクエリでだけ作り（<see cref="AllAtoms"/>）、警告で飛ばされる
/// ことを表明する（合成の条件には混ぜない＝CLAUDE.md の名前付きクエリの節）。
/// </remarks>
public sealed class DslFuzzGenerator(int seed, bool valueObjects)
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
        var atoms = filterable.SelectMany(column => AllAtoms(column)).ToList();

        // 原子のクエリの綴りは番号で回す（乱数を引かずに、どの綴りも原子のクエリに出す）
        for (var i = 0; i < atoms.Count; i++)
        {
            queries.Add(AtomQuery($"A{i}", atoms[i], SpellingAt(i)));
            queries.Add(AtomQuery($"A{i}n", new DslNot(Rename(atoms[i])), SpellingAt(i + 1)));
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

    /// <summary>網羅の単位の合成のクエリの原子の数の上限</summary>
    private const int CoverageAtomsPerQuery = 6;

    /// <summary>
    /// 網羅の単位のクエリ：<paramref name="atomColumns"/> の列の原子のクエリ（<see cref="Queries"/> と同じ作り方）と、
    /// 戻り形ごと・射影の非 NULL 指定の、原子の形の全水準・自身の否定の 2 水準・列の全種類・NOT の 2 向きを含む合成のクエリ
    /// </summary>
    /// <remarks>
    /// <para>
    /// 合成のクエリの原子は、列に使える全ての形の原子（<see cref="AllAtoms"/>）を並べ、まだ出していない形・否定・列の種類を
    /// 足すものだけを先頭から貪欲に選ぶ。型の検査が断る原子と、EF Core（SQLite）が流さない decimal の列の順序比較は使わない
    /// （その原子を含むクエリは照合されず、同じクエリの他の原子まで数えられなくなる）。選んだ原子を
    /// <see cref="CoverageAtomsPerQuery"/> 個ずつ OR でまとめ、1 つおきに NOT で包む。射影の非 NULL 指定は、射影する列の
    /// IS NOT NULL と AND でつなぐ（射影する列が NULL の行を返さない）。
    /// </para>
    /// <para>
    /// 乱数（値とパラメータ名）はこのメソッドの中で引き、<see cref="Queries"/> の引き方には触れない（網羅の単位は種の指紋の外）。
    /// </para>
    /// </remarks>
    /// <param name="columns">列（先頭が主キー）</param>
    /// <param name="atomColumns">原子のクエリを作る列の名前（網羅の単位で初めて現れる列の種類の列）</param>
    public List<DslQuery> CoverageQueries(
        IReadOnlyList<DslColumn> columns,
        IReadOnlyCollection<string> atomColumns
    )
    {
        var queries = new List<DslQuery>();
        var filterable = columns
            .Where(column => !column.PrimaryKey || column.Clr == DslClr.GuidKey)
            .ToList();
        var atoms = filterable
            .Where(column => atomColumns.Contains(column.Name))
            .SelectMany(column => AllAtoms(column))
            .ToList();

        for (var i = 0; i < atoms.Count; i++)
        {
            queries.Add(AtomQuery($"KA{i}", atoms[i], SpellingAt(i)));
            queries.Add(AtomQuery($"KA{i}n", new DslNot(Rename(atoms[i])), SpellingAt(i + 1)));
        }

        // 文字列のリテラルは ' を含む値と含まない値の両方の原子を並べる（網羅の単位で両方の水準を埋める）
        var forms = filterable
            .SelectMany(column => AllAtoms(column, bothQuotes: true))
            .Where(atom =>
                !atom.IsRejectedByTypeCheck
                && !(
                    atom is DslCompare { Op: not (DslOp.Eq or DslOp.Ne) }
                    && atom.Column.Clr == DslClr.Decimal
                )
            )
            .ToList();
        var projectable = columns.Skip(1).ToList();
        var shapes = new[]
        {
            QueryReturnShape.List,
            QueryReturnShape.Single,
            QueryReturnShape.Count,
            QueryReturnShape.Projection,
        };

        foreach (var shape in shapes)
        {
            var chunks = CoverageChunks(forms);

            for (var i = 0; i < chunks.Count; i++)
            {
                var name = $"K{shape}{i}";
                queries.Add(
                    new DslQuery
                    {
                        Name = name,
                        Condition = chunks[i],
                        Returns = shape,
                        IsAtomQuery = false,
                        OrderDescending = i % 2 == 0,
                        ProjectedColumn =
                            shape == QueryReturnShape.Projection
                                ? projectable[i % projectable.Count]
                                : null,
                        ResultTypeName = shape == QueryReturnShape.Projection ? name + "Row" : null,
                    }
                );
            }
        }

        // 射影の非 NULL 指定（射影する列は NULL 許容の参照型か値オブジェクトの列＝Queries と同じ条件）
        var pinned = columns
            .Skip(1)
            .First(column => column.Nullable && (column.IsString || valueObjects));
        var pinnedChunks = CoverageChunks(forms);

        for (var i = 0; i < pinnedChunks.Count; i++)
        {
            queries.Add(
                new DslQuery
                {
                    Name = $"KP{i}",
                    Condition = new DslAnd([
                        new DslNullCheck(IsNot: true) { Column = pinned },
                        pinnedChunks[i],
                    ]),
                    Returns = QueryReturnShape.Projection,
                    IsAtomQuery = false,
                    ProjectedColumn = pinned,
                    ProjectedNonNull = true,
                    ResultTypeName = $"KP{i}Row",
                }
            );
        }

        queries.AddRange(NestingQueries(forms, shapes, projectable, pinned));

        // 合成のクエリを、優先順位で決まる括弧を省いた描画でもう 1 本ずつ流す（構文木は同じ＝オラクルの答えも同じ。
        // 描画が変わらないクエリは流し直さない）
        var compounds = queries
            .Where(query =>
                !query.IsAtomQuery
                && DslFuzz.Render(query.Condition, DslGrouping.Minimal) != query.Text
            )
            .ToList();
        queries.AddRange(
            compounds.Select(query =>
                query.WithRendering(
                    query.Name + "g",
                    DslGrouping.Minimal,
                    query.Spelling,
                    RenameTree(query.Condition)
                )
            )
        );

        // 合成のクエリの綴りは番号で回す（乱数を引かずに、どの綴りもどの形の原子と一緒に出す）
        return queries
            .Select(
                (query, index) =>
                    query.IsAtomQuery
                        ? query
                        : query.WithRendering(
                            query.Name,
                            query.Grouping,
                            SpellingAt(index),
                            query.Condition
                        )
            )
            .ToList();
    }

    /// <summary>番号で回す綴り（大文字・小文字・先頭だけ大文字の順）</summary>
    private static DslSpelling SpellingAt(int index) =>
        (DslSpelling)(index % Enum.GetValues<DslSpelling>().Length);

    /// <summary>網羅の単位の NOT の入れ子のクエリの原子の数の上限</summary>
    private const int NestingAtomsPerQuery = 12;

    /// <summary>
    /// 網羅の単位の NOT の入れ子のクエリ：形・自身の否定・列の種類の全水準を足す原子（<see cref="CoveringForms"/>）を、
    /// NOT の入れ子の水準（<see cref="DslFuzzDimensions.NestingLevels"/>）ごとにその形で包んで OR でまとめた一覧のクエリ
    /// （1 つおきに外側をもう 1 つの NOT で包み、NOT の数の奇偶の両方を作る）と、一覧以外の戻り形・射影の非 NULL 指定ごとに
    /// 全水準を 1 つずつ持つクエリ
    /// </summary>
    /// <remarks>
    /// 水準ごとの包み方は <see cref="WrapForNesting"/>。乱数は引かない（パラメータ名を付け直すだけ）。
    /// </remarks>
    private List<DslQuery> NestingQueries(
        IReadOnlyList<DslAtom> forms,
        IReadOnlyList<QueryReturnShape> shapes,
        IReadOnlyList<DslColumn> projectable,
        DslColumn pinned
    )
    {
        var queries = new List<DslQuery>();
        var picked = CoveringForms(forms);

        for (var level = 0; level < DslFuzzDimensions.NestingLevels.Count; level++)
        {
            var nesting = DslFuzzDimensions.NestingLevels[level];
            var grouped =
                nesting is DslFuzzDimensions.NestingGroupAnd or DslFuzzDimensions.NestingGroupOr;
            var chunks = NestingChunks(WrapForNesting(nesting, picked), grouped);

            // 外側の NOT で包むものと包まないものを 1 つ以上ずつ作る（NOT の数の奇偶の両方）
            if (chunks.Count == 1)
            {
                chunks.Add(NestingChunks(WrapForNesting(nesting, picked), grouped)[0]);
            }

            for (var i = 0; i < chunks.Count; i++)
            {
                DslNode condition = new DslOr(chunks[i]);
                queries.Add(
                    new DslQuery
                    {
                        Name = $"KN{level}c{i}",
                        Condition = i % 2 == 1 ? new DslNot(condition) : condition,
                        Returns = QueryReturnShape.List,
                        IsAtomQuery = false,
                    }
                );
            }
        }

        foreach (var shape in shapes.Where(shape => shape != QueryReturnShape.List))
        {
            var name = $"KM{shape}";
            queries.Add(
                new DslQuery
                {
                    Name = name,
                    Condition = AllNestings(picked),
                    Returns = shape,
                    IsAtomQuery = false,
                    OrderDescending = true,
                    ProjectedColumn = shape == QueryReturnShape.Projection ? projectable[0] : null,
                    ResultTypeName = shape == QueryReturnShape.Projection ? name + "Row" : null,
                }
            );
        }

        queries.Add(
            new DslQuery
            {
                Name = "KMP",
                Condition = new DslAnd([
                    new DslNullCheck(IsNot: true) { Column = pinned },
                    AllNestings(picked),
                ]),
                Returns = QueryReturnShape.Projection,
                IsAtomQuery = false,
                ProjectedColumn = pinned,
                ProjectedNonNull = true,
                ResultTypeName = "KMPRow",
            }
        );

        return queries;
    }

    /// <summary>
    /// 原子をそれぞれ NOT の入れ子の水準の形で包む（none＝そのまま・single / double / triple＝NOT を 1・2・3 段・
    /// group-and / group-or＝隣り合う 2 つを AND / OR でつないで NOT で包む。数が奇数なら最後は先頭の原子と組む）
    /// </summary>
    private List<DslNode> WrapForNesting(string nesting, IReadOnlyList<DslAtom> atoms)
    {
        DslNode Not(DslNode node, int times) =>
            times == 0 ? node : Not(new DslNot(node), times - 1);

        return nesting switch
        {
            DslFuzzDimensions.NestingNone => atoms.Select(atom => (DslNode)Rename(atom)).ToList(),
            DslFuzzDimensions.NestingSingle => atoms.Select(atom => Not(Rename(atom), 1)).ToList(),
            DslFuzzDimensions.NestingDouble => atoms.Select(atom => Not(Rename(atom), 2)).ToList(),
            DslFuzzDimensions.NestingTriple => atoms.Select(atom => Not(Rename(atom), 3)).ToList(),
            _ => Enumerable
                .Range(0, (atoms.Count + 1) / 2)
                .Select(index =>
                {
                    List<DslNode> pair =
                    [
                        Rename(atoms[2 * index]),
                        Rename(atoms[(2 * index + 1) % atoms.Count]),
                    ];
                    return (DslNode)
                        new DslNot(
                            nesting == DslFuzzDimensions.NestingGroupAnd
                                ? new DslAnd(pair)
                                : new DslOr(pair)
                        );
                })
                .ToList(),
        };
    }

    /// <summary>
    /// 包んだ項目を、原子が <see cref="NestingAtomsPerQuery"/> 個までになるように分ける（項目 1 つだけの塊は前の塊へ足す＝
    /// 外側の NOT が 1 項目の OR を包むと、描画が NOT の 1 段と区別できない）
    /// </summary>
    private static List<List<DslNode>> NestingChunks(List<DslNode> items, bool grouped)
    {
        var chunks = items
            .Chunk(grouped ? NestingAtomsPerQuery / 2 : NestingAtomsPerQuery)
            .Select(chunk => chunk.ToList())
            .ToList();

        if (chunks.Count >= 2 && chunks[^1].Count == 1)
        {
            chunks[^2].AddRange(chunks[^1]);
            chunks.RemoveAt(chunks.Count - 1);
        }

        return chunks;
    }

    /// <summary>NOT の入れ子の全水準を 1 つずつ持つ条件（原子 8 つ。足りなければ先頭から使い回す）</summary>
    private DslNode AllNestings(IReadOnlyList<DslAtom> atoms)
    {
        DslAtom At(int index) => Rename(atoms[index % atoms.Count]);

        return new DslOr([
            At(0),
            new DslNot(At(1)),
            new DslNot(new DslNot(At(2))),
            new DslNot(new DslNot(new DslNot(At(3)))),
            new DslNot(new DslAnd([At(4), At(5)])),
            new DslNot(new DslOr([At(6), At(7)])),
        ]);
    }

    /// <summary>
    /// 網羅の単位の決まった形のクエリ：NOT の入れ子と NULL 許容の列の組み合わせの代表（NOT NOT 原子・NOT NOT NOT 原子・
    /// NOT (AND 群)・NOT (AND (比較, IS NULL))・NOT (OR 群)・前置 NOT と NOT LIKE の重ね）・整数の列の範囲の両端のリテラルと、<paramref name="ordinalStrings"/>
    /// なら合成済みの「が」の前方・後方・部分一致とソフトハイフンで始まる前方一致（序数で比べる実装先と、カルチャで比べると
    /// 結果が変わる値）
    /// </summary>
    /// <remarks>
    /// 列は NULL 許容の文字列 <c>s</c> と NULL 許容の int <c>i32</c>（どの方言の網羅の単位にもある）。乱数（値とパラメータ名）は
    /// この生成器から引く（網羅の単位の生成器＝種の列の外）。序数の値の行は呼び出し側が <see cref="DslFuzzUnits"/> で入れる。
    /// </remarks>
    /// <param name="columns">列</param>
    /// <param name="ordinalStrings">序数で比べる文字列一致のクエリも作るか（SQLite の単位だけ）</param>
    public List<DslQuery> FixedQueries(IReadOnlyList<DslColumn> columns, bool ordinalStrings)
    {
        var s = columns.Single(column => column.Name == "s");
        var i32 = columns.Single(column => column.Name == "i32");
        DslLike Like(bool negated, string shape) =>
            new(negated, LikeLiteral(s, shape)) { Column = s };
        DslCompare OnI32(DslOp op, bool literal) =>
            new(op, literal ? Literal(i32) : Param(i32)) { Column = i32 };
        DslNullCheck SIsNull() => new(IsNot: false) { Column = s };
        DslNode Not(DslNode node) => new DslNot(node);
        var exact = LikeLiteral(s, "exact");
        DslNode[] conditions =
        [
            // NOT NOT の順序比較（NULL 許容の列）・NOT NOT のワイルドカードなしの LIKE・NOT NOT / NOT NOT NOT の部分一致
            Not(Not(OnI32(DslOp.Lt, literal: false))),
            Not(Not(Like(false, "exact"))),
            Not(Not(Like(false, "contains"))),
            Not(Not(Not(Like(false, "contains")))),
            // NOT (比較 AND IS NULL)：パラメータとリテラル
            Not(new DslAnd([OnI32(DslOp.Ge, literal: false), SIsNull()])),
            Not(new DslAnd([OnI32(DslOp.Ne, literal: true), SIsNull()])),
            // NOT (LIKE OR 比較)・NOT (比較 OR 文字列の等値)
            Not(new DslOr([Like(false, "exact"), OnI32(DslOp.Eq, literal: true)])),
            Not(new DslOr([Like(false, "exact"), OnI32(DslOp.Lt, literal: false)])),
            Not(
                new DslOr([
                    OnI32(DslOp.Lt, literal: false),
                    new DslCompare(DslOp.Eq, StringLiteral(s, likeSafe: false)) { Column = s },
                ])
            ),
            // 肯定の LIKE と、同じ LIKE と IS NULL の OR の NOT
            new DslOr([
                new DslLike(false, exact) { Column = s },
                Not(new DslOr([new DslLike(false, exact) { Column = s }, SIsNull()])),
            ]),
            // 前置 NOT と NOT LIKE の重ね・前置 NOT の部分一致
            Not(Like(true, "contains")),
            Not(Like(false, "contains")),
            // 整数の列の型（値オブジェクトなら内包型）の範囲の両端のリテラル（型の検査が断らない端）
            EdgeLiterals(columns),
        ];
        var queries = conditions
            .Select(
                (condition, index) =>
                    new DslQuery
                    {
                        Name = $"KF{index}",
                        Condition = condition,
                        Returns = QueryReturnShape.List,
                        IsAtomQuery = false,
                    }
            )
            .ToList();

        if (ordinalStrings)
        {
            string[] patterns =
            [
                OrdinalComposed + "%",
                "%" + OrdinalComposed,
                "%" + OrdinalComposed + "%",
                OrdinalSoftHyphen + "a%",
            ];

            for (var i = 0; i < patterns.Length; i++)
            {
                queries.Add(
                    new DslQuery
                    {
                        Name = $"{OrdinalQueryPrefix}{i}",
                        Condition = new DslLike(
                            false,
                            new DslLiteral(patterns[i], DslFuzz.QuoteText(patterns[i]))
                        )
                        {
                            Column = s,
                        },
                        Returns = QueryReturnShape.List,
                        IsAtomQuery = false,
                    }
                );
            }
        }

        return queries;
    }

    /// <summary>
    /// byte・int・long の列（主キーでなく、どの方言の網羅の単位にもある型）の範囲の両端との等値を OR でつないだ条件
    /// （元の QueryConditionTypeCheckTests の受理の形 PlainEdges・LongEdges・値オブジェクトの VoEdges。値の池の乱数で選ぶ
    /// 境界値は種ごとに当たるとは限らないので、決まった形で毎回流す）
    /// </summary>
    private static DslNode EdgeLiterals(IReadOnlyList<DslColumn> columns)
    {
        var items = new List<DslNode>();

        foreach (
            var (clr, min, max) in new (DslClr, object, object)[]
            {
                (DslClr.Byte, byte.MinValue, byte.MaxValue),
                (DslClr.Int32, int.MinValue, int.MaxValue),
                (DslClr.Int64, long.MinValue, long.MaxValue),
            }
        )
        {
            var column = columns.First(column => column.Clr == clr && !column.PrimaryKey);

            foreach (var value in new[] { min, max })
            {
                items.Add(
                    new DslCompare(DslOp.Eq, new DslLiteral(value, DslFuzz.NumberText(value)))
                    {
                        Column = column,
                    }
                );
            }
        }

        return new DslOr(items);
    }

    /// <summary>
    /// 序数の比較を確かめるクエリ（合成済みと分解形の「が」・ソフトハイフンの LIKE）の名前の接頭辞（網羅の単位をカルチャを変えて
    /// 流し直すときは、このクエリだけを流す）
    /// </summary>
    public const string OrdinalQueryPrefix = "KO";

    /// <summary>合成済みの「が」（U+304C）</summary>
    public const string OrdinalComposed = "\u304C";

    /// <summary>分解形の「か＋濁点」（U+304B U+3099。カルチャで比べると合成済みと等しい）</summary>
    public const string OrdinalDecomposed = "\u304B\u3099";

    /// <summary>ソフトハイフン（U+00AD。カルチャで比べると無視される）</summary>
    public const string OrdinalSoftHyphen = "\u00AD";

    /// <summary>
    /// 形・自身の否定・列の種類のまだ出していない水準を足す原子を貪欲に選び、<see cref="CoverageAtomsPerQuery"/> 個ずつ
    /// OR でまとめた条件（1 つおきに NOT で包む）
    /// </summary>
    private List<DslNode> CoverageChunks(IReadOnlyList<DslAtom> forms)
    {
        var picked = CoveringForms(forms).Select(Rename).ToList();

        return picked
            .Chunk(CoverageAtomsPerQuery)
            .Select(chunk =>
                (DslNode)
                    new DslOr(
                        chunk
                            .Select(
                                (atom, index) => index % 2 == 1 ? new DslNot(atom) : (DslNode)atom
                            )
                            .ToList()
                    )
            )
            .ToList();
    }

    /// <summary>
    /// 形・自身の否定・列の種類・文字列のリテラルの水準のまだ出していない組を足す原子を、並びの先頭から貪欲に選ぶ
    /// （名前は付け直さない。文字列のリテラルの水準は形・否定・列の種類のそれぞれと組で数える）
    /// </summary>
    private static List<DslAtom> CoveringForms(IReadOnlyList<DslAtom> forms)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var picked = new List<DslAtom>();

        foreach (var atom in forms)
        {
            var quote = DslFuzz.QuoteOf(atom);
            string[] keys =
            [
                $"shape:{DslFuzzDimensions.ShapeOf(atom)}:{quote}",
                $"negated:{atom.InherentNegation}:{quote}",
                $"clr:{atom.Column.Clr}:{quote}",
            ];

            // Count は短絡しない（3 つの水準をすべて記録する）
            if (keys.Count(seen.Add) > 0)
            {
                picked.Add(atom);
            }
        }

        return picked;
    }

    /// <summary>列に使える全ての形の原子（値は乱数）</summary>
    /// <param name="column">列</param>
    /// <param name="bothQuotes">
    /// 文字列のリテラルの原子を、<c>'</c> を含む値と含まない値の 2 つずつ作るか（網羅の単位だけ。種の単位は値の池から
    /// 乱数で選ぶ）
    /// </param>
    private IEnumerable<DslAtom> AllAtoms(DslColumn column, bool bothQuotes = false)
    {
        // 文字列のリテラルの値の選び方（null は値の池全体から選ぶ）
        bool?[] quotes = bothQuotes && column.IsString ? [false, true] : [null];

        foreach (var negated in new[] { false, true })
        {
            // IS [NOT] NULL は NULL を許す列だけ（NOT NULL の列は生成時に断られる）
            if (column.Nullable && !column.PrimaryKey)
            {
                yield return new DslNullCheck(negated) { Column = column };
            }

            foreach (var shape in DslIn.ListShapes.Where(shape => CanHoldList(column, shape)))
            {
                yield return new DslIn(negated, ListParam(column, shape)) { Column = column };
            }
        }

        var ops = column.IsOrdered ? Enum.GetValues<DslOp>() : [DslOp.Eq, DslOp.Ne];
        var literalAllowed = column.IsString || column.IsInteger || column.Clr == DslClr.Decimal;

        foreach (var op in ops)
        {
            yield return new DslCompare(op, Param(column)) { Column = column };

            if (literalAllowed)
            {
                foreach (var quote in quotes)
                {
                    yield return new DslCompare(op, Literal(column, quote)) { Column = column };
                }
            }

            // 整数の列と小数のリテラル（docs：素の整数の列は数として比べ、整数の値オブジェクトの列は断る。断る形は
            // 原子のクエリでだけ作り、等値の 1 つで足りる。受け付ける形は x.5 の値と整数で結果の違う 3 つ＝
            // < と <=・> と >= は同じ結果になり、<> は = の NOT のクエリが作る）
            if (
                column.IsInteger
                && (valueObjects ? op == DslOp.Eq : DslFuzzDimensions.FractionalOps.Contains(op))
            )
            {
                yield return new DslCompare(op, FractionalLiteral())
                {
                    Column = column,
                    RejectedByTypeCheck = valueObjects,
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
                foreach (var quote in quotes)
                {
                    yield return new DslLike(negated, LikeLiteral(column, shape, quote))
                    {
                        Column = column,
                    };
                }
            }

            yield return new DslLike(negated, Param(column)) { Column = column };

            foreach (var match in Enum.GetValues<DslMatchKind>())
            {
                foreach (var quote in quotes)
                {
                    yield return new DslMatch(
                        match,
                        negated,
                        StringLiteral(column, likeSafe: false, quote)
                    )
                    {
                        Column = column,
                    };
                }

                yield return new DslMatch(match, negated, Param(column)) { Column = column };
            }
        }
    }

    /// <summary>列の型の範囲内のリテラル</summary>
    /// <param name="column">列</param>
    /// <param name="quote">文字列のリテラルの値に <c>'</c> を含めるか（null は値の池全体から選ぶ）</param>
    private DslOperand Literal(DslColumn column, bool? quote = null)
    {
        if (column.IsString)
        {
            return StringLiteral(column, likeSafe: false, quote);
        }

        var value = Pick(DslFuzz.ValuePool(column.Clr));

        return new DslLiteral(value, DslFuzz.NumberText(value));
    }

    /// <summary>
    /// 整数の列と比べる小数のリテラル（2 進で正確に表せる値＝double で比べても decimal のオラクルと食い違わない）
    /// </summary>
    private DslLiteral FractionalLiteral()
    {
        var value = Pick(FractionalPool);

        return new DslLiteral(value, DslFuzz.NumberText(value));
    }

    /// <summary>小数のリテラルの値（0.5 刻み・±2^53 の中）</summary>
    private static readonly decimal[] FractionalPool =
    [
        -1.5m,
        -0.5m,
        0.5m,
        1.5m,
        4.5m,
        127.5m,
        255.5m,
        32767.5m,
    ];

    /// <summary>LIKE のリテラル（% は先頭・末尾だけ）</summary>
    private DslLiteral LikeLiteral(DslColumn column, string shape, bool? quote = null)
    {
        var core = ((DslLiteral)StringLiteral(column, likeSafe: true, quote)).Value.ToString()!;
        var pattern = shape switch
        {
            "exact" => core,
            "prefix" => core + "%",
            "suffix" => "%" + core,
            _ => "%" + core + "%",
        };

        return new DslLiteral(pattern, DslFuzz.QuoteText(pattern));
    }

    private static DslQuery AtomQuery(string name, DslNode condition, DslSpelling spelling) =>
        new()
        {
            Name = name,
            Condition = condition,
            Spelling = spelling,
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
            Grouping = _random.Next(2) == 0 ? DslGrouping.Minimal : DslGrouping.Explicit,
            Spelling = (DslSpelling)_random.Next(Enum.GetValues<DslSpelling>().Length),
        };
    }

    /// <summary>条件の原子のパラメータ名をすべて付け直す（構文木の形は同じ）</summary>
    private DslNode RenameTree(DslNode node) =>
        node switch
        {
            DslAtom atom => Rename(atom),
            DslNot not => new DslNot(RenameTree(not.Inner)),
            DslAnd and => new DslAnd(and.Items.Select(RenameTree).ToList()),
            DslOr or => new DslOr(or.Items.Select(RenameTree).ToList()),
            _ => throw new ArgumentOutOfRangeException(nameof(node)),
        };

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
            () =>
                new DslIn(_random.Next(2) == 0, ListParam(column, PickListShape(column)))
                {
                    Column = column,
                },
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

            // 範囲外のリテラルは原子のクエリだけで作る（合成の条件に混ぜると、失敗のキーがシードで揺れる）。
            // 整数の列への小数のリテラルは、断られない素の列でだけ混ぜる
            if (
                column.IsInteger
                && !valueObjects
                && DslFuzzDimensions.FractionalOps.Contains(op)
                && _random.Next(4) == 0
            )
            {
                return new DslCompare(op, FractionalLiteral()) { Column = column };
            }

            var value = Pick(DslFuzz.ValuePool(column.Clr));

            return new DslCompare(op, new DslLiteral(value, DslFuzz.NumberText(value)))
            {
                Column = column,
            };
        }

        return new DslCompare(op, Param(column)) { Column = column };
    }

    /// <summary>
    /// 型の検査が断る整数のリテラル（docs の簡易 DSL の節: 列の型〔値オブジェクトなら内包型〕の範囲の外と、
    /// <c>ulong</c> を超える整数は、警告してそのクエリを飛ばす）。範囲の両端のすぐ外と、<c>ulong</c> の上限の次
    /// </summary>
    private static IEnumerable<string> RejectedLiterals(DslColumn column)
    {
        if (!column.IsInteger)
        {
            yield break;
        }

        var (min, max) = column.Clr switch
        {
            DslClr.Int64 => ((BigInteger)long.MinValue, (BigInteger)long.MaxValue),
            DslClr.Int32 => (int.MinValue, int.MaxValue),
            DslClr.Int16 => (short.MinValue, short.MaxValue),
            DslClr.SByte => (sbyte.MinValue, sbyte.MaxValue),
            DslClr.Byte => (byte.MinValue, byte.MaxValue),
            DslClr.UInt16 => (ushort.MinValue, ushort.MaxValue),
            DslClr.UInt32 => (uint.MinValue, uint.MaxValue),
            DslClr.UInt64 => (0, ulong.MaxValue),
            _ => throw new ArgumentOutOfRangeException(nameof(column)),
        };

        yield return (max + 1).ToString(CultureInfo.InvariantCulture);
        yield return (min - 1).ToString(CultureInfo.InvariantCulture);

        if (max + 1 != ulong.MaxValue + (BigInteger)1)
        {
            yield return (ulong.MaxValue + (BigInteger)1).ToString(CultureInfo.InvariantCulture);
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

        return new DslLiteral(pattern, DslFuzz.QuoteText(pattern));
    }

    /// <summary>文字列のリテラル（LIKE のパターンの芯なら % と _ を含まず空でないもの）</summary>
    /// <param name="column">列</param>
    /// <param name="likeSafe">LIKE のパターンの芯か</param>
    /// <param name="quote">値に ' を含めるか（null は値の池全体から選ぶ）</param>
    private DslOperand StringLiteral(DslColumn column, bool likeSafe, bool? quote = null)
    {
        var pool = DslFuzz
            .ValuePool(column.Clr)
            .Cast<string>()
            .Where(value =>
                !likeSafe || (value.Length > 0 && !value.Contains('%') && !value.Contains('_'))
            )
            .Where(value => quote is null || value.Contains('\'') == quote)
            .ToList();
        var value = Pick(pool);

        return new DslLiteral(value, DslFuzz.QuoteText(value));
    }

    private DslParam Param(DslColumn column) =>
        new(
            "p" + _parameterIndex++,
            [Pick(DslFuzz.ValuePool(column.Clr))],
            IsList: false,
            ByColumn: !DslFuzz.HasTypeToken(column.Clr) || _random.Next(2) == 0,
            column
        );

    /// <summary>
    /// リストに null を入れられるか（要素が参照型のリストだけ）：値オブジェクトなしの文字列の列のリスト（<c>IReadOnlyList&lt;string&gt;</c>）と、
    /// 値オブジェクトありで列で型付けしたリスト（<c>IReadOnlyList&lt;値オブジェクト&gt;</c>）
    /// </summary>
    /// <remarks>
    /// 値型のリスト（<c>IReadOnlyList&lt;int&gt;</c> など）は null を持てない。値オブジェクトの列へ型トークンで型付けしたリストは
    /// メソッドの冒頭で <c>Create</c> へ渡して持ち上げ、値オブジェクトは null を包まず null の入力を検証エラーにする
    /// （docs の値オブジェクトの節）ので、null を入れない。
    /// </remarks>
    private bool CanHoldNull(DslColumn column) => valueObjects || column.IsString;

    /// <summary>リストの形を作れる列か（null を含む形は <see cref="CanHoldNull"/> の列だけ）</summary>
    private bool CanHoldList(DslColumn column, string shape) =>
        shape is DslIn.ListValues or DslIn.ListEmpty || CanHoldNull(column);

    /// <summary>合成の条件の IN のリストの形（値の並ぶリストを多めに選ぶ）</summary>
    private string PickListShape(DslColumn column)
    {
        var shape = _random.Next(8) switch
        {
            0 => DslIn.ListEmpty,
            1 => DslIn.ListWithNull,
            2 => DslIn.ListAllNull,
            _ => DslIn.ListValues,
        };

        return CanHoldList(column, shape) ? shape : DslIn.ListValues;
    }

    /// <summary>IN のリストのパラメータ（null を含む形は、値オブジェクトありなら列で型付けする）</summary>
    private DslParam ListParam(DslColumn column, string shape)
    {
        var pool = DslFuzz.ValuePool(column.Clr);
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

        var nullable = shape is DslIn.ListWithNull or DslIn.ListAllNull;

        return new DslParam(
            "p" + _parameterIndex++,
            values,
            IsList: true,
            ByColumn: (nullable && valueObjects)
                || !DslFuzz.HasTypeToken(column.Clr)
                || _random.Next(2) == 0,
            column
        );
    }

    private T Pick<T>(IReadOnlyList<T> items) => items[_random.Next(items.Count)];
}
