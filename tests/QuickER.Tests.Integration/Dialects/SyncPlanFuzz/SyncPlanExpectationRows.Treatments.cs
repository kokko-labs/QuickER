using System.Collections.Generic;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 差分と計画の警告の段の期待の行（方言が扱えない定義の扱い：NOT NULL の子の列への SET DEFAULT・ドットを含む表名・空白だけの説明・
/// BMP の外の文字の説明・前後に空白のある DB の名前）。方言ごとの期待は、方言の宣言から導かず絶対値で書く
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    /// <summary>絵文字（サロゲート対）を 2 つ含む説明</summary>
    private const string TreatmentEmoji = "a\U0001F600b\U0001F600c";

    /// <summary>MySQL が絵文字を保存する形</summary>
    private const string TreatmentEmojiStored = "a?b?c";

    /// <summary>空白だけの説明（タブを含む）</summary>
    private const string TreatmentBlank = " \t ";

    /// <summary>NOT NULL の子の列への SET DEFAULT の警告（SQL Server だけが出す）</summary>
    private const string SetDefaultWarning =
        "SetDefaultOnNotNullColumn(child): FK_child_parent (parent_id)";

    /// <summary>説明を保存できない方言（MySQL）が出す、BMP の外の文字を置き換える警告（表・列の順）</summary>
    private static string[] DescriptionReplacedWarnings =>
        ["DescriptionCharactersReplaced(t): t", "DescriptionCharactersReplaced(t): t.v"];

    /// <summary>説明を持つ 4 方言（SQLite は説明を持たない）</summary>
    private static string[] DescriptionDialects =>
        [DialectNames.SqlServer, DialectNames.PostgreSql, DialectNames.MySql, DialectNames.Oracle];

    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> TreatmentRows() =>
        [
            // 外部キーの SET DEFAULT（NOT NULL の子の列）
            SetDefaultAddRow(
                    Row(
                        "plan-set-default-on-not-null-child-warns-only-where-declared",
                        "同期: NOT NULL の子の列への SET DEFAULT の追加は SQL Server の計画だけが警告する",
                        AllDialects
                    ),
                    childNullable: false
                )
                .Expect(e =>
                {
                    e.For(
                        DialectNames.SqlServer,
                        d =>
                            d.Warnings.Count("SetDefaultOnNotNullColumn*", 1)
                                .Warnings.Single(SetDefaultWarning)
                    );

                    foreach (var dialect in AllDialects)
                    {
                        if (dialect != DialectNames.SqlServer)
                        {
                            e.For(dialect, d => d.Warnings.Absent("SetDefaultOnNotNullColumn*"));
                        }
                    }
                }),
            SetDefaultAddRow(
                    Row(
                        "plan-set-default-on-nullable-child-does-not-warn",
                        "同期: NULL を許す子の列への SET DEFAULT の追加は SQL Server でも警告しない",
                        DialectNames.SqlServer
                    ),
                    childNullable: true
                )
                .Expect(e => e.Warnings.Absent("SetDefaultOnNotNullColumn*")),
            SetDefaultAddRow(
                    Row(
                        "plan-set-default-on-primary-key-child-flagged-nullable-warns-like-not-null",
                        "同期: DB に無い表の、図で主キーの子の列への SET DEFAULT の追加は、NULL 許容の印があっても NOT NULL の列と同じく SQL Server が警告する",
                        DialectNames.SqlServer
                    ),
                    childNullable: true,
                    childKeyFlaggedNullable: true
                )
                .Expect(e =>
                    e.Warnings.Count("SetDefaultOnNotNullColumn*", 1)
                        .Warnings.Single(SetDefaultWarning)
                ),
            // 図でだけ主キーにした子の列（DB では NULL を許す主キーでない列）は、主キーの変更を選ばなければ同期後も NULL を許すので
            // 警告しない（同期後の値は、選んでいない主キーの変更を図の主キーで読まない）
            SetDefaultAddRow(
                    Row(
                        "plan-set-default-on-child-key-only-in-diagram-without-primary-key-change-does-not-warn",
                        "同期: DB では NULL を許す主キーでない子の列を図でだけ主キーにしても、主キーの変更を選ばなければ列は NULL を許すままなので、SET DEFAULT の追加を SQL Server でも警告しない",
                        DialectNames.SqlServer
                    ),
                    childNullable: true,
                    childKeyOnlyInDiagram: true
                )
                .Expect(e => e.Warnings.Absent("SetDefaultOnNotNullColumn*")),
            Row(
                    "plan-alter-foreign-key-to-set-default-on-not-null-child-warns-only-where-declared",
                    "同期: 外部キーを NOT NULL の子の列への SET DEFAULT へ作り直す変更も SQL Server の計画だけが警告する",
                    DialectNames.SqlServer,
                    DialectNames.PostgreSql,
                    DialectNames.Sqlite
                )
                .Live(s =>
                {
                    s.Table("parent", S.Pk("id", "int"));
                    s.Table("child", S.Pk("id", "int"), S.NotNull("parent_id", "int"));
                    s.Fk("parent", "child", "FK_child_parent", ("id", "parent_id"));
                })
                .TargetFromLive(s =>
                    s.Relationships[0].OnDelete = ForeignKeyReferentialAction.SetDefault
                )
                .Select(items => items[0].IsSelected = true)
                .Expect(e =>
                {
                    e.Diff.Exact("AlterForeignKey *");
                    e.For(
                        DialectNames.SqlServer,
                        d =>
                            d.Warnings.Count("SetDefaultOnNotNullColumn*", 1)
                                .Warnings.Single(SetDefaultWarning)
                    );
                    e.For(
                        DialectNames.PostgreSql,
                        d => d.Warnings.Absent("SetDefaultOnNotNullColumn*")
                    );
                    e.For(
                        DialectNames.Sqlite,
                        d => d.Warnings.Absent("SetDefaultOnNotNullColumn*")
                    );
                }),
            // 子の列の NULL 許容は同期後の値で見る：図は NULL 許容でも、NULL 許容化を選ばなければ DB の列は NOT NULL のまま
            Row(
                    "plan-set-default-on-child-kept-not-null-by-unselected-alter-warns",
                    "同期: 図で NULL 許容にした子の列への SET DEFAULT の追加は、NULL 許容化を選ばなければ列が NOT NULL のままなので、SQL Server の計画が警告する",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    s.Table("parent", S.Pk("id", "int"));
                    s.Table("child", S.Pk("id", "int"), S.NotNull("parent_id", "int"));
                })
                .TargetFromLive(s =>
                {
                    s.Col("child", "parent_id").IsNullable = true;
                    s.Fk("parent", "child", "FK_child_parent", ("id", "parent_id")).OnDelete =
                        ForeignKeyReferentialAction.SetDefault;
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AddForeignKey))
                .Expect(e =>
                    e.Warnings.Count("SetDefaultOnNotNullColumn*", 1)
                        .Warnings.Single(SetDefaultWarning)
                )
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Outcome.Exact("failed: *");
                        e.Unchanged();
                    })
                ),
            // 逆向き：図は NOT NULL でも、NOT NULL 化を選ばなければ DB の列は NULL を許すままなので警告しない
            Row(
                    "plan-set-default-on-child-kept-nullable-by-unselected-alter-does-not-warn",
                    "同期: 図で NOT NULL にした子の列への SET DEFAULT の追加は、NOT NULL 化を選ばなければ列が NULL を許すままなので、SQL Server でも警告しない",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    s.Table("parent", S.Pk("id", "int"));
                    s.Table("child", S.Pk("id", "int"), S.Nullable("parent_id", "int"));
                })
                .TargetFromLive(s =>
                {
                    s.Col("child", "parent_id").IsNullable = false;
                    s.Fk("parent", "child", "FK_child_parent", ("id", "parent_id")).OnDelete =
                        ForeignKeyReferentialAction.SetDefault;
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AddForeignKey))
                .Expect(e => e.Warnings.Absent("SetDefaultOnNotNullColumn*"))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Outcome.Exact("committed");
                        e.After.Count("fk *", 1);
                    })
                ),
            // SET DEFAULT の外部キーを付ける文の 2 つの腕（選んだ追加・列の変更の暗黙の付け直し）× 子の列の DB の既定値の有無
            // （3 回目の効果測定の PE3・台帳の項目 pe3）
            SetDefaultArmRow(readd: false, withDefault: false),
            SetDefaultArmRow(readd: false, withDefault: true),
            SetDefaultArmRow(readd: true, withDefault: false),
            SetDefaultArmRow(readd: true, withDefault: true),
            // ドットを含む表名
            Row(
                    "plan-dotted-table-name-warns-only-where-declared",
                    "同期: 選んだ項目が触れるドット付きの表は SQLite だけが表ごとに警告する",
                    AllDialects
                )
                .Live(s =>
                {
                    s.Table("sales.orders", S.Pk("id", "INTEGER"));
                    s.Table("plain", S.Pk("id", "INTEGER"), S.NotNull("order_id", "INTEGER"));
                })
                .Items(w =>
                {
                    var parent = w.Live["sales.orders"];
                    var child = w.Live["plain"];
                    return
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddForeignKey,
                            TableName = child.TableName,
                            ColumnName = "order_id",
                            ForeignKeyColumnPairs = [new("id", "order_id")],
                            ParentEntity = parent,
                            ChildEntity = child,
                            Relationship = new Relationship
                            {
                                SourceEntityId = parent.Id,
                                TargetEntityId = child.Id,
                                Type = RelationshipType.OneToMany,
                                ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                                ConstraintName = "FK_plain_orders",
                            },
                            IsSelected = true,
                        },
                    ];
                })
                .Expect(e =>
                {
                    e.For(
                        DialectNames.Sqlite,
                        d =>
                            d.Warnings.Count("SchemaQualifiedTableName*", 1)
                                .Warnings.Single("SchemaQualifiedTableName(sales.orders)")
                    );

                    foreach (var dialect in AllDialects)
                    {
                        if (dialect != DialectNames.Sqlite)
                        {
                            e.For(dialect, d => d.Warnings.Absent("SchemaQualifiedTableName*"));
                        }
                    }
                }),
            // スキーマをまたぐ外部キー：SQLite は作らずに注記する（同期スクリプトの面は実 SQLite の行
            // realdb-sqlite-cross-schema-fk-to-dotted-parent-is-omitted-with-note）。計画は両端のドットの表を名指しする
            Row(
                    "plan-cross-schema-fk-names-both-dotted-ends-only-where-declared",
                    "同期: 子と親が違うスキーマの外部キーの追加は、SQLite だけが両端のドットの表を表ごとに警告する",
                    AllDialects
                )
                .Live(s =>
                {
                    s.Table("temp.regions", S.Pk("id", "INTEGER"));
                    s.Table("main.notes", S.Pk("id", "INTEGER"), S.NotNull("region_id", "INTEGER"));
                })
                .Items(w =>
                {
                    var parent = w.Live["temp.regions"];
                    var child = w.Live["main.notes"];
                    return
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddForeignKey,
                            TableName = child.TableName,
                            ColumnName = "region_id",
                            ForeignKeyColumnPairs = [new("id", "region_id")],
                            ParentEntity = parent,
                            ChildEntity = child,
                            Relationship = new Relationship
                            {
                                SourceEntityId = parent.Id,
                                TargetEntityId = child.Id,
                                Type = RelationshipType.OneToMany,
                                ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                            },
                            IsSelected = true,
                        },
                    ];
                })
                .Expect(e =>
                {
                    e.For(
                        DialectNames.Sqlite,
                        d =>
                            d.Warnings.Count("SchemaQualifiedTableName*", 2)
                                .Warnings.Contains(
                                    "SchemaQualifiedTableName(temp.regions)",
                                    "SchemaQualifiedTableName(main.notes)"
                                )
                    );

                    foreach (var dialect in AllDialects)
                    {
                        if (dialect != DialectNames.Sqlite)
                        {
                            e.For(dialect, d => d.Warnings.Absent("SchemaQualifiedTableName*"));
                        }
                    }
                }),
            // 空白だけの説明
            BlankDescriptionDiffRow(
                    "diff-blank-description-null-to-blank-is-same-as-none",
                    "差分: 説明の無い DB と空白だけの説明の図は、説明の差分を出さない",
                    null,
                    TreatmentBlank
                )
                .Expect(e => e.Diff.Absent("Set*Description *")),
            BlankDescriptionDiffRow(
                    "diff-blank-description-blank-to-null-is-same-as-none",
                    "差分: 空白だけの説明の DB と説明の無い図は、説明の差分を出さない",
                    TreatmentBlank,
                    null
                )
                .Expect(e => e.Diff.Absent("Set*Description *")),
            BlankDescriptionDiffRow(
                    "diff-blank-description-removal-of-note-sets-empty",
                    "差分: 説明のある DB を空白だけの説明の図へ合わせる差分は、説明の削除（新しい値は空）になる",
                    "note",
                    TreatmentBlank
                )
                .Expect(e =>
                    e.Diff.Count("Set*Description *", 2)
                        .Diff.Single(
                            "SetTableDescription t * newdesc=\"\" *",
                            "SetColumnDescription t.v * newdesc=\"\" *"
                        )
                ),
            Row(
                    "diff-blank-description-on-new-table-adds-no-description-item",
                    "差分: 新しい表の空白だけの説明は説明の差分項目を作らない",
                    DescriptionDialects
                )
                .Target(s => DescriptionTable(s, TreatmentBlank, TreatmentBlank))
                .Expect(e => e.Diff.Absent("Set*Description *")),
            Row(
                    "diff-blank-description-on-new-column-adds-no-description-item",
                    "差分: 新しい列の空白だけの説明は説明の差分項目を作らない",
                    DescriptionDialects
                )
                .Live(s => DescriptionTable(s, null, null))
                .Target(s =>
                {
                    DescriptionTable(s, TreatmentBlank, TreatmentBlank);
                    var added = S.Nullable("w", "int");
                    added.Description = TreatmentBlank;
                    s["t"].Columns.Add(added);
                })
                .Expect(e => e.Diff.Absent("Set*Description *")),
            // BMP の外の文字の説明
            Row(
                    "diff-description-stored-form-folds-only-where-declared",
                    "差分: 説明の BMP の外の文字を保存できない方言（MySQL）だけが、保存する形で比べる",
                    AllDialects
                )
                .Live(s => DescriptionTable(s, TreatmentEmojiStored, TreatmentEmojiStored))
                .Target(s => DescriptionTable(s, TreatmentEmoji, TreatmentEmoji))
                .Expect(e =>
                {
                    ExpectDescriptionItemCount(e, DialectNames.SqlServer, 2);
                    ExpectDescriptionItemCount(e, DialectNames.PostgreSql, 2);
                    ExpectDescriptionItemCount(e, DialectNames.MySql, 0);
                    ExpectDescriptionItemCount(e, DialectNames.Oracle, 2);
                    ExpectDescriptionItemCount(e, DialectNames.Sqlite, 0);
                }),
            Row(
                    "diff-description-stored-form-different-text-still-differs",
                    "差分: 保存する形でも違う説明は、説明を持つ方言で差分になる",
                    AllDialects
                )
                .Live(s => DescriptionTable(s, "a?b", "a?b"))
                .Target(s => DescriptionTable(s, "a\U0001F600c", "a\U0001F600c"))
                .Expect(e =>
                {
                    ExpectDescriptionItemCount(e, DialectNames.SqlServer, 2);
                    ExpectDescriptionItemCount(e, DialectNames.PostgreSql, 2);
                    ExpectDescriptionItemCount(e, DialectNames.MySql, 2);
                    ExpectDescriptionItemCount(e, DialectNames.Oracle, 2);
                    ExpectDescriptionItemCount(e, DialectNames.Sqlite, 0);
                }),
            DescriptionReplacedRow(
                "plan-description-characters-replaced-emoji",
                "計画: BMP の外の文字を書く説明の項目は、保存できない方言（MySQL）だけが表・列ごとに名指しする",
                TreatmentEmoji,
                replacedOnMySql: true
            ),
            DescriptionReplacedRow(
                "plan-description-characters-replaced-bmp-only-note",
                "計画: BMP の中だけの説明の項目は、どの方言も名指ししない",
                "note",
                replacedOnMySql: false
            ),
            DescriptionReplacedRow(
                "plan-description-characters-replaced-lone-surrogate",
                "計画: 対になっていないサロゲートを書く説明の項目も、保存できない方言（MySQL）だけが名指しする",
                "x\uD800y",
                replacedOnMySql: true
            ),
            // 前後に空白のある DB の名前
            Row(
                    "diff-padded-live-names-are-folded",
                    "差分: 前後に空白のある DB の名前は、空白を除いた図の名前と同じものとして畳む",
                    C.Neutral
                )
                .Live(s => s.Table(" Orders ", S.Pk(" Id ", "int")))
                .Target(s => s.Table("Orders", S.Pk("Id", "int")))
                .Expect(e => e.Diff.Empty()),
        ];

    /// <summary>
    /// 行に、親 parent(id)・子 child(id, parent_id) の DB 側の図と、親 id を参照する外部キー FK_child_parent（ON UPDATE は SET DEFAULT）の
    /// 追加の項目（選んだ・手で組む）を書く
    /// </summary>
    private static ExpectationRow SetDefaultAddRow(
        ExpectationRow row,
        bool childNullable,
        bool childKeyFlaggedNullable = false,
        bool childKeyOnlyInDiagram = false
    ) =>
        row.Live(s =>
            {
                s.Table("parent", S.Pk("id", "int"));

                // 主キーの列の印を見る行は、子の表を DB に置かない（子の列の NULL 許容は同期後の値で見るので、DB に在る列は DB の値が
                // 使われる。図の列の印が読まれるのは DB に無い表・列だけ）
                if (!childKeyFlaggedNullable)
                {
                    s.Table(
                        "child",
                        S.Pk("id", "int"),
                        childNullable
                            ? S.Nullable("parent_id", "int")
                            : S.NotNull("parent_id", "int")
                    );
                }
            })
            .Items(w =>
            {
                var parent = w.Live["parent"];
                Entity child;

                if (childKeyFlaggedNullable)
                {
                    // 図の子の表（DB に無い）では parent_id を主キーへ加え、NULL 許容の印を立てたままにする（図のファイルの手書きで
                    // だけ作れる形）
                    var flagged = S.Nullable("parent_id", "int");
                    flagged.IsPrimaryKey = true;
                    child = new Entity { TableName = "child" };
                    child.Columns.Add(S.Pk("id", "int"));
                    child.Columns.Add(flagged);
                }
                else
                {
                    child = w.Live["child"];

                    // 項目の子の表（図）でだけ parent_id を主キーへ加える（DB の列は主キーでないまま。主キーの変更の項目は作らない）
                    if (childKeyOnlyInDiagram)
                    {
                        child = child.Clone(preserveId: true);
                        child.Columns[1].IsPrimaryKey = true;
                    }
                }

                var addForeignKey = new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddForeignKey,
                    TableName = "child",
                    ColumnName = "parent_id",
                    ForeignKeyColumnPairs = [new("id", "parent_id")],
                    ForeignKeyName = "FK_child_parent",
                    ParentEntity = parent,
                    ChildEntity = child,
                    Relationship = new Relationship
                    {
                        SourceEntityId = parent.Id,
                        TargetEntityId = child.Id,
                        Type = RelationshipType.OneToMany,
                        ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                        ConstraintName = "FK_child_parent",
                        OnUpdate = ForeignKeyReferentialAction.SetDefault,
                    },
                    IsSelected = true,
                };

                // DB に無い子の表は、表の追加も選ぶ（選ばないと、外部キーの追加が前提の欠けで外れる）
                return childKeyFlaggedNullable
                    ?
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddTable,
                            TableName = "child",
                            Entity = child,
                            IsSelected = true,
                        },
                        addForeignKey,
                    ]
                    : [addForeignKey];
            });

    /// <summary>
    /// 説明を持つ 4 方言で、DB 側の表 t の説明（表・列 v とも <paramref name="live"/>）を、図の説明（<paramref name="target"/>）へ合わせる差分の行を書く
    /// </summary>
    private static ExpectationRow BlankDescriptionDiffRow(
        string id,
        string description,
        string? live,
        string? target
    ) =>
        Row(id, description, DescriptionDialects)
            .Live(s => DescriptionTable(s, live, live))
            .Target(s => DescriptionTable(s, target, target));

    /// <summary>
    /// 説明のない DB の表 t から、説明 <paramref name="description"/>（表・列 v とも）の図へ同期する計画の行を書く（選べる項目をすべて選ぶ）。
    /// 5 方言のうち MySQL だけが、BMP の外の文字を置き換えるので名指しする
    /// </summary>
    private static ExpectationRow DescriptionReplacedRow(
        string id,
        string description,
        string text,
        bool replacedOnMySql
    ) =>
        Row(id, description, AllDialects)
            .Live(s => DescriptionTable(s, string.Empty, string.Empty))
            .Target(s => DescriptionTable(s, text, text))
            .Select(SelectSelectable(_ => true))
            .Expect(e =>
            {
                foreach (var dialect in AllDialects)
                {
                    if (replacedOnMySql && dialect == DialectNames.MySql)
                    {
                        e.For(dialect, d => d.Warnings.Exact(DescriptionReplacedWarnings));
                    }
                    else
                    {
                        e.For(dialect, d => d.Warnings.Absent("DescriptionCharactersReplaced*"));
                    }
                }
            });

    /// <summary>表 t(id 主キー・v NULL 許容の int) を足し、表の説明と列 v の説明を指定する（null は説明なしの代入）</summary>
    private static void DescriptionTable(S s, string? tableDescription, string? columnDescription)
    {
        var table = s.Tbl("t", ("id", "int", true), ("v", "int", false));
        table.Description = tableDescription!;
        table.Columns[1].Description = columnDescription!;
    }

    /// <summary>方言について、差分の説明の項目（表・列）の数をちょうど <paramref name="count"/> 個と期待する</summary>
    private static void ExpectDescriptionItemCount(
        ExpectationBuilder e,
        string dialect,
        int count
    ) => e.For(dialect, d => d.Diff.Count("Set*Description *", count));

    /// <summary>
    /// SET DEFAULT の外部キーを付ける文の腕と子の列の DB の既定値の組の行（SQL Server だけ。3 回目の効果測定の PE3・台帳の項目 pe3）
    /// </summary>
    /// <param name="readd">
    /// 真なら、DB にある SET DEFAULT の外部キーの NULL 許容の子の列を NOT NULL にする（列の変更が外部キーを外して付け直す＝暗黙の付け直しの腕）。
    /// 偽なら、NOT NULL の子の列へ SET DEFAULT の外部キーを足す（選んだ追加の腕）
    /// </param>
    /// <param name="withDefault">子の列に DB の DEFAULT 制約があるか（あれば SQL Server は SET DEFAULT を受け、警告しない）</param>
    private static ExpectationRow SetDefaultArmRow(bool readd, bool withDefault)
    {
        var arm = readd ? "readd" : "selected-add";
        var shape = withDefault ? "with-default-does-not-warn" : "without-default-warns";
        var row = Row(
            $"plan-set-default-not-null-child-{arm}-{shape}",
            (
                readd
                    ? "同期: DB の SET DEFAULT の外部キーの子の列を NOT NULL にすると、列の変更が外部キーを付け直す"
                    : "同期: NOT NULL の子の列へ SET DEFAULT の外部キーを足す"
            )
                + (
                    withDefault
                        ? "が、子の列に DB の既定値があるので SQL Server も受けて警告しない"
                        : "。子の列に DB の既定値が無いので SQL Server は断り、計画が警告する"
                ),
            DialectNames.SqlServer
        );

        row = readd
            ? row.Live(s =>
                {
                    s.Table("parent", S.Pk("id", "int"));
                    s.Table("child", S.Pk("id", "int"), S.Nullable("parent_id", "int"));
                    s.Fk("parent", "child", "FK_child_parent", ("id", "parent_id")).OnDelete =
                        ForeignKeyReferentialAction.SetDefault;
                })
                .TargetFromLive(s => s.Col("child", "parent_id").IsNullable = false)
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
            : row.Live(s =>
                {
                    s.Table("parent", S.Pk("id", "int"));
                    s.Table("child", S.Pk("id", "int"), S.NotNull("parent_id", "int"));
                })
                .TargetFromLive(s =>
                    s.Fk("parent", "child", "FK_child_parent", ("id", "parent_id")).OnDelete =
                        ForeignKeyReferentialAction.SetDefault
                )
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AddForeignKey));

        if (withDefault)
        {
            // 網は取込の代わりに既定値の事実を計画の文脈へ渡し、実 DB は DDL の後（取込の前）に DEFAULT 制約を足す
            row = row.Context(w => new SyncPlanContext
            {
                LiveEntities = w.Live.Entities,
                LiveRelationships = w.Live.Relationships,
                ColumnDefaults = [new SchemaColumnDefault("child", "parent_id", "((0))")],
            });
        }

        return row.Expect(e =>
            {
                if (withDefault)
                {
                    e.Warnings.Absent("SetDefaultOnNotNullColumn*");
                }
                else
                {
                    e.Warnings.Count("SetDefaultOnNotNullColumn*", 1)
                        .Warnings.Single(SetDefaultWarning);
                }
            })
            .OnRealDatabase(r =>
            {
                if (withDefault)
                {
                    r.Setup(
                        "ALTER TABLE [child] ADD CONSTRAINT [DF_child_parent_id] DEFAULT 0 FOR [parent_id];"
                    );
                }

                r.Expect(e =>
                {
                    if (withDefault)
                    {
                        e.Outcome.Exact("committed");
                        e.After.Count("fk *", 1);
                    }
                    else
                    {
                        e.Outcome.Exact("failed: *");
                        e.Unchanged();
                    }
                });
            })
            .Tagged(
                RowTag
                    .Ledger("pe3")
                    .Except(
                        DialectNames.PostgreSql,
                        "SetDefaultRequiresColumnDefault が偽で、PostgreSQL は既定値の無い NOT NULL の列への SET DEFAULT を作る（警告の門の外）"
                    )
                    .Except(
                        DialectNames.Sqlite,
                        "SetDefaultRequiresColumnDefault が偽で、SQLite は既定値の無い NOT NULL の列への SET DEFAULT を作る（警告の門の外）"
                    )
                    .Except(
                        DialectNames.MySql,
                        "SupportsSetDefaultAction が偽で、MySQL は SET DEFAULT の外部キーを作らない（製品は実効値へ畳む）"
                    )
                    .Except(
                        DialectNames.Oracle,
                        "SupportsSetDefaultAction が偽で、Oracle は SET DEFAULT の外部キーを作らない（製品は実効値へ畳む）"
                    )
            );
    }
}
