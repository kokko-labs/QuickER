using QuickER.CodeGen.CSharp;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using static QuickER.Tests.CodeGen.CSharp.CodegenConfig.CodegenConfigDimensions;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>図の形の列の種類（方言ごとの型の表記へ写す）</summary>
internal enum ShapeType
{
    /// <summary>32 ビット整数</summary>
    Int,

    /// <summary>可変長文字列（20）</summary>
    String20,

    /// <summary>可変長文字列（50）</summary>
    String50,

    /// <summary>可変長文字列（100）</summary>
    String100,

    /// <summary>decimal(10,2)</summary>
    Decimal,

    /// <summary>行バージョンの水準の列（SQL Server 以外は日時）</summary>
    RowVersion,

    /// <summary>長さのあるバイナリ</summary>
    BoundedBinary,

    /// <summary>無制限バイナリ</summary>
    UnboundedBinary,
}

/// <summary>型の表記と、その表記について宣言する事実</summary>
/// <param name="Text">図に書く型の表記</param>
/// <param name="IsCanonical">
/// 図の方言の型カタログが同じ綴りへ書き戻すか（偽なら <c>[DbColumnMeta]</c> に元の表記が刻まれ、Info
/// <c>CodeGen_Info_NonCanonicalTypeSpellingColumns</c> が名指しする。根拠は各方言の <c>*TypeCatalog.TryFormat</c>）
/// </param>
/// <param name="IsUnboundedBinary">図の方言の型マッパーが無制限バイナリとして読むか</param>
internal sealed record ShapeSpelling(
    string Text,
    bool IsCanonical = true,
    bool IsUnboundedBinary = false
);

/// <summary>図の形の 1 列</summary>
/// <param name="Name">列名</param>
/// <param name="Type">列の種類</param>
/// <param name="Spelling">図の方言での表記と事実</param>
/// <param name="Column">図の列</param>
internal sealed record ShapeColumn(
    string Name,
    ShapeType Type,
    ShapeSpelling Spelling,
    Column Column
);

/// <summary>図の形の 1 表</summary>
/// <param name="Name">表名</param>
/// <param name="Stem">型名の幹（<c>customers</c> → <c>Customer</c>。docs「Generated type names」の単数形）</param>
/// <param name="Entity">図のエンティティ</param>
/// <param name="Columns">列</param>
internal sealed record ShapeTable(
    string Name,
    string Stem,
    Entity Entity,
    IReadOnlyList<ShapeColumn> Columns
)
{
    /// <summary>主キーが 1 列か（Repository 契約・同期の対象になる表）</summary>
    public bool HasSinglePrimaryKey => Columns.Count(c => c.Column.IsPrimaryKey) == 1;

    /// <summary>主キーの列</summary>
    public IReadOnlyList<ShapeColumn> PrimaryKey =>
        Columns.Where(c => c.Column.IsPrimaryKey).ToList();

    /// <summary>Entity の型名</summary>
    public string EntityClass => Stem + "Entity";
}

/// <summary>図の形の 1 関係</summary>
/// <param name="Parent">親の表</param>
/// <param name="Child">子の表</param>
/// <param name="Pairs">（親の列, 子の列）</param>
/// <param name="Type">関係の種類</param>
/// <param name="ParentNavigationOnChild">子の側の親参照ナビゲーションの名前（docs「Navigation property names」）</param>
internal sealed record ShapeRelation(
    ShapeTable Parent,
    ShapeTable Child,
    IReadOnlyList<(ShapeColumn Parent, ShapeColumn Child)> Pairs,
    RelationshipType Type,
    string? ParentNavigationOnChild
)
{
    /// <summary>複合外部キーか（生成はナビゲーションを作らず警告する）</summary>
    public bool IsComposite => Pairs.Count > 1;
}

/// <summary>
/// 図の形の水準から ER 図を組み立て、判定の規則が読む図の事実を持つ
/// </summary>
/// <remarks>
/// <para>
/// どの行にも基本の 2 表（<c>customers</c> 1 対多 <c>orders</c>）を置き、水準ごとに表・列・関係・一意制約・クエリを足す。
/// キーの形は主の表（<c>customers</c>）のキー。複合だけ・主キーなしの水準は、子の表のキーも変える（<see cref="KeyLevels.CompositeOnly"/>・<see cref="KeyLevels.KeylessOnly"/>）。
/// DB が値を作る列・バイナリ列・一意制約・クエリは子の表（<c>orders</c>）に置く。
/// </para>
/// <para>
/// 型の表記は図の方言で正しく解析できるもの（型カタログが読めて、フォールバックにならないもの）を選ぶ。
/// QuickER 版 Repository を方言の組で生成すると、図の表記を各方言の型マッパーが読む（型は変換しない＝docs「Database types」）。
/// マルチターゲットで方言どうしの C# 型が食い違うと生成時エラー（<c>CodeGen_Error_TypeMismatch</c>）になるので、
/// ここでは SQL Server と SQLite の型マッパーが同じ C# 型として読む表記を選ぶ（読めない表記はどちらも <c>string</c> へ倒れて一致する）。
/// 例外は行バージョンの水準の列で、SQL Server だけが <c>rowversion</c> / <c>timestamp</c> を行バージョンとして読み、
/// マルチターゲットでは行バージョンへ統一される（src/QuickER.CodeGen.CSharp/CLAUDE.md「マルチターゲット × rowversion」）。
/// </para>
/// <para>
/// Guid は表名・列名から決定的に作る（同じ行を流し直しても同じ図になる。CLI / MCP の入口は図をファイルへ保存して読み戻す）。
/// </para>
/// </remarks>
internal sealed class ShapeDiagram
{
    /// <summary>図の方言の水準（型解決のプロバイダの名前）</summary>
    public static IReadOnlyList<string> TypeDialects { get; } =
    [
        DialectNames.SqlServer,
        DialectNames.Sqlite,
        DialectNames.PostgreSql,
        DialectNames.MySql,
        DialectNames.Oracle,
    ];

    /// <summary>方言ごとの型の表記（根拠は各方言の型カタログ・型マッパーと docs「Database types and C# types」）</summary>
    private static readonly IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<ShapeType, ShapeSpelling>
    > Spellings = new Dictionary<string, IReadOnlyDictionary<ShapeType, ShapeSpelling>>(
        StringComparer.Ordinal
    )
    {
        [DialectNames.SqlServer] = new Dictionary<ShapeType, ShapeSpelling>
        {
            [ShapeType.Int] = new("int"),
            [ShapeType.String20] = new("nvarchar(20)"),
            [ShapeType.String50] = new("nvarchar(50)"),
            [ShapeType.String100] = new("nvarchar(100)"),
            [ShapeType.Decimal] = new("decimal(10,2)"),
            [ShapeType.RowVersion] = new("rowversion"),
            [ShapeType.BoundedBinary] = new("varbinary(16)"),
            [ShapeType.UnboundedBinary] = new("varbinary(max)", IsUnboundedBinary: true),
        },
        // SQLite の型カタログは SQL Server と同じ綴り（INT / NVARCHAR(n) / VARBINARY(n) / VARBINARY(MAX)）を書き戻す。
        // timestamp は日時の別名で、書き戻すと DATETIME2 になる（綴りが戻らない）
        [DialectNames.Sqlite] = new Dictionary<ShapeType, ShapeSpelling>
        {
            [ShapeType.Int] = new("INT"),
            [ShapeType.String20] = new("NVARCHAR(20)"),
            [ShapeType.String50] = new("NVARCHAR(50)"),
            [ShapeType.String100] = new("NVARCHAR(100)"),
            [ShapeType.Decimal] = new("DECIMAL(10,2)"),
            [ShapeType.RowVersion] = new("timestamp", IsCanonical: false),
            [ShapeType.BoundedBinary] = new("VARBINARY(16)"),
            [ShapeType.UnboundedBinary] = new("VARBINARY(MAX)", IsUnboundedBinary: true),
        },
        // PostgreSQL の整数は SQL Server と SQLite が同じ int と読む int で書く（integer は SQL Server では読めず SQLite では long になる）。
        // int は書き戻すと integer になる。長さのあるバイナリは無い（bytea は無制限）
        [DialectNames.PostgreSql] = new Dictionary<ShapeType, ShapeSpelling>
        {
            [ShapeType.Int] = new("int", IsCanonical: false),
            [ShapeType.String20] = new("varchar(20)"),
            [ShapeType.String50] = new("varchar(50)"),
            [ShapeType.String100] = new("varchar(100)"),
            [ShapeType.Decimal] = new("numeric(10,2)"),
            [ShapeType.RowVersion] = new("timestamp"),
            [ShapeType.BoundedBinary] = new("bytea", IsUnboundedBinary: true),
            [ShapeType.UnboundedBinary] = new("bytea", IsUnboundedBinary: true),
        },
        [DialectNames.MySql] = new Dictionary<ShapeType, ShapeSpelling>
        {
            [ShapeType.Int] = new("int"),
            [ShapeType.String20] = new("varchar(20)"),
            [ShapeType.String50] = new("varchar(50)"),
            [ShapeType.String100] = new("varchar(100)"),
            [ShapeType.Decimal] = new("decimal(10,2)"),
            [ShapeType.RowVersion] = new("timestamp"),
            [ShapeType.BoundedBinary] = new("varbinary(16)"),
            [ShapeType.UnboundedBinary] = new("longblob", IsUnboundedBinary: true),
        },
        // Oracle の無制限バイナリは LONG RAW で書く（BLOB は SQLite がバイナリ・SQL Server が string と読み、マルチターゲットで食い違う）。
        // LONG RAW は書き戻すと BLOB になる
        [DialectNames.Oracle] = new Dictionary<ShapeType, ShapeSpelling>
        {
            [ShapeType.Int] = new("NUMBER(10)"),
            [ShapeType.String20] = new("VARCHAR2(20)"),
            [ShapeType.String50] = new("VARCHAR2(50)"),
            [ShapeType.String100] = new("VARCHAR2(100)"),
            [ShapeType.Decimal] = new("NUMBER(10,2)"),
            [ShapeType.RowVersion] = new("TIMESTAMP"),
            [ShapeType.BoundedBinary] = new("RAW(16)"),
            [ShapeType.UnboundedBinary] = new(
                "LONG RAW",
                IsCanonical: false,
                IsUnboundedBinary: true
            ),
        },
    };

    /// <summary>名前付きクエリの名前（簡易 DSL）</summary>
    public const string DslQueryName = "FindFromOrderId";

    /// <summary>名前付きクエリの名前（生 SQL）</summary>
    public const string RawSqlQueryName = "CountAllRaw";

    /// <summary>名前付きクエリの名前（手動実装）</summary>
    public const string ManualQueryName = "CountAllManual";

    private readonly List<ShapeTable> _tables = [];
    private readonly List<ShapeRelation> _relations = [];
    private int _guidCounter;

    /// <summary>図の形の水準から図を組む</summary>
    /// <param name="levels">図の形の水準</param>
    public ShapeDiagram(ShapeLevels levels)
    {
        Levels = levels;
        var spellings = Spellings[levels.TypeDialect];
        // 主キーなしの水準（keyless-only）はどの表にも主キーを置かず、複合だけの水準（composite-only）は子の表のキーへ
        // 親のキーを含める（識別関係＝どの表も複合主キーになる）
        var keyless = levels.Key == KeyLevels.KeylessOnly;
        var identifying = levels.Key == KeyLevels.CompositeOnly;

        // 主の表（キーの形）
        var customerKey = levels.Key switch
        {
            KeyLevels.Composite or KeyLevels.CompositeOnly => new[]
            {
                ("region_code", ShapeType.Int),
                ("customer_no", ShapeType.Int),
            },
            // 宣言幅は GuidKey の探針の前提（Probes.GuidKeyDeclaredWidthValue）と同じ 50
            KeyLevels.String => new[] { ("customer_code", ShapeType.String50) },
            _ => new[] { ("customer_id", ShapeType.Int) },
        };

        var customerColumns = customerKey
            .Select(key =>
                NewColumn(
                    spellings,
                    key.Item1,
                    key.Item2,
                    primaryKey: !keyless,
                    nullable: levels.Key == KeyLevels.NullableInt
                )
            )
            .Append(NewColumn(spellings, "name", ShapeType.String50))
            .ToList();

        if (levels.Relation == RelationLevels.Self)
        {
            // 自己参照：主キーと同じ型の NULL 許容の外部キー（列名は主キーと違う＝値オブジェクトの統一が名指しされる）
            customerColumns.AddRange(
                customerKey.Select(key =>
                    NewColumn(
                        spellings,
                        "referrer_"
                            + key.Item1.Replace(
                                "customer_",
                                string.Empty,
                                StringComparison.Ordinal
                            ),
                        key.Item2,
                        nullable: true,
                        foreignKey: true
                    )
                )
            );
        }

        Customers = AddTable("customers", "Customer", customerColumns);

        // 主の表のキーの列（主キーなしの水準でも、関係はこの列を参照する）
        var customerKeyColumns = Customers.Columns.Take(customerKey.Length).ToList();

        // 子の表（外部キー・DB が値を作る列・バイナリ・一意制約）
        var orderColumns = new List<ShapeColumn>
        {
            NewColumn(spellings, "order_id", ShapeType.Int, primaryKey: !keyless),
        };
        orderColumns.AddRange(
            customerKey.Select(key =>
                NewColumn(
                    spellings,
                    key.Item1,
                    key.Item2,
                    primaryKey: identifying,
                    foreignKey: true
                )
            )
        );
        orderColumns.Add(NewColumn(spellings, "amount", ShapeType.Decimal));
        orderColumns.Add(NewColumn(spellings, "note", ShapeType.String100, nullable: true));

        switch (levels.DbGenerated)
        {
            case DbGeneratedLevels.RowVersion:
                RowVersionColumn = NewColumn(spellings, "row_version", ShapeType.RowVersion);
                orderColumns.Add(RowVersionColumn);
                break;

            case DbGeneratedLevels.Computed:
                ComputedColumn = NewColumn(spellings, "total", ShapeType.Decimal);
                ComputedColumn.Column.IsComputed = true;
                orderColumns.Add(ComputedColumn);
                break;

            case DbGeneratedLevels.Identity:
                IdentityColumn = NewColumn(spellings, "seq_no", ShapeType.Int);
                IdentityColumn.Column.IsIdentity = true;
                orderColumns.Add(IdentityColumn);
                break;
        }

        switch (levels.Binary)
        {
            case BinaryLevels.Bounded:
                BinaryColumn = NewColumn(
                    spellings,
                    "checksum",
                    ShapeType.BoundedBinary,
                    nullable: true
                );
                orderColumns.Add(BinaryColumn);
                break;

            case BinaryLevels.Unbounded:
                BinaryColumn = NewColumn(
                    spellings,
                    "attachment",
                    ShapeType.UnboundedBinary,
                    nullable: true
                );
                orderColumns.Add(BinaryColumn);
                break;
        }

        var uniqueMembers = new List<ShapeColumn>();

        if (levels.Unique != UniqueLevels.None)
        {
            var orderNo = NewColumn(spellings, "order_no", ShapeType.String20);
            orderColumns.Add(orderNo);
            uniqueMembers.Add(orderNo);

            if (levels.Unique == UniqueLevels.CompositeNullable)
            {
                var branchNo = NewColumn(spellings, "branch_no", ShapeType.Int, nullable: true);
                orderColumns.Add(branchNo);
                uniqueMembers.Add(branchNo);
            }
        }

        Orders = AddTable("orders", "Order", orderColumns);

        if (uniqueMembers.Count > 0)
        {
            Orders.Entity.UniqueConstraints.Add(
                new UniqueConstraint
                {
                    Id = NextGuid(),
                    ColumnIds = uniqueMembers.Select(column => column.Column.Id).ToList(),
                }
            );
        }

        AddRelation(
            Customers,
            Orders,
            customerKeyColumns.Zip(Orders.Columns.Where(c => c.Column.IsForeignKey)).ToList(),
            RelationshipType.OneToMany,
            "Customer"
        );

        switch (levels.Relation)
        {
            case RelationLevels.Self:
                AddRelation(
                    Customers,
                    Customers,
                    customerKeyColumns
                        .Zip(Customers.Columns.Where(c => c.Column.IsForeignKey))
                        .ToList(),
                    RelationshipType.OneToMany,
                    null
                );
                break;

            case RelationLevels.OneToOne:
                var profileColumns = new List<ShapeColumn>
                {
                    NewColumn(spellings, "profile_id", ShapeType.Int, primaryKey: !keyless),
                };
                profileColumns.AddRange(
                    customerKey.Select(key =>
                        NewColumn(
                            spellings,
                            key.Item1,
                            key.Item2,
                            primaryKey: identifying,
                            foreignKey: true
                        )
                    )
                );
                profileColumns.Add(
                    NewColumn(spellings, "bio", ShapeType.String100, nullable: true)
                );
                var profiles = AddTable("customer_profiles", "CustomerProfile", profileColumns);
                AddRelation(
                    Customers,
                    profiles,
                    customerKeyColumns
                        .Zip(profiles.Columns.Where(c => c.Column.IsForeignKey))
                        .ToList(),
                    RelationshipType.OneToOne,
                    "Customer"
                );
                break;

            case RelationLevels.Chain:
                var lineColumns = new List<ShapeColumn>
                {
                    NewColumn(spellings, "line_id", ShapeType.Int, primaryKey: !keyless),
                };
                // 子の表のキーの列（主キーなしの水準では order_id。複合だけの水準では親のキーを含む複合主キーの全列）
                IReadOnlyList<ShapeColumn> orderKeyColumns = keyless
                    ? [Orders.Columns[0]]
                    : Orders.PrimaryKey;
                lineColumns.AddRange(
                    orderKeyColumns.Select(key =>
                        NewColumn(
                            spellings,
                            key.Name,
                            key.Type,
                            primaryKey: identifying,
                            foreignKey: true
                        )
                    )
                );
                lineColumns.Add(NewColumn(spellings, "quantity", ShapeType.Int));
                var lines = AddTable("order_lines", "OrderLine", lineColumns);
                AddRelation(
                    Orders,
                    lines,
                    orderKeyColumns.Zip(lineColumns.Skip(1).Take(orderKeyColumns.Count)).ToList(),
                    RelationshipType.OneToMany,
                    "Order"
                );
                break;
        }

        Diagram = new ErDiagram
        {
            TargetDbms = levels.TypeDialect,
            Entities = _tables.Select(table => table.Entity).ToList(),
            Relationships = _relations
                .Select(relation => new Relationship
                {
                    Id = NextGuid(),
                    SourceEntityId = relation.Parent.Entity.Id,
                    TargetEntityId = relation.Child.Entity.Id,
                    Type = relation.Type,
                    ColumnPairs = relation
                        .Pairs.Select(pair => new RelationshipColumnPair(
                            pair.Parent.Column.Id,
                            pair.Child.Column.Id
                        ))
                        .ToList(),
                })
                .ToList(),
            Queries = BuildQueries(levels.Query, Orders),
        };
    }

    /// <summary>図の形の水準</summary>
    public ShapeLevels Levels { get; }

    /// <summary>組んだ図</summary>
    public ErDiagram Diagram { get; }

    /// <summary>表（図の並び）</summary>
    public IReadOnlyList<ShapeTable> Tables => _tables;

    /// <summary>関係（図の並び）</summary>
    public IReadOnlyList<ShapeRelation> Relations => _relations;

    /// <summary>主の表</summary>
    public ShapeTable Customers { get; }

    /// <summary>子の表</summary>
    public ShapeTable Orders { get; }

    /// <summary>子の表に付く名前付きクエリの名前（水準 none なら null。図はクエリを 1 本だけ持つ）</summary>
    public string? QueryName =>
        Levels.Query switch
        {
            QueryLevels.Dsl => DslQueryName,
            QueryLevels.RawSql => RawSqlQueryName,
            QueryLevels.Manual => ManualQueryName,
            _ => null,
        };

    /// <summary>行バージョンの水準の列（無ければ null）</summary>
    public ShapeColumn? RowVersionColumn { get; }

    /// <summary>計算列（無ければ null）</summary>
    public ShapeColumn? ComputedColumn { get; }

    /// <summary>主キーでない採番列（無ければ null）</summary>
    public ShapeColumn? IdentityColumn { get; }

    /// <summary>バイナリ列（無ければ null）</summary>
    public ShapeColumn? BinaryColumn { get; }

    /// <summary>
    /// インメモリのシーダーが投入する表ごとの行数（Entity の型名 → 行数。<see cref="CodegenProbe.InMemorySeed"/> の期待）
    /// </summary>
    /// <remarks>
    /// src/QuickER.CodeGen.CSharp/CLAUDE.md「インメモリのシード値」と生成物の <c>InMemorySampleData</c> の説明から書く：
    /// 主キーが 1 列でない表は入れず、それを直接・間接に参照する表も入れない（外部キーの NULL 許容に依らない）。
    /// ほかは <c>RowsPerEntity</c>（3）行（図の形の表は整数・文字列の列だけで、行数を減らす一意制約を持たない）。
    /// 図の形では、主の表のキーが複合（Key=composite）なら、どの表も主の表へぶら下がるのですべて 0 行になる。
    /// </remarks>
    public IReadOnlyDictionary<string, int> SeedRowCounts
    {
        get
        {
            var unseeded = _tables.Where(table => !table.HasSinglePrimaryKey).ToHashSet();
            bool changed;

            do
            {
                changed = false;

                foreach (var relation in _relations.Where(r => unseeded.Contains(r.Parent)))
                {
                    changed |= unseeded.Add(relation.Child);
                }
            } while (changed);

            return _tables.ToDictionary(
                table => table.EntityClass,
                table => unseeded.Contains(table) ? 0 : 3,
                StringComparer.Ordinal
            );
        }
    }

    /// <summary>無制限バイナリの列（図の方言の型マッパーが無制限と読むもの）</summary>
    public IEnumerable<ShapeColumn> UnboundedBinaryColumns =>
        _tables.SelectMany(table => table.Columns).Where(c => c.Spelling.IsUnboundedBinary);

    /// <summary>
    /// 行バージョンの水準の列が、生成される Entity で行バージョン（<c>byte[]</c>・<c>[StoreGeneratedColumn]</c>）になるか
    /// </summary>
    /// <remarks>
    /// SQL Server の図では型マッパーが <c>rowversion</c> を行バージョンとして読む。ほかの方言の図では日時の列のままだが、
    /// QuickER 版 Repository を SQL Server を含む 2 方言以上で生成すると、SQL Server が <c>timestamp</c> を行バージョンとして読むので、
    /// 共有 Entity がその解決へ統一される（src/QuickER.CodeGen.CSharp/CLAUDE.md「マルチターゲット × rowversion」の 1. 型統合。
    /// 方言の辞書が 2 つ以上のときだけ統一する）。
    /// </remarks>
    /// <param name="options">実効の生成オプション</param>
    public bool RowVersionIsStoreGenerated(CodeGenerationOptions options) =>
        RowVersionColumn is not null
        && (
            Levels.TypeDialect == DialectNames.SqlServer
            || RowVersionIsUnifiedAcrossDialects(options)
        );

    /// <summary>行バージョンの水準の列がマルチターゲットの型統合で行バージョンへ統一されるか</summary>
    /// <param name="options">実効の生成オプション</param>
    public bool RowVersionIsUnifiedAcrossDialects(CodeGenerationOptions options) =>
        RowVersionColumn is not null
        && options.GenerateRepositories
        && CodegenOptionFacts.Dialects(options).Count >= 2
        && CodegenOptionFacts.Dialects(options).Contains(DialectNames.SqlServer);

    /// <summary>
    /// 綴りが図の方言の型カタログで同じ表記へ書き戻らない列があるか（行バージョンへ統一された列は除く＝行バージョン列は
    /// 中立トークンも元の表記も刻まない。src/QuickER.CodeGen.CSharp/CLAUDE.md「同義の型表記の保存」）
    /// </summary>
    /// <param name="options">実効の生成オプション</param>
    public bool HasNonCanonicalSpelling(CodeGenerationOptions options) =>
        _tables
            .SelectMany(table => table.Columns)
            .Any(column =>
                !column.Spelling.IsCanonical
                && !(column == RowVersionColumn && RowVersionIsStoreGenerated(options))
            );

    /// <summary>
    /// 手動で実装する名前付きクエリ（生成が実装を出さない実装先）を、利用者がそうするのと同じ partial クラスで書いたソース
    /// </summary>
    /// <remarks>
    /// src/QuickER.CodeGen.CSharp/Queries/CLAUDE.md「構造」：SQL（または DSL 共有本体）が与えられていない実装先は手動実装で、
    /// 契約宣言だけが生成され、実装は利用者が partial クラスへ書く（漏れはコンパイルエラー）。EF Core × 生 SQL・インメモリ × 生 SQL と、
    /// 手動実装のクエリのすべての実装先が該当する。生 SQL は SQL Server と SQLite の SQL を持つので、QuickER 版 Repository は実装を持つ。
    /// 置き場所（名前空間・層）は名前空間の規則（<see cref="CodegenNamespaces"/>）と同じものを使う。
    /// </remarks>
    /// <param name="options">実効の生成オプション</param>
    public IReadOnlyList<GeneratedFile> ManualSources(CodeGenerationOptions options)
    {
        // 簡易 DSL は全実装先へ共有の本体が出るので、手で書く実装が要るのは生 SQL と手動実装だけ
        var methodName = Levels.Query == QueryLevels.Dsl ? null : QueryName;

        // 名前付きクエリは Repository に付く。子の表の主キーが 1 列でなければ Repository が無いので、手で書く実装も無い
        if (
            methodName is null
            || !CodegenOptionFacts.GeneratesRepositoryContract(options)
            || !Orders.HasSinglePrimaryKey
        )
        {
            return [];
        }

        var namespaces = new CodegenNamespaces(options);
        var classes = new List<(string Namespace, string ClassName)>();
        var stem = Orders.Stem;

        if (Levels.Query == QueryLevels.Manual && options.GenerateRepositories)
        {
            classes.AddRange(
                CodegenOptionFacts
                    .Dialects(options)
                    .Select(dialect =>
                        (namespaces.DialectImplementation(dialect), stem + "Repository")
                    )
            );
        }

        if (options.GenerateEfCoreRepositories)
        {
            classes.Add((namespaces.EfCore, "EfCore" + stem + "Repository"));
        }

        if (options.GenerateInMemoryRepositories)
        {
            classes.Add((namespaces.InMemory, "InMemory" + stem + "Repository"));
        }

        return classes
            .Select(
                (target, index) =>
                    new GeneratedFile
                    {
                        FileName = string.Create(
                            System.Globalization.CultureInfo.InvariantCulture,
                            $"Manual{index}.{target.ClassName}.cs"
                        ),
                        RelativeDirectory = namespaces.InfrastructureDirectory,
                        Content = $$"""
                            namespace {{target.Namespace}}
                            {
                                partial class {{target.ClassName}}
                                {
                                    /// <summary>Hand-written implementation of the named query.</summary>
                                    /// <param name="cancellationToken">The cancellation token.</param>
                                    /// <returns>Always zero.</returns>
                                    public global::System.Threading.Tasks.Task<int> {{methodName}}Async(
                                        global::System.Threading.CancellationToken cancellationToken = default
                                    ) => global::System.Threading.Tasks.Task.FromResult(0);
                                }
                            }
                            """,
                    }
            )
            .ToList();
    }

    private ShapeColumn NewColumn(
        IReadOnlyDictionary<ShapeType, ShapeSpelling> spellings,
        string name,
        ShapeType type,
        bool primaryKey = false,
        bool nullable = false,
        bool foreignKey = false
    )
    {
        var spelling = spellings[type];
        return new ShapeColumn(
            name,
            type,
            spelling,
            new Column
            {
                Id = NextGuid(),
                Name = name,
                DataType = spelling.Text,
                IsPrimaryKey = primaryKey,
                IsNullable = nullable,
                IsForeignKey = foreignKey,
            }
        );
    }

    private ShapeTable AddTable(string name, string stem, List<ShapeColumn> columns)
    {
        var table = new ShapeTable(
            name,
            stem,
            new Entity
            {
                Id = NextGuid(),
                TableName = name,
                Columns = columns.Select(column => column.Column).ToList(),
            },
            columns
        );
        _tables.Add(table);
        return table;
    }

    private void AddRelation(
        ShapeTable parent,
        ShapeTable child,
        IReadOnlyList<(ShapeColumn Parent, ShapeColumn Child)> pairs,
        RelationshipType type,
        string? parentNavigationOnChild
    ) => _relations.Add(new ShapeRelation(parent, child, pairs, type, parentNavigationOnChild));

    private List<QueryDefinition> BuildQueries(string query, ShapeTable orders)
    {
        // order_id は子の表の先頭の列（主キーなしの水準では主キーでない）
        var orderId = orders.Columns[0].Column;

        return query switch
        {
            QueryLevels.Dsl =>
            [
                new QueryDefinition
                {
                    Id = NextGuid(),
                    EntityId = orders.Entity.Id,
                    Name = DslQueryName,
                    Condition = "order_id >= @minId",
                    Parameters =
                    [
                        new QueryParameter { Name = "minId", SourceColumnId = orderId.Id },
                    ],
                    Returns = QueryReturnShape.List,
                },
            ],
            QueryLevels.RawSql =>
            [
                new QueryDefinition
                {
                    Id = NextGuid(),
                    EntityId = orders.Entity.Id,
                    Name = RawSqlQueryName,
                    Returns = QueryReturnShape.Count,
                    Implementation = QueryImplementationKind.Sql,
                    Sql = new Dictionary<string, string>
                    {
                        [DialectNames.SqlServer] = "SELECT COUNT(*) FROM orders",
                        [DialectNames.Sqlite] = "SELECT COUNT(*) FROM orders",
                    },
                },
            ],
            QueryLevels.Manual =>
            [
                new QueryDefinition
                {
                    Id = NextGuid(),
                    EntityId = orders.Entity.Id,
                    Name = ManualQueryName,
                    Returns = QueryReturnShape.Count,
                    Implementation = QueryImplementationKind.Manual,
                },
            ],
            _ => [],
        };
    }

    private Guid NextGuid()
    {
        _guidCounter++;
        var bytes = new byte[16];
        BitConverter.GetBytes(_guidCounter).CopyTo(bytes, 0);
        bytes[15] = 0x5A;
        return new Guid(bytes);
    }
}
