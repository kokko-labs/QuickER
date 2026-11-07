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
/// 網羅しない組を埋める行（計算列・採番列・説明）
/// </summary>
internal static partial class DialectDdlRows
{
    /// <summary>主キー item_id と、指定した名前・性質の列 1 本を持つ表 items</summary>
    private static Entity CvGeneratedEntity(
        string columnName,
        bool computed,
        bool identity,
        string type = "int",
        bool nullable = true
    ) =>
        Table(
            "items",
            Pk("item_id", "int"),
            new Column
            {
                Name = columnName,
                DataType = type,
                IsNullable = nullable,
                IsComputed = computed,
                IsIdentity = identity,
            }
        );

    private static IEnumerable<DialectDdlRow> CoverageGeneratedColumnRows() =>
        [
            // 行 21
            DialectDdlRow
                .Ddl(
                    "ddl-computed-column-is-written-as-plain-column",
                    "計算列は普通の列として書き（AS・GENERATED・PERSISTED を書かない）、注記は列行の直前に置く（src/QuickER.Provider/DdlGeneratorBase.cs の計算列の扱い）",
                    E.Computed,
                    F.StatementShape,
                    _ => Diagram(TrComputedEntity(computed: true))
                )
                .ExpectAll(e =>
                    e.Sequence(
                            O.Lines,
                            "note computed-column | -- Computed column 'total'*",
                            "column items.total decimal(21,2) NULL*"
                        )
                        .NoText("*GENERATED*", "*PERSISTED*", "*VIRTUAL*", "* AS (*")
                ),
            // 行 22
            DialectDdlRow
                .Ddl(
                    "ddl-computed-column-name-with-terminator-is-doubled-in-column-line-only",
                    "終端クォート文字を含む計算列名は、列行では二重化し、注記には生の名前を書く（src/QuickER.Provider/ComputedColumnComment.cs）",
                    E.Computed,
                    F.Quote,
                    d =>
                        Diagram(
                            CvGeneratedEntity(
                                CvQuoteName(d, "tot"),
                                computed: true,
                                identity: false,
                                "decimal(21,2)"
                            )
                        )
                )
                .ExpectWithDialect(
                    DialectNames.All,
                    (d, e) =>
                        e.Lines(
                            $"note computed-column | -- Computed column '{CvQuoteName(d, "tot")}'*",
                            $"column items.{CvQuoteName(d, "tot")} decimal(21,2) NULL*"
                        )
                ),
            // 行 23
            DialectDdlRow
                .Ddl(
                    "ddl-computed-column-in-unnamed-unique-constraint-synthesizes-name",
                    "計算列を含む名前なしの一意制約も UQ_表_列 の合成名になる（src/QuickER.Model/UniqueConstraint.cs の SynthesizeName）",
                    E.Computed,
                    F.ConstraintName,
                    _ =>
                    {
                        var entity = CvGeneratedEntity("calc", computed: true, identity: false);
                        entity.UniqueConstraints.Add(
                            new UniqueConstraint { ColumnIds = [entity.Columns[1].Id] }
                        );
                        return Diagram(entity);
                    }
                )
                .ExpectAll(e => e.Lines("uq items UQ_items_calc (calc)*")),
            // 行 24
            DialectDdlRow
                .SyncDiff(
                    "sync-other-computed-column-type-change-only-where-expression-is-kept",
                    "計算列の型の変更は、式を保ったまま型を変えられる PostgreSQL・MySQL だけが ALTER を出し、ほかの方言はスクリプトが空（src/QuickER.Provider/CLAUDE.md「計算列と主キーと同じ列集合の一意制約」）",
                    P.SyncOther,
                    E.Computed,
                    F.Omit,
                    d =>
                    {
                        Entity Build(string type) =>
                            Table(
                                "items",
                                Pk("id", IntType[d]),
                                new Column
                                {
                                    Name = "total",
                                    DataType = type,
                                    IsNullable = true,
                                    IsComputed = true,
                                }
                            );

                        var live = Build(d == Sl ? "NUMERIC(10,2)" : "decimal(10,2)");
                        var target = Build(d == Sl ? "NUMERIC(12,2)" : "decimal(12,2)");

                        return CvDiff([live], [], [target], []);
                    }
                )
                .Expect([Pg, My], e => e.Text("*ALTER TABLE*total*"))
                .Expect(
                    [Ss, Or, Sl],
                    e => e.NoText("*ALTER TABLE*", "*CREATE TABLE*", "*DROP TABLE*")
                ),
            // 行 25
            DialectDdlRow
                .Ddl(
                    "ddl-computed-identity-supplementary-description-note-order",
                    "計算列でも採番列でもあり、説明に BMP の外の文字がある列の注記は、計算列 → 採番列 → 説明 の順で列行の直前に並ぶ（採番列の注記は SQL Server・PostgreSQL・Oracle、説明の注記は MySQL だけ。src/QuickER.Provider/DdlGeneratorBase.cs）",
                    E.Computed,
                    F.Order,
                    d =>
                    {
                        var entity = CvGeneratedEntity(
                            "n",
                            computed: true,
                            identity: true,
                            IntType[d]
                        );
                        entity.Columns[1].Description = TrEmoji;
                        return Diagram(entity);
                    }
                )
                .Expect(
                    [Ss, Pg, Or],
                    e =>
                        e.Sequence(
                            O.Lines,
                            "note computed-column | -- Computed column 'n'*",
                            "note identity-column | -- Identity column 'n'*",
                            "column items.n *"
                        )
                )
                .Expect(
                    My,
                    e =>
                        e.Sequence(
                            O.Lines,
                            "note computed-column | -- Computed column 'n'*",
                            "note description-characters | *column items.n*",
                            "column items.n *"
                        )
                )
                .Expect(
                    Sl,
                    e =>
                        e.Sequence(
                            O.Lines,
                            "note computed-column | -- Computed column 'n'*",
                            "column items.n *"
                        )
                ),
            // 行 26
            DialectDdlRow
                .Ddl(
                    "ddl-computed-column-name-with-newline-is-rejected",
                    "計算列名に改行があれば、DDL の入口が列名を名指しして図全体を断る（src/QuickER.Provider/CLAUDE.md「SQL の信頼境界」）",
                    E.Computed,
                    F.Reject,
                    _ => Diagram(CvGeneratedEntity(CcInjected, computed: true, identity: false))
                )
                .ExpectAll(e => e.Throws("InvalidOperationException: *COLUMN*items*")),
            // 行 27
            DialectDdlRow
                .Ddl(
                    "ddl-identity-column-is-written-as-plain-column",
                    "採番列は普通の列として書き（IDENTITY・AUTO_INCREMENT・GENERATED・SERIAL を書かない）、注記は SQL Server・PostgreSQL・Oracle だけが列行の直前に置く（src/QuickER.Provider/IdentityColumnComment.cs）",
                    E.Identity,
                    F.StatementShape,
                    _ => Diagram(CvGeneratedEntity("seq", computed: false, identity: true, "int", nullable: false))
                )
                .ExpectAll(e =>
                    e.Lines("column items.seq int NOT NULL*")
                        .NoText("*IDENTITY*", "*AUTO_INCREMENT*", "*AUTOINCREMENT*", "*SERIAL*", "*GENERATED*")
                )
                .Expect(
                    [Ss, Pg, Or],
                    e =>
                        e.Sequence(
                            O.Lines,
                            "note identity-column | -- Identity column 'seq'*",
                            "column items.seq *"
                        )
                )
                .Expect([My, Sl], e => e.NoText("*-- Identity column*")),
            // 行 28
            DialectDdlRow
                .Ddl(
                    "ddl-identity-column-name-with-terminator-is-doubled-in-column-line-only",
                    "終端クォート文字を含む採番列名は、列行では二重化し、注記には生の名前を書く（src/QuickER.Provider/IdentityColumnComment.cs）",
                    E.Identity,
                    F.Quote,
                    d =>
                        Diagram(
                            CvGeneratedEntity(
                                CvQuoteName(d, "seq"),
                                computed: false,
                                identity: true,
                                "int",
                                nullable: false
                            )
                        )
                )
                .ExpectWithDialect(
                    [Ss, Pg, Or],
                    (d, e) =>
                        e.Lines(
                            $"note identity-column | -- Identity column '{CvQuoteName(d, "seq")}'*",
                            $"column items.{CvQuoteName(d, "seq")} int NOT NULL*"
                        )
                ),
            // 行 29
            DialectDdlRow
                .Ddl(
                    "ddl-identity-primary-key-column-uses-default-primary-key-name",
                    "採番の主キー列の主キー名は既定の PK_表で、INSERT が失敗する旨の文を足さない（src/QuickER.Provider/IdentityColumnComment.cs）",
                    E.Identity,
                    F.ConstraintName,
                    d =>
                        Diagram(
                            Table(
                                "items",
                                new Column
                                {
                                    Name = "id",
                                    DataType = IntType[d],
                                    IsPrimaryKey = true,
                                    IsNullable = false,
                                    IsIdentity = true,
                                }
                            )
                        )
                )
                .ExpectAll(e =>
                    e.Lines("pk items PK_items (id)*").NoText("*fail on its NOT NULL constraint*")
                ),
            // 行 30
            DialectDdlRow
                .SyncItems(
                    "sync-other-alter-column-to-identity-writes-no-identity-clause-or-note",
                    "採番列への型の変更は IDENTITY を書かず採番列の注記も出さない（注記を出すのは表の追加・列の追加だけ。src/QuickER.Provider/IdentityColumnComment.cs の呼び出し元）",
                    P.SyncOther,
                    E.Identity,
                    F.Omit,
                    _ =>
                        new DdlSyncItems(
                            [
                                CvAlterColumn(
                                    "items",
                                    new Column
                                    {
                                        Name = "seq",
                                        DataType = "bigint",
                                        IsNullable = false,
                                        IsIdentity = true,
                                    }
                                ),
                            ],
                            TestDialectCapabilities.Neutral
                        )
                )
                .ExpectAll(e => e.NoText("*-- Identity column*"))
                .Expect([Ss, Or], e => e.NoText("*IDENTITY*")),
            // 行 31
            DialectDdlRow
                .Ddl(
                    "ddl-identity-column-name-with-newline-is-rejected",
                    "採番列名に改行があれば、DDL の入口が列名を名指しして図全体を断る（src/QuickER.Provider/CLAUDE.md「SQL の信頼境界」）",
                    E.Identity,
                    F.Reject,
                    _ => Diagram(CvGeneratedEntity(CcInjected, computed: false, identity: true))
                )
                .ExpectAll(e => e.Throws("InvalidOperationException: *COLUMN*items*")),
            // 行 32
            DialectDdlRow
                .Ddl(
                    "ddl-description-statements-follow-all-tables-and-foreign-keys",
                    "説明の文は全表と外部キーの後へまとめて出す（MySQL は閉じ括弧のインライン、SQLite は表・列の位置。src/QuickER.Provider/DdlGeneratorBase.cs の AppendDescriptionStatements）",
                    E.Description,
                    F.Order,
                    d =>
                    {
                        var a = Table("a", Pk("id", IntType[d]));
                        a.Description = "about a";
                        var b = Table("b", Pk("id", IntType[d]), Col("a_id", IntType[d]));
                        b.Description = "about b";
                        var diagram = Diagram(a, b);
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
                            "ALTER TABLE [b] ADD CONSTRAINT*",
                            "*@level1name=N'a';*",
                            "*@level1name=N'b';*"
                        )
                )
                .Expect(
                    [Pg, Or],
                    e =>
                        e.Order(
                            O.Text,
                            "CREATE TABLE \"b\" (",
                            "ALTER TABLE \"b\" ADD CONSTRAINT*",
                            "COMMENT ON TABLE \"a\" IS 'about a';",
                            "COMMENT ON TABLE \"b\" IS 'about b';"
                        )
                )
                .Expect(
                    My,
                    e =>
                        e.Order(
                            O.Text,
                            ") COMMENT='about a';",
                            ") COMMENT='about b';",
                            "ALTER TABLE `b` ADD CONSTRAINT*"
                        )
                )
                .Expect(
                    Sl,
                    e =>
                        e.Order(O.Text, "-- a: about a", "-- b: about b")
                            .NoText("*ALTER TABLE*")
                ),
            // 行 33
            DialectDdlRow
                .Ddl(
                    "ddl-description-with-newline-is-not-rejected-and-stays-in-comment",
                    "改行入りの説明は断らない（説明は入口の名前検証の対象外・複数行を許す）。SQLite は -- コメント 1 行へ畳み、後続の行を作らない（src/QuickER.Provider/CLAUDE.md「SQL の信頼境界」の「説明は対象外」と SqlComment.Sanitize）",
                    E.Description,
                    F.Reject,
                    d =>
                    {
                        var entity = Table(
                            "t",
                            Pk("id", IntType[d]),
                            Described(Col("v", IntType[d]), "col1\ncol2")
                        );
                        entity.Description = "line1\nline2";
                        return Diagram(entity);
                    }
                )
                .ExpectAll(e => e.NoException().Lines("table t"))
                .Expect(
                    Sl,
                    e =>
                        e.Text("-- t: line1 line2", "    -- v: col1 col2")
                            .NoText("line2*", "col2*")
                ),
            // 行 34
            DialectDdlRow
                .SyncItems(
                    "sync-add-table-writes-no-description",
                    "表の追加は表・列の説明を書かない（説明を書くのは説明の同期の項目だけ。src/QuickER.Provider/CLAUDE.md「説明の空欄と表せない文字」）",
                    P.SyncAddTable,
                    E.Description,
                    F.Omit,
                    _ =>
                    {
                        var entity = Table(
                            "t",
                            Pk("id", "int"),
                            Described(Col("v", "int"), "about column")
                        );
                        entity.Description = "about table";
                        return new DdlSyncItems([CvAddTable(entity)], null, new SyncPlanContext());
                    }
                )
                .ExpectAll(e => e.Lines("table t").NoText("*about table*", "*about column*")),
            // 行 35
            DialectDdlRow
                .SyncItems(
                    "sync-rebuild-writes-no-description-comment",
                    "特性の固定（文書に定めが無い今の振る舞い）：SQLite の再構築の CREATE TABLE は表・列の説明のコメントを書かない（DDL は書く）。根拠の実装は src/QuickER.Provider.Sqlite/SqliteSyncScriptBuilder.cs の AppendCreateTableBody が説明を出さないこと",
                    P.SyncRebuild,
                    E.Description,
                    F.Omit,
                    _ =>
                    {
                        var live = Table(
                            "t",
                            Pk("id", "INTEGER"),
                            Described(Col("a", "INTEGER", nullable: false), "about column")
                        );
                        live.Description = "about table";
                        return CvLiveItems(
                            [live],
                            [],
                            CvAlterColumn("t", Col("a", "INTEGER", nullable: true))
                        );
                    }
                )
                .Expect(
                    Sl,
                    e => e.Lines("table t_quicker_rebuild").NoText("*about table*", "*about column*")
                ),
        ];
}
