using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport;
using static QuickER.Tests.Provider.DdlModel;
using E = QuickER.Tests.Provider.DialectDdlElements;
using F = QuickER.Tests.Provider.DialectDdlFacets;
using O = QuickER.Tests.Provider.DialectDdlOutputs;
using P = QuickER.Tests.Provider.DialectDdlPaths;

namespace QuickER.Tests.Provider;

/// <summary>
/// 網羅しない組を埋める行（表・列・主キー・外部キー・一意制約。期待は製品の XmlDoc・CLAUDE.md から導く）
/// </summary>
internal static partial class DialectDdlRows
{
    /// <summary>網羅しない組を埋める行すべて（主題ごとの partial が返す）</summary>
    private static IEnumerable<DialectDdlRow> CoverageRows() =>
        [.. CoverageTableKeyRows(), .. CoverageGeneratedColumnRows(), .. CoverageNameAndTypeRows()];

    /// <summary>方言の識別子の開きクォート</summary>
    private static string CvOpen(string dialect) =>
        dialect == Ss ? "["
        : dialect == My ? "`"
        : "\"";

    /// <summary>方言の識別子の閉じクォート（識別子の中に現れたら二重化される文字）</summary>
    private static string CvClose(string dialect) =>
        dialect == Ss ? "]"
        : dialect == My ? "`"
        : "\"";

    /// <summary>識別子を方言のクォートで囲む（終端文字は二重化。テストの期待用の独立した書き方）</summary>
    private static string CvQuote(string dialect, string name) =>
        CvOpen(dialect)
        + name.Replace(
            CvClose(dialect),
            CvClose(dialect) + CvClose(dialect),
            StringComparison.Ordinal
        )
        + CvClose(dialect);

    /// <summary>表の追加の項目</summary>
    private static SchemaDiffItem CvAddTable(Entity entity) =>
        new()
        {
            Kind = SchemaDiffKind.AddTable,
            TableName = entity.TableName,
            Entity = entity,
            IsSelected = true,
        };

    /// <summary>列の追加の項目</summary>
    private static SchemaDiffItem CvAddColumn(string table, Column column, Entity? entity = null) =>
        new()
        {
            Kind = SchemaDiffKind.AddColumn,
            TableName = table,
            ColumnName = column.Name,
            Entity = entity,
            Column = column,
            IsSelected = true,
        };

    /// <summary>列の変更の項目</summary>
    private static SchemaDiffItem CvAlterColumn(string table, Column column) =>
        new()
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = table,
            ColumnName = column.Name,
            Column = column,
            IsSelected = true,
        };

    /// <summary>主キーの列（NOT NULL・指定した型）</summary>
    private static Column CvPkOf(string name, string type) => Pk(name, type);

    /// <summary>親 a（id）と子 b（id・a_id）を結ぶ 1 対多</summary>
    private static Relationship CvLink(
        Entity parent,
        Entity child,
        int childColumn,
        string? name
    ) =>
        new()
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs = [new(parent.Columns[0].Id, child.Columns[childColumn].Id)],
            ConstraintName = name,
        };

    /// <summary>live の表だけを持つ同期の入力（能力は方言の宣言・live の文脈つき。SQLite は再構築の経路へ入る）</summary>
    private static DdlSyncItems CvLiveItems(
        IReadOnlyList<Entity> live,
        IReadOnlyList<Relationship> relationships,
        params SchemaDiffItem[] items
    ) =>
        new(
            items,
            null,
            new SyncPlanContext { LiveEntities = live, LiveRelationships = relationships }
        );

    /// <summary>差分から作る同期の入力（live と目標を組み立てる関数から。全項目を選ぶ）</summary>
    private static DdlSyncDiff CvDiff(
        IReadOnlyList<Entity> live,
        IReadOnlyList<Relationship> liveRelationships,
        IReadOnlyList<Entity> target,
        IReadOnlyList<Relationship> targetRelationships,
        Action<IReadOnlyList<SchemaDiffItem>>? select = null
    ) => new(live, liveRelationships, target, targetRelationships, select);

    /// <summary>方言ごとの、整数より広い整数の型（外部キーの片側の型の変更に使う）</summary>
    private static IReadOnlyDictionary<string, string> CvWideInt =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Ss] = "bigint",
            [Pg] = "bigint",
            [My] = "bigint",
            [Or] = "NUMBER(19)",
            [Sl] = "BIGINT",
        };

    /// <summary>方言の終端クォート文字を含む名前（<c>c</c> に終端文字を足したもの）</summary>
    private static string CvQuoteName(string dialect, string stem) => stem + CvClose(dialect);

    /// <summary>表（先頭・左）、列（上）、主キー、外部キー、一意制約の行</summary>
    private static IEnumerable<DialectDdlRow> CoverageTableKeyRows() =>
        [
            // 行 1
            DialectDdlRow
                .Ddl(
                    "ddl-table-description-supplementary-character-note-before-create-mysql-only",
                    "表の説明の BMP の外の文字: MySQL だけ CREATE TABLE の直前に注記し、ほかの方言は注記しない（src/QuickER.Provider/CLAUDE.md「説明の空欄と表せない文字」）",
                    E.Table,
                    F.Note,
                    d => TrTargetDiagram(d, TrDescribed(TrEmoji, IntType[d], columnDescription: ""))
                )
                .Expect(
                    My,
                    e =>
                        e.Count(O.Text, 1, TrBmpNote)
                            .Sequence(O.Lines, "note description-characters | *table t*", "table t")
                )
                .Expect([Ss, Pg, Or, Sl], e => e.NoText(TrBmpNote)),
            // 行 2
            DialectDdlRow
                .Ddl(
                    "ddl-table-definition-order-then-foreign-keys-then-descriptions",
                    "表は定義順に作り、外部キーは全表の後（SQLite は子の表の中）、説明の文はその後にまとめる（src/QuickER.Provider/DdlGeneratorBase.cs の出力順）",
                    E.Table,
                    F.Order,
                    d =>
                    {
                        var a = Table("a", Pk("id", IntType[d]));
                        a.Description = "about a";
                        var b = Table("b", Pk("id", IntType[d]), Col("a_id", IntType[d]));
                        b.Description = "about b";
                        var diagram = Diagram(b, a);
                        diagram.Relationships.Add(CvLink(a, b, 1, "FK_b_a"));
                        return diagram;
                    }
                )
                .Expect(
                    Ss,
                    e =>
                        e.Order(
                            O.Text,
                            "CREATE TABLE [b] (",
                            "CREATE TABLE [a] (",
                            "ALTER TABLE [b] ADD CONSTRAINT*",
                            "*@level1name=N'b';*",
                            "*@level1name=N'a';*"
                        )
                )
                .Expect(
                    [Pg, Or],
                    e =>
                        e.Order(
                            O.Text,
                            "CREATE TABLE \"b\" (",
                            "CREATE TABLE \"a\" (",
                            "ALTER TABLE \"b\" ADD CONSTRAINT*",
                            "COMMENT ON TABLE \"b\" IS 'about b';",
                            "COMMENT ON TABLE \"a\" IS 'about a';"
                        )
                )
                .Expect(
                    My,
                    e =>
                        e.Order(
                            O.Text,
                            "CREATE TABLE `b` (",
                            ") COMMENT='about b';",
                            "CREATE TABLE `a` (",
                            ") COMMENT='about a';",
                            "ALTER TABLE `b` ADD CONSTRAINT*"
                        )
                )
                .Expect(
                    Sl,
                    e =>
                        e.Order(
                                O.Lines,
                                "table b",
                                "fk b FK_b_a (a_id) -> a (id) inline",
                                "table a"
                            )
                            .NoText("*ALTER TABLE*")
                ),
            // 行 3
            DialectDdlRow
                .Ddl(
                    "ddl-table-description-control-characters-fold-into-one-comment-line-sqlite",
                    "表の説明の CR・LF・TAB・NEL・U+2028・U+2029 は、SQLite の -- コメント 1 行へ空白に畳まれ、後続の行を作らない（src/QuickER.Provider/SqlComment.cs の Sanitize）",
                    E.Table,
                    F.CommentSanitize,
                    d =>
                    {
                        var entity = Table("t", Pk("id", IntType[d]));
                        entity.Description = "a\r\nb\tc\u0085d\u2028e\u2029f";
                        return Diagram(entity);
                    }
                )
                .Expect(
                    Sl,
                    e =>
                        e.Count(O.Text, 1, "-- t: a  b c d e f")
                            .Count(O.Lines, 1, "comment -- t: *")
                            .NoText("b*", "c*", "d*", "e*", "f*")
                ),
            // 行 4
            DialectDdlRow
                .SyncItems(
                    "sync-add-table-primary-key-name-from-trimmed-table-name",
                    "表の追加の主キー名は、前後に空白のある表名から空白を除いた名前で作る（src/QuickER.Provider/CLAUDE.md「制約名の正本と識別子の分割クォート」）",
                    P.SyncAddTable,
                    E.Table,
                    F.ConstraintName,
                    _ =>
                    {
                        var (_, _, child, _) = CcFamily();
                        return new DdlSyncItems(
                            [
                                new SchemaDiffItem
                                {
                                    Kind = SchemaDiffKind.AddTable,
                                    TableName = CcChildTable,
                                    Entity = child,
                                    IsSelected = true,
                                },
                            ],
                            null,
                            new SyncPlanContext()
                        );
                    }
                )
                .Expect(
                    [Ss, Pg, My, Or, Sl],
                    e => e.Text("*PK_Orders*").NoText("*PK_ Orders*", "*PK_Orders *")
                ),
            // 行 5
            DialectDdlRow
                .SyncItems(
                    "sync-add-table-column-name-terminator-is-doubled",
                    "表の追加の列名に含まれる終端クォート文字は、列定義と主キーの列リストで二重化する（src/QuickER.Provider/CLAUDE.md「SQL の信頼境界」）",
                    P.SyncAddTable,
                    E.Column,
                    F.Quote,
                    d => new DdlSyncItems(
                        [CvAddTable(Table("t", Pk(CvQuoteName(d, "c"), "int"), Col("v", "int")))],
                        null,
                        new SyncPlanContext()
                    )
                )
                .ExpectWithDialect(
                    DialectNames.All,
                    (d, e) =>
                        e.Lines(
                            $"column t.{CvQuoteName(d, "c")} int NOT NULL*",
                            $"pk t PK_t ({CvQuoteName(d, "c")})*"
                        )
                ),
            // 行 6
            DialectDdlRow
                .SyncItems(
                    "sync-add-table-columns-in-definition-order-then-constraints",
                    "表の追加は列を定義順に並べ、制約行はその後に出す（src/QuickER.Provider/TableConstraintLineBuilder.cs）",
                    P.SyncAddTable,
                    E.Column,
                    F.Order,
                    _ => new DdlSyncItems(
                        [CvAddTable(Table("t", Col("z", "int"), Pk("a", "int"), Col("m", "int")))],
                        null,
                        new SyncPlanContext()
                    )
                )
                .ExpectAll(e =>
                    e.Order(
                        O.Lines,
                        "column t.z *",
                        "column t.a *",
                        "column t.m *",
                        "pk t PK_t (a)*"
                    )
                ),
            // 行 7
            DialectDdlRow
                .SyncDiff(
                    "sync-add-column-unnamed-unique-constraint-synthesizes-name",
                    "列の追加と名前なしの一意制約: 制約名は UQ_表_列 を合成し、列の追加の節が一意制約の追加の節より先（SQLite は再構築の表に載る。src/QuickER.Provider/CLAUDE.md「一意制約の差分」）",
                    P.SyncAddColumn,
                    E.Column,
                    F.ConstraintName,
                    d =>
                    {
                        var live = Table("t", Pk("id", IntType[d]));
                        var target = Table(
                            "t",
                            Pk("id", IntType[d]),
                            Col("code", IntType[d], nullable: false)
                        );
                        target.UniqueConstraints.Add(
                            new UniqueConstraint { ColumnIds = [target.Columns[1].Id] }
                        );
                        return CvDiff([live], [], [target], []);
                    }
                )
                .Expect(
                    [Ss, Pg, My],
                    e =>
                        e.Text("*UQ_t_code*")
                            .Order(
                                O.Lines,
                                "note section | -- ===== AddColumn*",
                                "note section | -- ===== AddUniqueConstraint*"
                            )
                )
                .Expect(Sl, e => e.Lines("uq t_quicker_rebuild UQ_t_code (code)*"))
                .Expect(
                    Or,
                    e =>
                        e.Text("*UQ_t_code*")
                            .Order(
                                O.Text,
                                "ALTER TABLE \"t\" ADD (\"code\"*",
                                "ALTER TABLE \"t\" ADD CONSTRAINT \"UQ_t_code\" UNIQUE*"
                            )
                ),
            // 行 8
            DialectDdlRow
                .SyncItems(
                    "sync-rebuild-column-attributes-lost-are-warned",
                    "SQLite の再構築は図に無い列の属性（DEFAULT・COLLATE）を失うので警告する（src/QuickER.Provider/CLAUDE.md「テーブル再構築の合成」）",
                    P.SyncRebuild,
                    E.Column,
                    F.Note,
                    _ =>
                    {
                        var live = Table(
                            "t",
                            Pk("id", "INTEGER"),
                            Col("a", "TEXT", nullable: false)
                        );
                        return new DdlSyncItems(
                            [CvAlterColumn("t", Col("a", "TEXT", nullable: true))],
                            null,
                            new SyncPlanContext
                            {
                                LiveEntities = [live],
                                TableCreateSql = new Dictionary<string, string>(
                                    StringComparer.Ordinal
                                )
                                {
                                    ["t"] =
                                        "CREATE TABLE \"t\" (\"id\" INTEGER PRIMARY KEY, \"a\" TEXT NOT NULL DEFAULT 'x' COLLATE NOCASE)",
                                },
                            }
                        );
                    }
                )
                .Expect(
                    Sl,
                    e =>
                        e.Contains(
                            O.Warnings,
                            "TableRebuildDropsColumnAttribute t*DEFAULT*COLLATE*"
                        )
                ),
            // 行 9
            DialectDdlRow
                .SyncItems(
                    "sync-rebuild-dropped-column-is-absent-from-new-table-and-copy",
                    "SQLite の再構築は、削除する列を新しい表にも INSERT ... SELECT にも出さない（src/QuickER.Provider/CLAUDE.md「構成列の削除と列が残らない表」）",
                    P.SyncRebuild,
                    E.Column,
                    F.Omit,
                    _ =>
                    {
                        var live = Table(
                            "t",
                            Pk("id", "INTEGER"),
                            Col("a", "INTEGER"),
                            Col("b", "INTEGER")
                        );
                        return CvLiveItems(
                            [live],
                            [],
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.DropColumn,
                                TableName = "t",
                                ColumnName = "b",
                                IsSelected = true,
                            }
                        );
                    }
                )
                .Expect(
                    Sl,
                    e =>
                        e.Lines("column t_quicker_rebuild.id *", "column t_quicker_rebuild.a *")
                            .NoLines("column t_quicker_rebuild.b *")
                            .Text("INSERT INTO \"t_quicker_rebuild\" (\"id\", \"a\")*")
                            .NoText("*\"b\"*")
                ),
            // 行 10
            DialectDdlRow
                .Ddl(
                    "ddl-column-description-control-characters-fold-into-one-comment-line-sqlite",
                    "列の説明の制御文字・行区切りは、SQLite の列行の直後のコメント 1 行へ空白に畳まれる（src/QuickER.Provider/SqlComment.cs の Sanitize）",
                    E.Column,
                    F.CommentSanitize,
                    d =>
                        Diagram(
                            Table(
                                "t",
                                Pk("id", IntType[d]),
                                Described(Col("v", IntType[d]), "a\r\nb\tc\u0085d\u2028e\u2029f")
                            )
                        )
                )
                .Expect(
                    Sl,
                    e =>
                        e.Count(O.Text, 1, "    -- v: a  b c d e f")
                            .Sequence(O.Lines, "column t.v *", "comment -- v: a  b c d e f")
                ),
            // 行 11
            DialectDdlRow
                .SyncItems(
                    "sync-rebuild-primary-key-column-name-quote-is-doubled",
                    "SQLite の再構築の主キー行は、付け替えた主キーの列名の終端クォート文字を二重化する（src/QuickER.Provider/CLAUDE.md「主キーの同期」）",
                    P.SyncRebuild,
                    E.PrimaryKey,
                    F.Quote,
                    _ =>
                    {
                        var live = Table(
                            "t",
                            Pk("id", "INTEGER"),
                            Col("c\"x", "INTEGER", nullable: false)
                        );
                        var target = live.Clone(preserveId: true);
                        target.Columns[0].IsPrimaryKey = false;
                        target.Columns[1].IsPrimaryKey = true;
                        return CvLiveItems(
                            [live],
                            [],
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.AlterPrimaryKey,
                                TableName = "t",
                                Entity = target,
                                IsSelected = true,
                            }
                        );
                    }
                )
                .Expect(Sl, e => e.Lines("pk t_quicker_rebuild PK_t (c\"x)*")),
            // 行 12
            DialectDdlRow
                .SyncDiff(
                    "sync-other-primary-key-change-under-foreign-key-warns-candidate-key",
                    "外部キーに支えられた主キーの付け替えは、付け直す外部キーの候補キー喪失を警告する（src/QuickER.Provider/CLAUDE.md「候補キーの証明」。期待の行 plan-alter-pk-referenced-column-leaves-pk-warns-candidate-key-loss と同じ警告）",
                    P.SyncOther,
                    E.PrimaryKey,
                    F.Note,
                    d =>
                    {
                        (List<Entity> Tables, List<Relationship> Links) Build(bool moved)
                        {
                            var customer = Table(
                                "customer",
                                new Column
                                {
                                    Name = "id",
                                    DataType = IntType[d],
                                    IsPrimaryKey = !moved,
                                    IsNullable = false,
                                },
                                new Column
                                {
                                    Name = "code",
                                    DataType = IntType[d],
                                    IsPrimaryKey = moved,
                                    IsNullable = false,
                                }
                            );
                            var orders = Table(
                                "orders",
                                Pk("id", IntType[d]),
                                Col("customer_id", IntType[d])
                            );
                            return (
                                [customer, orders],
                                [CvLink(customer, orders, 1, "FK_orders_customer")]
                            );
                        }

                        var (liveTables, liveLinks) = Build(false);
                        var (targetTables, targetLinks) = Build(true);
                        return CvDiff(
                            liveTables,
                            liveLinks,
                            targetTables,
                            targetLinks,
                            items =>
                            {
                                foreach (var item in items)
                                {
                                    item.IsSelected = item.Kind == SchemaDiffKind.AlterPrimaryKey;
                                }
                            }
                        );
                    }
                )
                .ExpectAll(e =>
                    e.Contains(O.Warnings, "ForeignKeyRebuildMayLoseCandidateKey orders*")
                ),
            // 行 13
            DialectDdlRow
                .SyncDiff(
                    "sync-other-primary-key-removed-writes-no-primary-key-statement",
                    "主キーを外すだけの同期は、PRIMARY KEY を付与する文を出さない（src/QuickER.Provider/CLAUDE.md「主キーの同期」の「解除だけなら ADD なし」）",
                    P.SyncOther,
                    E.PrimaryKey,
                    F.Omit,
                    d =>
                    {
                        var live = Table("t", Pk("id", IntType[d]), Col("v", IntType[d]));
                        var target = Table(
                            "t",
                            Col("id", IntType[d], nullable: false),
                            Col("v", IntType[d])
                        );
                        return CvDiff(
                            [live],
                            [],
                            [target],
                            [],
                            items =>
                            {
                                foreach (var item in items)
                                {
                                    item.IsSelected = item.Kind == SchemaDiffKind.AlterPrimaryKey;
                                }
                            }
                        );
                    }
                )
                .ExpectAll(e =>
                    e.NoException()
                        .NoLines("pk t *")
                        .NoText("*ADD CONSTRAINT*PRIMARY KEY*", "*ADD PRIMARY KEY*")
                ),
            // 行 14
            DialectDdlRow
                .SyncItems(
                    "sync-other-alter-primary-key-column-name-with-newline-is-rejected",
                    "主キーの付け替えの主キー列名に改行があれば、同期スクリプトの入口が名指しで断る（src/QuickER.Provider/CLAUDE.md「SQL の信頼境界」の名前の入口検証）",
                    P.SyncOther,
                    E.PrimaryKey,
                    F.Reject,
                    _ => new DdlSyncItems(
                        [
                            new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.AlterPrimaryKey,
                                TableName = "t",
                                Entity = Table("t", Pk(CcInjected, "int")),
                                IsSelected = true,
                            },
                        ],
                        TestDialectCapabilities.Neutral
                    )
                )
                .ExpectAll(e => e.Throws("InvalidOperationException: *COLUMN*")),
            // 行 15
            DialectDdlRow
                .SyncItems(
                    "sync-add-column-primary-key-column-marked-nullable-is-not-null",
                    "列の追加の主キー列は、NULL 許容の印があっても NOT NULL で追加する（src/QuickER.Provider/CLAUDE.md「主キーの列の NULL 許容」）",
                    P.SyncAddColumn,
                    E.PrimaryKey,
                    F.StatementShape,
                    _ => new DdlSyncItems(
                        [
                            CvAddColumn(
                                "t",
                                new Column
                                {
                                    Name = "code",
                                    DataType = "int",
                                    IsPrimaryKey = true,
                                    IsNullable = true,
                                }
                            ),
                        ],
                        TestDialectCapabilities.Neutral
                    )
                )
                .ExpectAll(e => e.Text("*code*int NOT NULL*").NoText("*code*int NULL*")),
            // 行 16
            DialectDdlRow
                .SyncDiff(
                    "sync-add-column-foreign-key-child-column-name-terminator-is-doubled",
                    "終端クォート文字を含む子列の追加と外部キーは、列の追加と FOREIGN KEY の両方で二重化する（src/QuickER.Provider/CLAUDE.md「SQL の信頼境界」。SQLite は再構築の表に載る）",
                    P.SyncAddColumn,
                    E.ForeignKey,
                    F.Quote,
                    d =>
                    {
                        var name = CvQuoteName(d, "o");
                        var liveParent = Table("parent", Pk("id", IntType[d]));
                        var liveChild = Table("child", Pk("id", IntType[d]));
                        var parent = Table("parent", Pk("id", IntType[d]));
                        var child = Table("child", Pk("id", IntType[d]), Col(name, IntType[d]));
                        return CvDiff(
                            [liveParent, liveChild],
                            [],
                            [parent, child],
                            [CvLink(parent, child, 1, "FK_child_parent")]
                        );
                    }
                )
                .ExpectWithDialect(
                    DialectNames.All,
                    (d, e) =>
                        e.Lines($"fk * FK_child_parent ({CvQuoteName(d, "o")}) -> parent (id)*")
                )
                .ExpectWithDialect(
                    [Ss, Pg, My, Or],
                    (d, e) => e.Text($"*ADD*{CvQuote(d, CvQuoteName(d, "o"))}*")
                ),
            // 行 17
            DialectDdlRow
                .Ddl(
                    "ddl-unique-constraint-names-with-terminator-are-doubled",
                    "終端クォート文字を含む表名・列名・一意制約名は、一意制約の行で二重化される（src/QuickER.Provider/CLAUDE.md「SQL の信頼境界」）",
                    E.Unique,
                    F.Quote,
                    d =>
                    {
                        var entity = Table(
                            CcEscapedTableName[d],
                            Pk("id", IntType[d]),
                            Col(CvQuoteName(d, "c"), IntType[d], nullable: false)
                        );
                        entity.UniqueConstraints.Add(
                            new UniqueConstraint
                            {
                                Name = CvQuoteName(d, "u"),
                                ColumnIds = [entity.Columns[1].Id],
                            }
                        );
                        return Diagram(entity);
                    }
                )
                .ExpectWithDialect(
                    DialectNames.All,
                    (d, e) =>
                        e.Lines(
                            $"uq {CcEscapedTableName[d]} {CvQuoteName(d, "u")} ({CvQuoteName(d, "c")})*"
                        )
                ),
            // 行 18
            DialectDdlRow
                .SyncDiff(
                    "sync-other-primary-key-replaces-unique-constraint-on-same-columns",
                    "一意制約の列集合へ主キーを付け替える同期は、主キーと同じ列集合の一意制約を持てない Oracle だけが警告して一意制約を外す（src/QuickER.Provider/CLAUDE.md「計算列と主キーと同じ列集合の一意制約」）",
                    P.SyncOther,
                    E.Unique,
                    F.Note,
                    d =>
                    {
                        Entity Build(bool moved)
                        {
                            var entity = Table(
                                "item",
                                new Column
                                {
                                    Name = "id",
                                    DataType = IntType[d],
                                    IsPrimaryKey = moved,
                                    IsNullable = false,
                                },
                                new Column
                                {
                                    Name = "code",
                                    DataType = IntType[d],
                                    IsPrimaryKey = !moved,
                                    IsNullable = false,
                                }
                            );
                            entity.UniqueConstraints.Add(
                                new UniqueConstraint
                                {
                                    Name = "UQ_item_id",
                                    ColumnIds = [entity.Columns[0].Id],
                                }
                            );
                            return entity;
                        }

                        return CvDiff(
                            [Build(false)],
                            [],
                            [Build(true)],
                            [],
                            items =>
                            {
                                foreach (var item in items)
                                {
                                    item.IsSelected = item.Kind == SchemaDiffKind.AlterPrimaryKey;
                                }
                            }
                        );
                    }
                )
                .Expect(
                    Or,
                    e =>
                        e.Contains(O.Warnings, "UniqueConstraintReplacedByPrimaryKey item*")
                            .Text("*DROP CONSTRAINT*UQ_item_id*")
                )
                .Expect(
                    [Ss, Pg, My],
                    e => e.Absent(O.Warnings, "UniqueConstraintReplacedByPrimaryKey*")
                ),
            // 行 19
            DialectDdlRow
                .SyncItems(
                    "sync-rebuild-constraint-lines-order-pk-unique-foreign-key",
                    "SQLite の再構築の制約行は、主キー → 一意制約（宣言順）→ 外部キーの順に並ぶ（src/QuickER.Provider.Sqlite/SqliteSyncScriptBuilder.cs の AppendCreateTableBody）",
                    P.SyncRebuild,
                    E.Unique,
                    F.Order,
                    _ =>
                    {
                        var parent = Table("parent", Pk("id", "INTEGER"));
                        var child = Table(
                            "t",
                            Pk("id", "INTEGER"),
                            Col("a", "INTEGER", nullable: false),
                            Col("b", "INTEGER", nullable: false),
                            Col("parent_id", "INTEGER")
                        );
                        child.UniqueConstraints.Add(
                            new UniqueConstraint
                            {
                                Name = "UQ_t_b",
                                ColumnIds = [child.Columns[2].Id],
                            }
                        );
                        child.UniqueConstraints.Add(
                            new UniqueConstraint
                            {
                                Name = "UQ_t_a",
                                ColumnIds = [child.Columns[1].Id],
                            }
                        );
                        return CvLiveItems(
                            [parent, child],
                            [CvLink(parent, child, 3, "FK_t_parent")],
                            CvAlterColumn("t", Col("a", "INTEGER", nullable: true))
                        );
                    }
                )
                .Expect(
                    Sl,
                    e =>
                        e.Order(
                            O.Lines,
                            "pk t_quicker_rebuild PK_t (id)*",
                            "uq t_quicker_rebuild UQ_t_b (b)*",
                            "uq t_quicker_rebuild UQ_t_a (a)*",
                            "fk t_quicker_rebuild FK_t_parent (parent_id) -> parent (id)*"
                        )
                ),
            // 行 20
            DialectDdlRow
                .SyncDiff(
                    "sync-add-column-named-unique-constraint-statement-shape",
                    "列の追加と名前付きの一意制約: 名前つきの UNIQUE を付ける文になる（SQLite は再構築の表の UNIQUE 行。src/QuickER.Provider/CLAUDE.md「一意制約の差分」）",
                    P.SyncAddColumn,
                    E.Unique,
                    F.StatementShape,
                    d =>
                    {
                        var live = Table("t", Pk("id", IntType[d]));
                        var target = Table(
                            "t",
                            Pk("id", IntType[d]),
                            Col("code", IntType[d], nullable: false)
                        );
                        target.UniqueConstraints.Add(
                            new UniqueConstraint
                            {
                                Name = "UQ_t_code",
                                ColumnIds = [target.Columns[1].Id],
                            }
                        );
                        return CvDiff([live], [], [target], []);
                    }
                )
                .Expect([Ss, Pg, My, Or], e => e.Text("*ADD CONSTRAINT*UQ_t_code*UNIQUE*"))
                .Expect(Sl, e => e.Lines("uq t_quicker_rebuild UQ_t_code (code)*")),
        ];
}
