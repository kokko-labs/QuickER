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
                        "同期: 図で主キーの子の列への SET DEFAULT の追加は、NULL 許容の印があっても NOT NULL の列と同じく SQL Server が警告する",
                        DialectNames.SqlServer
                    ),
                    childNullable: true,
                    childKeyFlaggedNullable: true
                )
                .Expect(e =>
                    e.Warnings.Count("SetDefaultOnNotNullColumn*", 1)
                        .Warnings.Single(SetDefaultWarning)
                ),
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
        bool childKeyFlaggedNullable = false
    ) =>
        row.Live(s =>
            {
                s.Table("parent", S.Pk("id", "int"));
                s.Table(
                    "child",
                    S.Pk("id", "int"),
                    childNullable ? S.Nullable("parent_id", "int") : S.NotNull("parent_id", "int")
                );
            })
            .Items(w =>
            {
                var parent = w.Live["parent"];
                var child = w.Live["child"];

                // 図の子の表では parent_id を主キーへ加え、NULL 許容の印を立てたままにする（図のファイルの手書きでだけ作れる形）
                if (childKeyFlaggedNullable)
                {
                    child = child.Clone(preserveId: true);
                    child.Columns[1].IsPrimaryKey = true;
                    child.Columns[1].IsNullable = true;
                }

                return
                [
                    new SchemaDiffItem
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
                    },
                ];
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
}
