using QuickER.Dialects;
using QuickER.Model;
using static QuickER.Tests.Provider.DdlModel;
using E = QuickER.Tests.Provider.DialectDdlElements;
using F = QuickER.Tests.Provider.DialectDdlFacets;
using O = QuickER.Tests.Provider.DialectDdlOutputs;

namespace QuickER.Tests.Provider;

/// <summary>5 方言の DDL 生成</summary>
internal static partial class DialectDdlRows
{
    /// <summary>MySQL の SET DEFAULT を省いた注記</summary>
    private const string MySqlSetDefaultNote =
        "-- Note: MySQL (InnoDB) does not support SET DEFAULT; using NO ACTION";

    /// <summary>Oracle の ON UPDATE を省いた注記（製品の定数の値）</summary>
    private const string OracleOnUpdateNote =
        "-- Note: Oracle does not support ON UPDATE; ignoring it";

    /// <summary>Oracle の SET DEFAULT を省いた注記（製品の定数の値）</summary>
    private const string OracleSetDefaultNote =
        "-- Note: Oracle does not support ON DELETE SET DEFAULT; using NO ACTION";

    /// <summary>主キーの列（NULL 許容を指定しない書き方：既定のまま）</summary>
    private static Column PkDefault(string name, string type) =>
        new()
        {
            Name = name,
            DataType = type,
            IsPrimaryKey = true,
        };

    /// <summary>説明つきの列</summary>
    private static Column Described(Column column, string description)
    {
        column.Description = description;
        return column;
    }

    /// <summary>親と子を 1 本の 1 対多で結ぶ図（子の <paramref name="childColumn"/> 番目の列が外部キー）</summary>
    private static ErDiagram OneToMany(
        Entity parent,
        Entity child,
        int childColumn,
        Action<Relationship>? configure = null
    )
    {
        var relationship = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parent.Columns[0].Id, child.Columns[childColumn].Id)],
        };
        configure?.Invoke(relationship);

        var diagram = Diagram(parent, child);
        diagram.Relationships.Add(relationship);
        return diagram;
    }

    /// <summary>親（id 主キー）と子（id 主キー・parent_id）の図</summary>
    private static ErDiagram ParentChild(string dialect, Action<Relationship> configure)
    {
        var type = ShopsIntType[dialect];
        return OneToMany(
            Table("parent", PkDefault("id", type)),
            Table("child", PkDefault("id", type), Col("parent_id", type)),
            1,
            configure
        );
    }

    /// <summary>親（id 主キー）と、主キーを持たず parent_id だけの子の図</summary>
    private static ErDiagram ParentWithKeylessChild(string dialect, Action<Relationship> configure)
    {
        var type = ShopsIntType[dialect];
        return OneToMany(
            Table("parent", PkDefault("id", type)),
            Table("child", Col("parent_id", type)),
            0,
            configure
        );
    }

    /// <summary>期待を方言ごとに（方言を受けて）書く</summary>
    private static DialectDdlRow ExpectWithDialect(
        this DialectDdlRow row,
        IEnumerable<string> dialects,
        Action<string, DdlExpect> expect
    )
    {
        foreach (var dialect in dialects)
        {
            row.Expect(dialect, e => expect(dialect, e));
        }

        return row;
    }

    private static IEnumerable<DialectDdlRow> GeneratorRows() =>
        [
            .. TableAndKeyRows(),
            .. ForeignKeyRows(),
            .. QuoteRows(),
            .. DescriptionRows(),
            .. SqliteOnlyRows(),
        ];

    /// <summary>CREATE TABLE・主キー</summary>
    private static IEnumerable<DialectDdlRow> TableAndKeyRows() =>
        [
            // SQL Server 以外の 4 方言
            DialectDdlRow
                .Ddl(
                    "ddl-table-create-table-and-pk",
                    "Build: CREATE TABLE と PRIMARY KEY が出力される",
                    E.Table,
                    F.StatementShape,
                    d =>
                        Diagram(
                            Table(
                                "users",
                                Pk("id", ShopsIntType[d]),
                                Col("name", VarcharType(d, 50), nullable: true)
                            )
                        )
                )
                .ExpectWithDialect(
                    [Pg, My, Or, Sl],
                    (d, e) =>
                        e.Lines(
                            "table users",
                            $"column users.id {ShopsIntType[d]} NOT NULL*",
                            "pk users PK_users (id)*",
                            $"column users.name {VarcharType(d, 50)} NULL*"
                        )
                )
                // SQLite だけ：宣言型を保ち AUTOINCREMENT を出さない
                .Expect(Sl, e => e.NoText("*AUTOINCREMENT*")),
            // SQL Server は表名 User・列 Id / Name の図
            DialectDdlRow
                .Ddl(
                    "ddl-table-create-table-and-pk-user",
                    "Build: CREATE TABLE と PRIMARY KEY が出力される（SQL Server・表名 User）",
                    E.Table,
                    F.StatementShape,
                    d => Diagram(Table("User", Pk("Id", "int"), Col("Name", "nvarchar(50)", true)))
                )
                .Expect(
                    Ss,
                    e =>
                        e.Lines(
                            "table User",
                            "column User.Id int NOT NULL*",
                            "pk User * (Id)*",
                            "column User.Name nvarchar(50) NULL*"
                        )
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-column-not-null-without-pk",
                    "Build: NULL 許容 OFF の列は NOT NULL が出力される",
                    E.Column,
                    F.StatementShape,
                    d => Diagram(Table("User", Col("Code", "nvarchar(20)", nullable: false)))
                )
                .Expect(Ss, e => e.Lines("column User.Code nvarchar(20) NOT NULL*")),
            DialectDdlRow
                .Ddl(
                    "ddl-pk-composite-combined",
                    "Build: 複合 PK は列を並べた PRIMARY KEY を生成する",
                    E.PrimaryKey,
                    F.StatementShape,
                    d =>
                        Diagram(
                            Table(
                                "order_items",
                                PkDefault("order_id", ShopsIntType[d]),
                                PkDefault("line_no", ShopsIntType[d])
                            )
                        )
                )
                .Expect(
                    [Pg, My, Or, Sl],
                    e => e.Lines("pk order_items PK_order_items (order_id, line_no)*")
                )
                .Expect(Sl, e => e.NoText("*AUTOINCREMENT*")),
            DialectDdlRow
                .Ddl(
                    "ddl-table-without-engine-clause",
                    "Build: ENGINE 句を出力しない",
                    E.Table,
                    F.Omit,
                    d => Diagram(Table("users", PkDefault("id", ShopsIntType[d])))
                )
                .Expect(My, e => e.NoText("*ENGINE*")),
            DialectDdlRow
                .Ddl(
                    "ddl-pk-single-non-integer-inline",
                    "Build: 非整数の単一 PK はインライン PRIMARY KEY 制約になる",
                    E.PrimaryKey,
                    F.StatementShape,
                    d => Diagram(Table("docs", PkDefault("code", "NVARCHAR(20)")))
                )
                .Expect(Sl, e => e.NoText("*AUTOINCREMENT*").Lines("pk docs PK_docs (code)*")),
            DialectDdlRow
                .Ddl(
                    "ddl-pk-schema-qualified-sqlserver",
                    "Build: schema.table 形式は [schema].[table] に分割され、PK 制約名は安全な名前になる",
                    E.SchemaQualified,
                    F.ConstraintName,
                    d => Diagram(Table("dbo.User", Pk("Id", "int")))
                )
                .Expect(Ss, e => e.Lines("table dbo.User", "pk dbo.User PK_dbo_User (Id)*")),
            DialectDdlRow
                .Ddl(
                    "ddl-pk-schema-qualified-postgresql",
                    "Build: schema.table 形式は \"schema\".\"table\" へ分割される",
                    E.SchemaQualified,
                    F.ConstraintName,
                    d => Diagram(Table("public.users", PkDefault("id", ShopsIntType[d])))
                )
                .Expect(
                    Pg,
                    e => e.Lines("table public.users", "pk public.users PK_public_users (id)*")
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-pk-schema-qualified-mysql",
                    "Build: schema.table 形式は `schema`.`table` へ分割される",
                    E.SchemaQualified,
                    F.ConstraintName,
                    d => Diagram(Table("shop.users", PkDefault("id", ShopsIntType[d])))
                )
                .Expect(My, e => e.Lines("table shop.users", "pk shop.users PK_shop_users (id)*")),
        ];

    /// <summary>外部キー・参照アクション</summary>
    private static IEnumerable<DialectDdlRow> ForeignKeyRows() =>
        [
            DialectDdlRow
                .Ddl(
                    "ddl-fk-one-to-many-with-actions",
                    "Build: 1対多リレーションが FOREIGN KEY と参照アクションを生成する",
                    E.ForeignKey,
                    F.StatementShape,
                    d =>
                        ParentChild(
                            d,
                            r =>
                            {
                                r.ConstraintName = "FK_child_parent";
                                r.OnDelete = ForeignKeyReferentialAction.Cascade;
                                r.OnUpdate = ForeignKeyReferentialAction.SetNull;
                            }
                        )
                )
                .Expect(
                    [Pg, My],
                    e =>
                        e.Lines(
                            "fk child FK_child_parent (parent_id) -> parent (id) on-delete=CASCADE on-update=SET NULL alter"
                        )
                )
                .Expect(My, e => e.NoText("*" + MySqlSetDefaultNote + "*"))
                // Oracle は ON UPDATE を持たないので句を出さず注記を添える
                .Expect(
                    Or,
                    e =>
                        e.Lines(
                                "fk child FK_child_parent (parent_id) -> parent (id) on-delete=CASCADE alter"
                            )
                            .NoText("*ON DELETE CASCADE ON UPDATE*")
                            .Text(OracleOnUpdateNote)
                )
                // SQLite は子の CREATE TABLE の中へインラインで書く
                .Expect(
                    Sl,
                    e =>
                        e.Lines(
                                "fk child FK_child_parent (parent_id) -> parent (id) on-delete=CASCADE on-update=SET NULL inline*"
                            )
                            .NoText("*ALTER TABLE*")
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-fk-schema-unqualified-sqlserver-default-name",
                    "Build: 1対多リレーションが FOREIGN KEY を生成する（SQL Server・名前と参照アクションなし）",
                    E.ForeignKey,
                    F.StatementShape,
                    d =>
                        OneToMany(
                            Table("P", PkDefault("Id", "int")),
                            Table("C", PkDefault("Id", "int"), Col("ParentId", "int")),
                            1
                        )
                )
                .Expect(Ss, e => e.Lines("fk C * (ParentId) -> P (Id) alter")),
            DialectDdlRow
                .Ddl(
                    "ddl-fk-constraint-name-and-actions-sqlserver",
                    "Build: 制約名と参照アクションが FOREIGN KEY に出力される",
                    E.ForeignKey,
                    F.ConstraintName,
                    d =>
                        OneToMany(
                            Table("Parent", PkDefault("Id", "int")),
                            Table("Child", PkDefault("Id", "int"), Col("ParentId", "int")),
                            1,
                            r =>
                            {
                                r.ConstraintName = "FK_Child_Parent_Custom";
                                r.OnDelete = ForeignKeyReferentialAction.Cascade;
                                r.OnUpdate = ForeignKeyReferentialAction.SetNull;
                            }
                        )
                )
                .Expect(
                    Ss,
                    e =>
                        e.Lines(
                            "fk Child FK_Child_Parent_Custom (ParentId) -> Parent (Id) on-delete=CASCADE on-update=SET NULL alter"
                        )
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-fk-schema-qualified-sqlserver-default-name",
                    "Build: schema 修飾された親子の FOREIGN KEY は分割括弧付けと安全な既定制約名で出力される",
                    E.SchemaQualified,
                    F.ConstraintName,
                    d =>
                        OneToMany(
                            Table("dbo.P", PkDefault("Id", "int")),
                            Table("dbo.C", PkDefault("Id", "int"), Col("ParentId", "int")),
                            1
                        )
                )
                .Expect(Ss, e => e.Lines("fk dbo.C FK_dbo_C_dbo_P (ParentId) -> dbo.P (Id) alter")),
            DialectDdlRow
                .Ddl(
                    "ddl-fk-set-default-omitted-with-note-mysql",
                    "Build: SET DEFAULT は句を出さず注意コメントを添える",
                    E.ForeignKey,
                    F.Note,
                    d =>
                        ParentChild(
                            d,
                            r =>
                            {
                                r.ConstraintName = "FK_child_parent";
                                r.OnDelete = ForeignKeyReferentialAction.SetDefault;
                                r.OnUpdate = ForeignKeyReferentialAction.SetDefault;
                            }
                        )
                )
                .Expect(
                    My,
                    e =>
                        e.NoText("*SET DEFAULT *")
                            .Sequence(
                                O.Text,
                                MySqlSetDefaultNote,
                                "ALTER TABLE `child` ADD CONSTRAINT `FK_child_parent`*"
                            )
                            .Text("*REFERENCES `parent` (`id`);")
                            .Lines("fk child FK_child_parent (parent_id) -> parent (id) alter")
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-fk-on-delete-set-null-oracle",
                    "Build: ON DELETE SET NULL が出力される",
                    E.ForeignKey,
                    F.StatementShape,
                    d =>
                        ParentWithKeylessChild(
                            d,
                            r =>
                            {
                                r.OnDelete = ForeignKeyReferentialAction.SetNull;
                                r.OnUpdate = ForeignKeyReferentialAction.NoAction;
                            }
                        )
                )
                .Expect(
                    Or,
                    e =>
                        e.Text("*ON DELETE SET NULL*")
                            .NoText("*Oracle does not support ON UPDATE*")
                            .NoText("*" + OracleSetDefaultNote + "*")
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-fk-on-delete-set-default-omitted-with-note-oracle",
                    "Build: ON DELETE SET DEFAULT は句を省いて注意コメントを付ける",
                    E.ForeignKey,
                    F.Note,
                    d =>
                        ParentWithKeylessChild(
                            d,
                            r => r.OnDelete = ForeignKeyReferentialAction.SetDefault
                        )
                )
                .Expect(
                    Or,
                    e =>
                        e.Text(
                            "*" + OracleSetDefaultNote + "*",
                            "*REFERENCES \"parent\" (\"id\");*"
                        )
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-fk-on-delete-no-action-omitted-oracle",
                    "Build: ON DELETE NO ACTION は句を省略する",
                    E.ForeignKey,
                    F.Omit,
                    d =>
                        ParentWithKeylessChild(
                            d,
                            r =>
                            {
                                r.OnDelete = ForeignKeyReferentialAction.NoAction;
                                r.OnUpdate = ForeignKeyReferentialAction.NoAction;
                            }
                        )
                )
                .Expect(Or, e => e.Text("*ADD CONSTRAINT*").NoText("*ON DELETE*")),
        ];

    /// <summary>識別子のクォート・エスケープ</summary>
    private static IEnumerable<DialectDdlRow> QuoteRows() =>
        [
            DialectDdlRow
                .Ddl(
                    "ddl-quote-japanese-identifiers",
                    "Build: 日本語テーブル名・列名がクォートされる",
                    E.Table,
                    F.Quote,
                    d => Diagram(Table("顧客", PkDefault("顧客ID", ShopsIntType[d])))
                )
                .ExpectWithDialect(
                    [Pg, My, Or],
                    (d, e) =>
                        e.Lines(
                            "table 顧客",
                            $"column 顧客.顧客ID {ShopsIntType[d]} NOT NULL*",
                            "pk 顧客 * (顧客ID)*"
                        )
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-quote-double-quote-in-identifier",
                    "Build: 識別子に含まれる \" がエスケープされる",
                    E.Table,
                    F.Quote,
                    d => Diagram(Table("wei\"rd", PkDefault("co\"l", ShopsIntType[d])))
                )
                .ExpectWithDialect(
                    [Pg, Or],
                    (d, e) =>
                        e.Lines(
                            "table wei\"rd",
                            $"column wei\"rd.co\"l {ShopsIntType[d]} NOT NULL*"
                        )
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-quote-backtick-in-identifier",
                    "Build: 識別子に含まれる ` がエスケープされる",
                    E.Table,
                    F.Quote,
                    d => Diagram(Table("wei`rd", PkDefault("co`l", ShopsIntType[d])))
                )
                .Expect(My, e => e.Lines("table wei`rd", "column wei`rd.co`l int NOT NULL*")),
            DialectDdlRow
                .Ddl(
                    "ddl-quote-closing-bracket-in-identifier",
                    "Build: 識別子に含まれる ] がエスケープされる",
                    E.Table,
                    F.Quote,
                    d => Diagram(Table("Weird]Name", Pk("Col]umn", "int")))
                )
                .Expect(
                    Ss,
                    e =>
                        e.Lines(
                            "table Weird]Name",
                            "column Weird]Name.Col]umn int NOT NULL*",
                            "pk Weird]Name * (Col]umn)*"
                        )
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-quote-japanese-table-and-double-quote-column",
                    "Build: 識別子が二重引用符でクォート・エスケープされる",
                    E.Table,
                    F.Quote,
                    d => Diagram(Table("顧客", PkDefault("co\"l", "NVARCHAR(10)")))
                )
                .Expect(Sl, e => e.Lines("table 顧客", "column 顧客.co\"l *")),
        ];

    /// <summary>説明（COMMENT ON・拡張プロパティ・COMMENT 句・コメント行）</summary>
    private static IEnumerable<DialectDdlRow> DescriptionRows() =>
        [
            DialectDdlRow
                .Ddl(
                    "ddl-description-table-and-column",
                    "Build: テーブル・列の説明が COMMENT ON などで出力される",
                    E.Description,
                    F.StatementShape,
                    d =>
                    {
                        var entity = Table(
                            "User",
                            Pk("Id", ShopsIntType[d]),
                            Described(Col("Name", VarcharType(d, 50)), "氏名")
                        );
                        entity.Description = "利用者マスタ";
                        return Diagram(entity);
                    }
                )
                .Expect(
                    [Pg, Or],
                    e =>
                        e.Text(
                            "COMMENT ON TABLE \"User\" IS '利用者マスタ';",
                            "COMMENT ON COLUMN \"User\".\"Name\" IS '氏名';"
                        )
                )
                // MySQL は列説明を列定義へインライン・表説明を閉じ括弧の後の COMMENT= 句で書く
                .ExpectWithDialect(
                    [My],
                    (d, e) =>
                        e.Lines($"column User.Name {VarcharType(d, 50)} NULL COMMENT '氏名',")
                            .Text(") COMMENT='利用者マスタ';")
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-description-extended-property-sqlserver",
                    "Build: テーブル・列の説明が sp_addextendedproperty で出力される（GO なし）",
                    E.Description,
                    F.StatementShape,
                    d =>
                    {
                        var entity = Table(
                            "dbo.User",
                            Pk("Id", "int"),
                            Described(Col("Name", "nvarchar(50)"), "氏名")
                        );
                        entity.Description = "利用者マスタ";
                        return Diagram(entity);
                    }
                )
                .Expect(
                    Ss,
                    e =>
                        e.Text(
                                "*EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'利用者マスタ', "
                                    + "@level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE',  @level1name=N'User';*",
                                "*EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'氏名', "
                                    + "@level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE',  @level1name=N'User', "
                                    + "@level2type=N'COLUMN', @level2name=N'Name';*"
                            )
                            .NoText("*GO*")
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-description-default-schema-declared-once-sqlserver",
                    "Build: 修飾の無い表の説明は既定スキーマ（SCHEMA_NAME()）を指し、宣言は 1 回だけ",
                    E.Description,
                    F.StatementShape,
                    d =>
                    {
                        var order = Table("sales.Order", PkDefault("Id", "int"));
                        order.Description = "注文";
                        var customer = Table(
                            "Customer",
                            Described(Col("Name", "nvarchar(50)"), "氏名")
                        );
                        customer.Description = "顧客";
                        return Diagram(order, customer);
                    }
                )
                .Expect(
                    Ss,
                    e =>
                        e.Count(O.Text, 1, "*DECLARE @default_schema sysname = SCHEMA_NAME();*")
                            .Order(
                                O.Text,
                                "*DECLARE @default_schema sysname = SCHEMA_NAME();*",
                                "*@level0name=@default_schema*"
                            )
                            .Text(
                                "*@level0type=N'SCHEMA', @level0name=@default_schema, @level1type=N'TABLE',  @level1name=N'Customer';*",
                                "*@level0type=N'SCHEMA', @level0name=@default_schema, @level1type=N'TABLE',  @level1name=N'Customer', "
                                    + "@level2type=N'COLUMN', @level2name=N'Name';*",
                                "*@level0type=N'SCHEMA', @level0name=N'sales', @level1type=N'TABLE',  @level1name=N'Order';*"
                            )
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-description-qualified-only-no-default-schema-sqlserver",
                    "Build: 修飾つきの表だけの説明には既定スキーマの宣言を出さない",
                    E.Description,
                    F.Omit,
                    d =>
                    {
                        var user = Table("dbo.User", PkDefault("Id", "int"));
                        user.Description = "利用者";
                        return Diagram(user, Table("Plain", PkDefault("Id", "int")));
                    }
                )
                .Expect(Ss, e => e.NoText("*@default_schema*")),
            DialectDdlRow
                .Ddl(
                    "ddl-description-single-quote-escaped-in-table",
                    "Build: 説明のシングルクォートがエスケープされる",
                    E.Description,
                    F.Quote,
                    d =>
                    {
                        var entity = Table("T", Col("C", ShopsIntType[d]));
                        entity.Description = "It's a table";
                        return Diagram(entity);
                    }
                )
                .Expect(Ss, e => e.Text("*@value=N'It''s a table'*"))
                .Expect([Pg, Or], e => e.Text("COMMENT ON TABLE \"T\" IS 'It''s a table';")),
            DialectDdlRow
                .Ddl(
                    "ddl-description-single-quote-escaped-in-table-and-column-mysql",
                    "Build: 説明のシングルクォートがエスケープされる（MySQL・表と列）",
                    E.Description,
                    F.Quote,
                    d =>
                    {
                        var entity = Table(
                            "T",
                            Described(Col("C", ShopsIntType[d]), "it's a column")
                        );
                        entity.Description = "It's a table";
                        return Diagram(entity);
                    }
                )
                .Expect(
                    My,
                    e => e.Text("*COMMENT 'it''s a column'*", ") COMMENT='It''s a table';")
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-description-none-emits-nothing",
                    "Build: 説明なしの図では説明の文を出力しない",
                    E.Description,
                    F.Omit,
                    d => Diagram(Table("T", Pk("Id", ShopsIntType[d])))
                )
                .Expect(Ss, e => e.NoText("*sp_addextendedproperty*"))
                .Expect([Pg, Or], e => e.NoText("*COMMENT ON*"))
                .Expect(My, e => e.NoText("*COMMENT*")),
            DialectDdlRow
                .Ddl(
                    "ddl-description-sql-comment-lines-sqlite",
                    "Build: テーブル・カラムの説明が -- コメントで出力される",
                    E.Description,
                    F.Note,
                    d =>
                    {
                        var entity = Table(
                            "products",
                            Described(PkDefault("id", ShopsIntType[d]), "商品ID")
                        );
                        entity.Description = "商品マスタ";
                        return Diagram(entity);
                    }
                )
                .Expect(Sl, e => e.Text("*-- products: 商品マスタ*", "*-- id: 商品ID*")),
        ];

    /// <summary>SQLite だけが持つ観点（ヘッダー・多対多の注記）</summary>
    private static IEnumerable<DialectDdlRow> SqliteOnlyRows() =>
        [
            DialectDdlRow
                .Ddl(
                    "ddl-table-common-header",
                    "Build: ヘッダコメントが共通文言で出力される",
                    E.Table,
                    F.English,
                    d => Diagram(Table("t", Col("id", "INT")))
                )
                .Expect(
                    Sl,
                    e =>
                        e.Exact(
                                O.Lines,
                                "header",
                                "generated-at",
                                "table t",
                                "column t.id INT NULL",
                                "end-table"
                            )
                            .Text("-- DDL auto-generated by QuickER*")
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-many-to-many-junction-note",
                    "Build: 多対多はジャンクションテーブルのコメントを出力する",
                    E.ManyToMany,
                    F.Note,
                    d =>
                    {
                        var a = Table("a", PkDefault("id", "INT"));
                        var b = Table("b", PkDefault("id", "INT"));
                        var diagram = Diagram(a, b);
                        diagram.Relationships.Add(
                            new Relationship
                            {
                                SourceEntityId = a.Id,
                                TargetEntityId = b.Id,
                                Type = RelationshipType.ManyToMany,
                            }
                        );
                        return diagram;
                    }
                )
                .Expect(
                    Sl,
                    e => e.Text("*-- Many-to-many (a <-> b): define a junction table separately.*")
                ),
            DialectDdlRow
                .Ddl(
                    "ddl-constraint-order-pk-unique-fk",
                    "Build: 制約行は PK → UNIQUE → FK の順に並ぶ",
                    E.ForeignKey,
                    F.StatementShape,
                    d =>
                    {
                        var owner = Table("owners", Pk("id", "INT"));
                        var shop = Table(
                            "shops",
                            Pk("id", "INT"),
                            Col("code", VarcharType(d, 20), nullable: false),
                            Col("region", VarcharType(d, 10), nullable: false),
                            Col("owner_id", "INT", nullable: false)
                        );
                        shop.UniqueConstraints.Add(
                            new UniqueConstraint
                            {
                                Name = "UQ_shops_code",
                                ColumnIds = [shop.Columns[1].Id],
                            }
                        );

                        var diagram = Diagram(shop, owner);
                        diagram.Relationships.Add(
                            new Relationship
                            {
                                SourceEntityId = owner.Id,
                                TargetEntityId = shop.Id,
                                Type = RelationshipType.OneToMany,
                                ColumnPairs = [new(owner.Columns[0].Id, shop.Columns[3].Id)],
                                ConstraintName = "FK_shops_owners",
                            }
                        );
                        return diagram;
                    }
                )
                .Expect(
                    Sl,
                    e =>
                        e.Order(
                            O.Lines,
                            "pk shops PK_shops (id)*",
                            "uq shops UQ_shops_code (code),",
                            "fk shops FK_shops_owners (owner_id) -> owners (id) inline"
                        )
                ),
        ];
}
