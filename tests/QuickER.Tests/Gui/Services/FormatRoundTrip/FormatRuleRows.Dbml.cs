using QuickER.Dialects;
using QuickER.Model;
using QuickER.Resources;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>DBML の取込の規則の行（手書きの DBML の文字列を取り込む）</summary>
internal static partial class FormatRuleRows
{
    private static IEnumerable<ScenarioRow<FormatRule>> Dbml() =>
        [
            .. DbmlBasicRules(),
            .. DbmlUnsupportedSyntaxRules(),
            .. DbmlRelationshipEndpointRules(),
            .. DbmlLiteralRules(),
            .. DbmlPrimaryKeyOrderRules(),
            .. DbmlConstraintNameRules(),
            .. DbmlUniqueConstraintRules(),
            .. DbmlKeywordColumnNameRules(),
            .. DbmlLongDefaultNameRules(),
        ];

    /// <summary>DbmlTests の取込の規則</summary>
    private static IEnumerable<ScenarioRow<FormatRule>> DbmlBasicRules() =>
        [
            DbmlRule(
                    "dbml-rule-parses-entities-and-relationships",
                    "DBML 読込でエンティティとリレーションを復元できる"
                )
                .Text(
                    Nl(
                        "Table Customer {",
                        "  CustomerId int [pk, not null]",
                        "  CustomerName nvarchar(100) [not null]",
                        "}",
                        string.Empty,
                        "Table Orders {",
                        "  OrderId int [pk, not null]",
                        "  CustomerId int [not null]",
                        "}",
                        string.Empty,
                        "Ref FK_Orders_Customer: Customer.CustomerId < Orders.CustomerId"
                    )
                )
                .Expect(e =>
                    e.Imported.Count("Structure table *", 2)
                        .Count("RelationshipType *", 1)
                        .Single("Structure table Customer")
                        .Single("PrimaryKey Customer.CustomerId")
                        .Single("Structure table Orders")
                        .Single("ForeignKeyMarker Orders.CustomerId")
                        .Single("RelationshipType * = OneToMany")
                        .Single("ConstraintName * = FK_Orders_Customer")
                ),
            DbmlRule(
                    "dbml-rule-ref-line-columns-as-written",
                    "DBML 取込は Ref 行の列名をそのまま端点に使う"
                )
                .Text(
                    Nl(
                        "Table Parent {",
                        "  ParentId int [pk, not null]",
                        "}",
                        string.Empty,
                        "Table Child {",
                        "  ChildId int [pk, not null]",
                        "  ParentId int [null]",
                        "  OwnerId int [null]",
                        "}",
                        string.Empty,
                        "Ref: Parent.ParentId < Child.OwnerId"
                    )
                )
                .Expect(e =>
                    e.Imported.Count("RelationshipType *", 1)
                        .Single("ColumnPairs Parent->Child#0 = ParentId>OwnerId")
                        .None("ForeignKeyMarker Child.ParentId")
                ),
            DbmlRule(
                    "dbml-rule-forward-reference",
                    "DBML は Table より前に書かれた Ref: を取り込める"
                )
                .Text(
                    Nl(
                        "Ref: Parent.ParentId < Child.OwnerId",
                        string.Empty,
                        "Table Parent {",
                        "  ParentId int [pk, not null]",
                        "}",
                        string.Empty,
                        "Table Child {",
                        "  ChildId int [pk, not null]",
                        "  OwnerId int [null]",
                        "}"
                    )
                )
                .Expect(e =>
                    e.Imported.Count("RelationshipType *", 1)
                        .Single("ColumnPairs Parent->Child#0 = ParentId>OwnerId")
                ),
            DbmlRule(
                    "dbml-rule-ref-to-undefined-table",
                    "DBML: 未定義テーブルを参照する Ref: は行番号つきで拒否する"
                )
                .Text(
                    Nl(
                        "Table Parent {",
                        "  ParentId int [pk, not null]",
                        "}",
                        string.Empty,
                        "Ref: Parent.ParentId < Missing.OwnerId"
                    )
                )
                .Rejects(InvalidData("*5*Missing*")),
            DbmlRule("dbml-rule-parse-error-line-number", "DBML 取込の解析エラーに行番号が付く")
                .Text(Nl("Table Customer {", "  CustomerId int [pk]", "  broken", "}"))
                .Rejects(
                    InvalidData(
                        string.Format(
                            Strings.Import_LineDiagnostic,
                            3,
                            string.Format(Strings.Dbml_ColumnParseError, "Customer", "broken")
                        )
                    )
                ),
            DbmlRule(
                    "dbml-rule-index-column-not-found-points-to-index-line",
                    "DBML 取込の索引の未定義列は索引定義行を指す"
                )
                .Text(
                    Nl(
                        "Table Customer {",
                        "  CustomerId int [pk]",
                        "",
                        "  Indexes {",
                        "    (Missing) [unique]",
                        "  }",
                        "}"
                    )
                )
                .Rejects(
                    InvalidData(
                        string.Format(
                            Strings.Import_LineDiagnostic,
                            5,
                            string.Format(Strings.Dbml_IndexColumnNotFound, "Customer", "Missing")
                        )
                    )
                ),
            DbmlRule(
                    "dbml-rule-missing-closing-brace-points-to-block-start",
                    "DBML 取込の未閉じブロックはブロック開始行を指す"
                )
                .Text(Nl("// comment", "Table Customer {", "  CustomerId int [pk]"))
                .Rejects(
                    InvalidData(
                        string.Format(
                            Strings.Import_LineDiagnostic,
                            2,
                            string.Format(Strings.Dbml_MissingClosingBrace, "Customer")
                        )
                    )
                ),
            DbmlRule(
                    "dbml-rule-whole-file-diagnostic-has-no-line-number",
                    "DBML 取込のファイル全体の診断には行番号を付けない"
                )
                .Text("// only a comment")
                .Rejects(InvalidData(Strings.Dbml_NoEntities)),
            DbmlRule(
                    "dbml-rule-table-note-is-not-a-column",
                    "DBML 取込: Note 行はカラムとして取り込まない"
                )
                .Text(
                    Nl(
                        "Table Customer {",
                        "  CustomerId int [pk, not null]",
                        "  Note: 'Stores customers'",
                        "}"
                    )
                )
                .Expect(e =>
                    e.Imported.Count("Structure table *", 1)
                        .Count("Structure column *", 1)
                        .Single("Structure column Customer.CustomerId")
                        .Single("TableDescription Customer = \"Stores customers\"")
                ),
        ];

    /// <summary>最小のテーブル定義（DbmlUnsupportedSyntaxTests の各ケースへ前後に足して使う）</summary>
    private static string DbmlMinimalTable => "Table t {\n  id int [pk, not null]\n}";

    /// <summary>DbmlUnsupportedSyntaxTests の取込の規則</summary>
    private static IEnumerable<ScenarioRow<FormatRule>> DbmlUnsupportedSyntaxRules() =>
        [
            .. new[]
            {
                (
                    "project",
                    "Project my_project {\n  database_type: 'PostgreSQL'\n  Note: 'desc'\n}"
                ),
                ("enum", "enum job_status {\n  created\n  running\n}"),
                ("tablegroup", "TableGroup g {\n  t\n}"),
                ("tablepartial", "TablePartial base {\n  created_at timestamp\n}"),
                ("note", "Note {\n  'project level note'\n}"),
                ("records", "records t(id) {\n  1\n}"),
            }.Select(item =>
                DbmlRule(
                        $"dbml-rule-skippable-top-level-block-{item.Item1}",
                        $"DBML 取込: QuickER が扱わないトップレベルブロックは読み飛ばす（{item.Item1}）"
                    )
                    .Text(Lf(item.Item2, string.Empty, DbmlMinimalTable))
                    .Expect(e =>
                        e.Imported.Count("Structure table *", 1).Single("Structure table t")
                    )
                    .Row()
            ),
            .. new[]
            {
                ("checks", "  checks {\n    `id > 0` [name: 'chk']\n  }"),
                ("records", "  records {\n    1\n  }"),
            }.Select(item =>
                DbmlRule(
                        $"dbml-rule-skippable-table-block-{item.Item1}",
                        $"DBML 取込: テーブル内の未対応ブロックは幻のカラムを作らない（{item.Item1}）"
                    )
                    .Text(Lf("Table t {", "  id int [pk, not null]", item.Item2, "}"))
                    .Expect(e =>
                        e.Imported.Count("Structure table *", 1).Single("ColumnOrder t = id")
                    )
                    .Row()
            ),
            DbmlRule(
                    "dbml-rule-skipped-block-ignores-braces-in-literals",
                    "DBML 取込: 読み飛ばしの波括弧はリテラル内の括弧を数えない"
                )
                .Text(
                    Lf(
                        "Project p {",
                        "  Note: 'a { brace } inside'",
                        "  database_type: '}'",
                        "}",
                        string.Empty,
                        DbmlMinimalTable
                    )
                )
                .Expect(e => e.Imported.Count("Structure table *", 1).Single("Structure table t")),
            DbmlRule(
                    "dbml-rule-skipped-block-ignores-braces-in-triple-quoted-literals",
                    "DBML 取込: 読み飛ばしの波括弧は複数行リテラル内の括弧を数えない"
                )
                .Text(
                    Lf(
                        "Project p {",
                        "  Note: '''",
                        "  a { brace",
                        "  } inside",
                        "  '''",
                        "}",
                        string.Empty,
                        DbmlMinimalTable
                    )
                )
                .Expect(e => e.Imported.Count("Structure table *", 1).Single("Structure table t")),
            DbmlRule(
                    "dbml-rule-unknown-top-level-line",
                    "DBML 取込: 解釈できないトップレベル行は行番号つきで拒否する"
                )
                .Text(Lf(DbmlMinimalTable, string.Empty, "Gibberish here"))
                .Rejects(
                    InvalidData(
                        string.Format(
                            Strings.Import_LineDiagnostic,
                            5,
                            string.Format(Strings.Dbml_UnsupportedLine, "Gibberish here")
                        )
                    )
                ),
            .. new[]
            {
                ("anonymous-block", "Ref {"),
                ("named-block", "Ref fk_name {"),
                ("quoted-name-block", "Ref \"fk name\" {"),
            }.Select(item =>
                DbmlRule(
                        $"dbml-rule-unsupported-relationship-form-{item.Item1}",
                        $"DBML 取込: 未対応のリレーション記法は行番号つきで拒否する（{item.Item2}）"
                    )
                    .Text(Lf(DbmlMinimalTable, string.Empty, item.Item2))
                    .Rejects(
                        InvalidData(
                            string.Format(
                                Strings.Import_LineDiagnostic,
                                5,
                                string.Format(Strings.Dbml_UnsupportedRelationshipForm, item.Item2)
                            )
                        )
                    )
                    .Row()
            ),
            DbmlRule(
                    "dbml-rule-named-ref-takes-name-as-constraint-name",
                    "DBML 取込: 名前付き Ref は名前を制約名として取り込む"
                )
                .Text(
                    Lf(
                        "Table p {",
                        "  id int [pk]",
                        "}",
                        "Table c {",
                        "  pid int",
                        "}",
                        "Ref fk_c_p: p.id < c.pid [delete: cascade]"
                    )
                )
                .Expect(e =>
                    e.Imported.Count("RelationshipType *", 1)
                        .Single("ConstraintName * = fk_c_p")
                        .Single("ReferentialAction * = del=Cascade upd=*")
                        .Count("Structure column c.*", 1)
                        .Single("ForeignKeyMarker c.pid")
                ),
            DbmlRule(
                    "dbml-rule-ref-with-settings-before-endpoints",
                    "DBML 取込: 設定ブロックを端点の前に置いた Ref 行は行番号つきで拒否する"
                )
                .Text(Lf(DbmlMinimalTable, string.Empty, "Ref: [note: 'FK_x'] t.id < t.id"))
                .Rejects(
                    InvalidData(
                        string.Format(
                            Strings.Import_LineDiagnostic,
                            5,
                            string.Format(
                                Strings.Dbml_RelationshipParseError,
                                "Ref: [note: 'FK_x'] t.id < t.id"
                            )
                        )
                    )
                ),
            DbmlRule(
                    "dbml-rule-valueless-ref-setting-is-ignored",
                    "DBML 取込: 値の無い ref 設定は読み飛ばし、ほかの設定は生きる"
                )
                .Text(
                    Lf("Table t {", "  id int [pk]", "  owner int [ref, not null, note: 'x']", "}")
                )
                .Expect(e =>
                    e.Imported.Count("Structure table *", 1)
                        .Single("Structure column t.owner")
                        .None("ForeignKeyMarker t.owner")
                        .Single("Nullability t.owner = notnull")
                        .Single("ColumnDescription t.owner = \"x\"")
                ),
            DbmlRule(
                    "dbml-rule-malformed-table-header-is-rejected",
                    "DBML 取込: 書式に一致しない Table 行はテーブルごと消えずに拒否される"
                )
                .Text(
                    Lf(
                        DbmlMinimalTable,
                        string.Empty,
                        "Table very_long_name as U {",
                        "  id int",
                        "}"
                    )
                )
                .Rejects(InvalidData("*Table very_long_name as U {*")),
            DbmlRule(
                    "dbml-rule-multiline-note-in-table-is-rejected",
                    "DBML 取込: 複数行 Note は幻のカラムにせず拒否する"
                )
                .Text(
                    Lf("Table t {", "  id int [pk, not null]", "  Note: '''", "  x", "  '''", "}")
                )
                .Rejects(
                    InvalidData(
                        string.Format(
                            Strings.Import_LineDiagnostic,
                            3,
                            string.Format(Strings.Dbml_UnsupportedLine, "Note: '''")
                        )
                    )
                ),
            DbmlRule(
                    "dbml-rule-line-comments-are-ignored",
                    "DBML 取込: 行コメントはトップレベル・ブロック内・行末のどこでも無視される"
                )
                .Text(
                    Lf(
                        "// leading comment",
                        "Table t { // trailing comment",
                        "  // inside the block",
                        "  id int [pk, not null] // after the column",
                        "}"
                    )
                )
                .Expect(e => e.Imported.Count("Structure table *", 1).Single("ColumnOrder t = id")),
            DbmlRule(
                    "dbml-rule-block-comments-are-ignored",
                    "DBML 取込: 複数行コメントはトップレベル・ブロック内・行末で無視される"
                )
                .Text(
                    Lf(
                        "/* leading",
                        "   spanning lines */",
                        "Table t { /* trailing */",
                        "  /* inside */",
                        "  id int [pk, not null] /* after */",
                        "}"
                    )
                )
                .Expect(e => e.Imported.Count("Structure table *", 1).Single("ColumnOrder t = id")),
            DbmlRule(
                    "dbml-rule-slashes-inside-literal-are-not-comments",
                    "DBML 取込: リテラル内の // はコメント扱いしない"
                )
                .Text(
                    Lf(
                        "Table t {",
                        "  id int [pk, not null, note: 'see https://example.com/docs']",
                        "}"
                    )
                )
                .Expect(e =>
                    e.Imported.Count("Structure table *", 1)
                        .Count("Structure column *", 1)
                        .Single("ColumnDescription t.id = \"see https://example.com/docs\"")
                ),
            DbmlRule(
                    "dbml-rule-keyword-casing-is-ignored",
                    "DBML 取込: キーワードの大文字小文字を区別しない"
                )
                .Text(
                    Lf(
                        "table parent {",
                        "  id int [PK, NOT NULL]",
                        "  code int [not null]",
                        "  indexes {",
                        "    (id, code) [UNIQUE, name: 'uq']",
                        "  }",
                        "}",
                        "table child {",
                        "  parent_id int [not null]",
                        "}",
                        "ref: parent.id < child.parent_id"
                    )
                )
                .Expect(e =>
                    e.Imported.Count("Structure table *", 2)
                        .Count("RelationshipType *", 1)
                        .Count("UniqueSet parent(*", 1)
                        .Single("UniqueName parent(*) = uq")
                ),
        ];

    /// <summary>DbmlRelationshipEndpointTests の取込の規則（インライン参照・多対 1 記号）</summary>
    private static IEnumerable<ScenarioRow<FormatRule>> DbmlRelationshipEndpointRules() =>
        [
            DbmlRule(
                    "dbml-rule-inline-many-to-one-and-primary-key-setting",
                    "DBML 取込: dbdiagram 形式のインライン参照 ref: > と primary key を読む"
                )
                .Text(
                    """
                    Table users {
                      id integer [primary key]
                    }

                    Table posts {
                      id integer [primary key]
                      user_id integer [ref: > users.id]
                    }
                    """
                )
                .Expect(e =>
                    e.Imported.Count("RelationshipType *", 1)
                        .Single("RelationshipType users->posts#0 = OneToMany")
                        .Single("ColumnPairs users->posts#0 = id>user_id")
                        .Count("PrimaryKey *", 2)
                        .Single("PrimaryKey users.id")
                        .Single("PrimaryKey posts.id")
                        .Single("Nullability users.id = notnull")
                        .Single("Nullability posts.id = notnull")
                        .Single("ForeignKeyMarker posts.user_id")
                ),
            DbmlRule(
                    "dbml-rule-inline-one-to-many-current-column-is-parent",
                    "DBML 取込: インライン参照 ref: < はこの列を親として読む"
                )
                .Text(
                    """
                    Table users {
                      id integer [pk, ref: < posts.user_id]
                    }

                    Table posts {
                      user_id integer
                    }
                    """
                )
                .Expect(e =>
                    e.Imported.Count("RelationshipType *", 1)
                        .Single("RelationshipType users->posts#0 = OneToMany")
                        .Single("ColumnPairs users->posts#0 = id>user_id")
                ),
            DbmlRule(
                    "dbml-rule-inline-one-to-one-current-column-is-child",
                    "DBML 取込: インライン参照 ref: - は ref を書いた列を外部キー（子）として読む"
                )
                .Text(
                    """
                    Table users {
                      id integer [pk]
                    }

                    Table user_infos {
                      user_id integer [ref: - users.id]
                    }
                    """
                )
                .Expect(e =>
                    e.Imported.Count("RelationshipType *", 1)
                        .Single("RelationshipType users->user_infos#0 = OneToOne")
                        .Single("ColumnPairs users->user_infos#0 = id>user_id")
                ),
            DbmlRule(
                    "dbml-rule-ref-line-one-to-one-second-column-is-child",
                    "DBML 取込: Ref 行の 1 対 1 は 2 つ目の列を外部キーとして読む（インラインと逆の対照）"
                )
                .Text(
                    """
                    Table users {
                      id integer [pk]
                    }

                    Table user_infos {
                      user_id integer
                    }

                    Ref: users.id - user_infos.user_id
                    """
                )
                .Expect(e =>
                    e.Imported.Count("RelationshipType *", 1)
                        .Single("RelationshipType users->user_infos#0 = OneToOne")
                        .Single("ColumnPairs users->user_infos#0 = id>user_id")
                ),
            DbmlRule(
                    "dbml-rule-inline-many-to-many",
                    "DBML 取込: インライン参照 ref: <> は多対多として読む"
                )
                .Text(
                    """
                    Table students {
                      id integer [pk, ref: <> courses.id]
                    }

                    Table courses {
                      id integer [pk]
                    }
                    """
                )
                .Expect(e =>
                    e.Imported.Count("RelationshipType *", 1)
                        .Single("RelationshipType * = ManyToMany")
                        .None("ColumnPairs * = *")
                ),
            DbmlRule(
                    "dbml-rule-ref-line-many-to-one-swaps-sides",
                    "DBML 取込: Ref 行の多対1（>）は左右を入れ替えた 1 対多として読む"
                )
                .Text(
                    """
                    Table users {
                      id integer [pk]
                    }

                    Table posts {
                      user_id integer
                    }

                    Ref fk_posts_users: posts.user_id > users.id [delete: cascade]
                    """
                )
                .Expect(e =>
                    e.Imported.Count("RelationshipType *", 1)
                        .Single("RelationshipType users->posts#0 = OneToMany")
                        .Single("ColumnPairs users->posts#0 = id>user_id")
                        .Single("ConstraintName users->posts#0 = fk_posts_users")
                        .Single("ReferentialAction users->posts#0 = del=Cascade upd=*")
                ),
            DbmlRule(
                    "dbml-rule-inline-reference-quoted-and-forward",
                    "DBML 取込: インライン参照は引用識別子と、後ろに定義されるテーブルへの参照を読む"
                )
                .Text(
                    """
                    Table "order lines" {
                      "order id" integer [ref: > "sales orders"."order id"]
                    }

                    Table "sales orders" {
                      "order id" integer [pk]
                    }
                    """
                )
                .Expect(e =>
                    e.Imported.Count("RelationshipType *", 1)
                        .Single("RelationshipType sales orders->order lines#0 = OneToMany")
                        .Single("ColumnPairs sales orders->order lines#0 = order id>order id")
                ),
            DbmlRule(
                    "dbml-rule-inline-and-ref-lines-keep-document-order",
                    "DBML 取込: リレーションは Ref 行とインライン参照の書かれた順に並ぶ"
                )
                .Text(
                    """
                    Ref first: a.id < b.a_id

                    Table a {
                      id integer [pk]
                    }

                    Table b {
                      a_id integer
                      c_id integer [ref: > c.id]
                    }

                    Table c {
                      id integer [pk]
                    }

                    Ref last: a.id < c.id
                    """
                )
                .Expect(e =>
                    e.Imported.That(
                        "制約名の行が first・名前なし・last の順",
                        FormatShapeRows.InOrder(
                            "ConstraintName ",
                            "ConstraintName a->b#0 = first",
                            "ConstraintName c->b#0 = -",
                            "ConstraintName a->c#0 = last"
                        )
                    )
                ),
            DbmlRule(
                    "dbml-rule-unsupported-inline-reference-is-rejected",
                    "DBML 取込: 読めないインライン参照は項目と行を名指しして断る"
                )
                .Text(
                    """
                    Table users {
                      id integer [pk]
                    }

                    Table posts {
                      user_id integer [ref: > public.users.id]
                    }
                    """
                )
                .Rejects(InvalidData("*6*ref: > public.users.id*")),
            DbmlRule(
                    "dbml-rule-bare-ref-setting-is-ignored",
                    "DBML 取込: 値なしの ref 設定は従来どおり読み飛ばす"
                )
                .Text(
                    """
                    Table posts {
                      user_id integer [ref]
                    }
                    """
                )
                .Expect(e =>
                    e.Imported.None("RelationshipType *").None("ForeignKeyMarker posts.user_id")
                ),
            // DbmlQuotedIdentifierTests
            DbmlRule(
                    "dbml-rule-unquoted-array-type-keeps-brackets",
                    "DBML 取込: 囲まれていない配列型も型を落とさない"
                )
                .Text(Lf("Table t {", "  id int [pk, not null]", "  tags integer[] [null]", "}"))
                .Expect(e =>
                    e.Imported.Count("Structure table *", 1).Single("Type t.tags = integer[]")
                ),
        ];

    /// <summary>DbmlLiteralFidelityTests の取込の規則（他ツール形式のリテラル）</summary>
    private static IEnumerable<ScenarioRow<FormatRule>> DbmlLiteralRules() =>
        [
            DbmlRule(
                    "dbml-rule-note-ending-with-backslash-followed-by-settings",
                    "DBML 取込: バックスラッシュで終わる note の後ろの設定も読む（他ツール形式）"
                )
                .Text(
                    """
                    Table items {
                      id int [pk]
                      path varchar [note: 'C:\\', not null, unique]
                      Note: 'ends with \\'
                    }
                    """
                )
                .Expect(e =>
                    e.Imported.Single("ColumnOrder items = id,path")
                        .Single("ColumnDescription items.path = \"C:\\\"")
                        .Single("Nullability items.path = notnull")
                        .Count("UniqueSet *", 1)
                        .Single("TableDescription items = \"ends with \\\"")
                ),
            DbmlRule(
                    "dbml-rule-index-name-ending-with-backslash-followed-by-settings",
                    "DBML 取込: バックスラッシュで終わる索引名の後ろの設定も読む"
                )
                .Text(
                    """
                    Table items {
                      id int [pk]
                      code varchar

                      Indexes {
                        code [name: 'UQ\\', unique]
                      }
                    }
                    """
                )
                .Expect(e => e.Imported.Count("UniqueSet *", 1).Single("UniqueName * = UQ\\")),
            DbmlRule(
                    "dbml-rule-ref-settings-with-bracket-in-literal",
                    "DBML 取込: Ref 行の設定ブロック内の文字列に ] があっても参照アクションを読む"
                )
                .Text(
                    """
                    Table a {
                      id int [pk]
                    }

                    Table b {
                      a_id int
                    }

                    Ref: a.id < b.a_id [note: 'x]y', delete: cascade]
                    """
                )
                .Expect(e =>
                    e.Imported.Count("RelationshipType *", 1)
                        .Single("ReferentialAction * = del=Cascade upd=*")
                ),
        ];

    /// <summary>行を <see cref="Environment.NewLine"/> でつなぎ、最後にも改行を置く（DbmlPrimaryKeyOrderTests の Lines）</summary>
    private static string NlTerminated(params string[] lines) => Nl(lines) + Environment.NewLine;

    /// <summary>DbmlPrimaryKeyOrderTests の取込の規則（[pk] 索引）</summary>
    private static IEnumerable<ScenarioRow<FormatRule>> DbmlPrimaryKeyOrderRules() =>
        [
            DbmlRule(
                    "dbml-rule-primary-key-index-only",
                    "DBML: 列ごとの pk の無い [pk] 索引だけの主キーを読める"
                )
                .Text(
                    NlTerminated(
                        "Table order_lines {",
                        "  tenant_id int",
                        "  order_no int",
                        "  Indexes {",
                        "    (order_no, tenant_id) [pk, name: 'pk_order_lines']",
                        "  }",
                        "}"
                    )
                )
                .Expect(e =>
                    e.Imported.Count("Structure table *", 1)
                        .Single("PrimaryKeyOrder order_lines = order_no,tenant_id")
                        .Count("Structure column *", 2)
                        .Count("PrimaryKey *", 2)
                        .None("Nullability * = null")
                ),
            DbmlRule(
                    "dbml-rule-primary-key-same-set-in-both-places-uses-index-order",
                    "DBML: 列ごとの pk と [pk] 索引が同じ集合なら、順序は索引を採る"
                )
                .Text(
                    NlTerminated(
                        "Table order_lines {",
                        "  tenant_id int [pk]",
                        "  order_no int [pk]",
                        "  Indexes {",
                        "    (order_no, tenant_id) [pk]",
                        "  }",
                        "}"
                    )
                )
                .Expect(e =>
                    e.Imported.Count("Structure table *", 1)
                        .Single("PrimaryKeyOrder order_lines = order_no,tenant_id")
                ),
            DbmlRule(
                    "dbml-rule-primary-key-conflicting-sets-are-rejected",
                    "DBML: 列ごとの pk と [pk] 索引の集合が食い違えば取込を断る"
                )
                .Text(
                    NlTerminated(
                        "Table order_lines {",
                        "  tenant_id int [pk]",
                        "  order_no int",
                        "  memo varchar(50)",
                        "  Indexes {",
                        "    (order_no, tenant_id) [pk]",
                        "  }",
                        "}"
                    )
                )
                .Rejects(
                    InvalidData(
                        string.Format(
                            Strings.Import_LineDiagnostic,
                            6,
                            string.Format(
                                Strings.Dbml_PrimaryKeyConflict,
                                "order_lines",
                                "tenant_id",
                                "order_no, tenant_id"
                            )
                        )
                    )
                ),
            DbmlRule(
                    "dbml-rule-two-primary-key-indexes-are-rejected",
                    "DBML: [pk] 索引が 2 つあれば取込を断る"
                )
                .Text(
                    NlTerminated(
                        "Table order_lines {",
                        "  tenant_id int",
                        "  order_no int",
                        "  Indexes {",
                        "    (order_no, tenant_id) [pk]",
                        "    (tenant_id, order_no) [pk]",
                        "  }",
                        "}"
                    )
                )
                .Rejects(
                    InvalidData(
                        string.Format(
                            Strings.Import_LineDiagnostic,
                            6,
                            string.Format(Strings.Dbml_PrimaryKeyIndexDuplicate, "order_lines")
                        )
                    )
                ),
            DbmlRule(
                    "dbml-rule-duplicate-column-in-primary-key-index-is-rejected",
                    "DBML: [pk] 索引の中で同じ列が 2 回出れば取込を断る"
                )
                .Text(
                    NlTerminated(
                        "Table order_lines {",
                        "  tenant_id int",
                        "  Indexes {",
                        "    (tenant_id, tenant_id) [pk]",
                        "  }",
                        "}"
                    )
                )
                .Rejects(
                    InvalidData(
                        string.Format(
                            Strings.Import_LineDiagnostic,
                            4,
                            string.Format(
                                Strings.Dbml_PrimaryKeyIndexDuplicateColumn,
                                "order_lines",
                                "tenant_id"
                            )
                        )
                    )
                ),
            DbmlRule(
                    "dbml-rule-primary-key-index-unknown-column-is-rejected",
                    "DBML: [pk] 索引が未定義の列を指せば取込を断る"
                )
                .Text(
                    NlTerminated(
                        "Table order_lines {",
                        "  tenant_id int",
                        "  Indexes {",
                        "    (tenant_id, no_such_column) [pk]",
                        "  }",
                        "}"
                    )
                )
                .Rejects(InvalidData("*no_such_column*")),
        ];

    /// <summary>customers・orders の 2 テーブルと、与えた Ref 行の DBML（DbmlConstraintNameTests.BuildText）</summary>
    private static string DbmlCustomersAndOrders(params string[] refLines) =>
        Lf([
            "Table customers {",
            "  id int [pk]",
            "}",
            "",
            "Table orders {",
            "  id int [pk]",
            "  bill_customer_id int",
            "  ship_customer_id int",
            "}",
            "",
            .. refLines,
        ]);

    /// <summary>DbmlConstraintNameTests の取込の規則（名前の無い Ref の外部キー名）</summary>
    private static IEnumerable<ScenarioRow<FormatRule>> DbmlConstraintNameRules() =>
        [
            DbmlRule(
                    "dbml-rule-same-pair-twice-without-names-second-gets-unique-name",
                    "同じテーブルの組に名前の無い Ref が 2 本あれば、2 本目に子の列名つきの名前を付ける"
                )
                .Text(
                    DbmlCustomersAndOrders(
                        "Ref: customers.id < orders.bill_customer_id",
                        "Ref: customers.id < orders.ship_customer_id"
                    )
                )
                .Expect(e =>
                {
                    e.Imported.Count("RelationshipType *", 2)
                        .Single("ColumnPairs customers->orders#0 = id>bill_customer_id")
                        .Single("ConstraintName customers->orders#0 = -")
                        .Single("ColumnPairs customers->orders#1 = id>ship_customer_id")
                        .Single(
                            "ConstraintName customers->orders#1 = FK_orders_customers_ship_customer_id"
                        );
                    e.ImportedDiagram.Add(DistinctDdlConstraintNames());
                }),
            DbmlRule(
                    "dbml-rule-single-unnamed-ref-stays-unnamed",
                    "名前の無い Ref が 1 本だけなら名前を持たないまま"
                )
                .Text(DbmlCustomersAndOrders("Ref: customers.id < orders.bill_customer_id"))
                .Expect(e =>
                {
                    e.Imported.Count("RelationshipType *", 1)
                        .Single("ColumnPairs customers->orders#0 = id>bill_customer_id")
                        .Single("ConstraintName customers->orders#0 = -");
                    e.ImportedDiagram.Add(DistinctDdlConstraintNames());
                }),
            DbmlRule(
                    "dbml-rule-named-ref-takes-default-unnamed-is-renamed",
                    "名前付き Ref が既定名をふさいでいれば、名前の無い Ref に名前を付け、名前付きの名前は変えない"
                )
                .Text(
                    DbmlCustomersAndOrders(
                        "Ref: customers.id < orders.bill_customer_id",
                        "Ref FK_orders_customers: customers.id < orders.ship_customer_id"
                    )
                )
                .Expect(e =>
                {
                    e.Imported.Count("RelationshipType *", 2)
                        .Single("ColumnPairs customers->orders#0 = id>bill_customer_id")
                        .Single(
                            "ConstraintName customers->orders#0 = FK_orders_customers_bill_customer_id"
                        )
                        .Single("ColumnPairs customers->orders#1 = id>ship_customer_id")
                        .Single("ConstraintName customers->orders#1 = FK_orders_customers");
                    e.ImportedDiagram.Add(DistinctDdlConstraintNames());
                }),
            DbmlRule(
                    "dbml-rule-named-refs-keep-their-names",
                    "名前付き Ref の名前は、名前の無い Ref と並んでもそのまま"
                )
                .Text(
                    DbmlCustomersAndOrders(
                        "Ref fk_bill: customers.id < orders.bill_customer_id",
                        "Ref: customers.id < orders.ship_customer_id"
                    )
                )
                .Expect(e =>
                {
                    e.Imported.Count("RelationshipType *", 2)
                        .Single("ColumnPairs customers->orders#0 = id>bill_customer_id")
                        .Single("ConstraintName customers->orders#0 = fk_bill")
                        .Single("ColumnPairs customers->orders#1 = id>ship_customer_id")
                        .Single("ConstraintName customers->orders#1 = -");
                    e.ImportedDiagram.Add(DistinctDdlConstraintNames());
                }),
        ];

    /// <summary>DbmlUniqueConstraintTests の取込の規則（Indexes ブロック）</summary>
    private static IEnumerable<ScenarioRow<FormatRule>> DbmlUniqueConstraintRules() =>
        [
            DbmlRule(
                    "dbml-rule-indexes-block-parses-unique-indexes-only",
                    "DBML 取込は Indexes ブロックの unique 索引だけを一意制約として取り込む"
                )
                .Text(
                    Nl(
                        "Table Customer {",
                        "  CustomerId int [pk, not null]",
                        "  Email nvarchar(100) [not null]",
                        "  TenantId int [not null]",
                        "  Code nvarchar(20) [not null]",
                        string.Empty,
                        "  Indexes {",
                        "    (TenantId, Code) [unique, name: 'UQ_Customer_Tenant']",
                        "    Email [unique]",
                        // unique でない索引は一意制約ではないため無視される
                        "    (CustomerId) [name: 'IX_Customer_Id']",
                        "  }",
                        "}"
                    )
                )
                .Expect(e =>
                    e.Imported.Count("Structure table *", 1)
                        .Count("UniqueSet *", 2)
                        .Single("UniqueName Customer(Code,TenantId) = UQ_Customer_Tenant")
                        .Single("UniqueName Customer(Email) = -")
                ),
            DbmlRule(
                    "dbml-rule-index-referencing-unknown-column-is-rejected",
                    "DBML 取込は索引が参照する未定義カラムをエラーにする"
                )
                .Text(
                    Nl(
                        "Table Customer {",
                        "  CustomerId int [pk, not null]",
                        "  Indexes {",
                        "    (NoSuchColumn) [unique]",
                        "  }",
                        "}"
                    )
                )
                .Rejects(InvalidData("*NoSuchColumn*")),
        ];

    /// <summary>DbmlKeywordColumnNameTests の取込の規則（1 行で閉じる checks / records ブロック）</summary>
    private static IEnumerable<ScenarioRow<FormatRule>> DbmlKeywordColumnNameRules() =>
        [
            .. new[] { "checks { `price > 0` }", "records { 1 }", "records t(id) { 1 }" }.Select(
                (block, index) =>
                    DbmlRule(
                            $"dbml-rule-single-line-skippable-block-is-not-a-column-{index}",
                            $"DBML: 1 行で閉じる checks / records ブロックは列にならない（{block}）"
                        )
                        .Text(
                            $"Table Audit {{{Environment.NewLine}  AuditId int [pk]{Environment.NewLine}  {block}{Environment.NewLine}}}{Environment.NewLine}"
                        )
                        .Expect(e =>
                            e.Imported.Count("Structure table *", 1)
                                .Count("Structure column *", 1)
                                .Single("Structure column Audit.AuditId")
                        )
                        .Row()
            ),
        ];

    /// <summary>60 文字の親テーブル名（ImportLongDefaultNameTests）</summary>
    private static string LongParentName => new('p', 60);

    /// <summary>60 文字の子テーブル名（ImportLongDefaultNameTests）</summary>
    private static string LongChildName => new('c', 60);

    /// <summary>長い名前の親子に名前の無い外部キーを 2 本張った DBML（ImportLongDefaultNameTests.Dbml）</summary>
    private static string LongNameDbml =>
        $$"""
            Table {{LongParentName}} {
              id int [pk]
            }

            Table {{LongChildName}} {
              id int [pk]
              bill_id int
              ship_id int
            }

            Ref: {{LongChildName}}.bill_id > {{LongParentName}}.id
            Ref: {{LongChildName}}.ship_id > {{LongParentName}}.id
            """;

    /// <summary>ImportLongDefaultNameTests の DBML の取込の規則</summary>
    private static IEnumerable<ScenarioRow<FormatRule>> DbmlLongDefaultNameRules() =>
        [
            DbmlRule(
                    "dbml-rule-long-default-name-postgresql",
                    "DBML: 2 本目の外部キーに付ける名前は、取込先の対象 DBMS（PostgreSQL の 63 バイト）に収める"
                )
                .Text(LongNameDbml)
                .TargetDbms(DialectNames.PostgreSql)
                .Expect(e =>
                {
                    e.ImportedDiagram.Add(
                        ("対象 DBMS が postgresql", d => d.TargetDbms == "postgresql")
                    );
                    e.ImportedDiagram.Add(
                        (
                            "既定名のままの 1 本目は名前を持たない",
                            d =>
                                d.Relationships.Count >= 2
                                && d.Relationships[0].ConstraintName is null
                        )
                    );
                    e.ImportedDiagram.Add(
                        (
                            "2 本目の名前が PostgreSQL の上限に収まり、DDL が付ける名前と同じ",
                            d =>
                                d.Relationships.Count >= 2
                                && d.Relationships[1].ConstraintName is { } second
                                && IdentifierLengthLimits.PostgreSql.Fits(second)
                                && second
                                    == ConstraintNames.ResolveForeignKeyNames(
                                        d.Entities,
                                        d.Relationships,
                                        IdentifierLengthLimits.PostgreSql
                                    )[d.Relationships[1]]
                        )
                    );
                }),
            DbmlRule(
                    "dbml-rule-long-default-name-sqlite",
                    "DBML: 上限の無い対象 DBMS（SQLite）では縮めない"
                )
                .Text(LongNameDbml)
                .TargetDbms(DialectNames.Sqlite)
                .Expect(e =>
                    e.ImportedDiagram.Add(
                        (
                            "2 本目の名前が縮めない FK_{子}_{親}_ship_id",
                            d =>
                                d.Relationships.Count >= 2
                                && d.Relationships[1].ConstraintName
                                    == $"FK_{LongChildName}_{LongParentName}_ship_id"
                        )
                    )
                ),
        ];
}
