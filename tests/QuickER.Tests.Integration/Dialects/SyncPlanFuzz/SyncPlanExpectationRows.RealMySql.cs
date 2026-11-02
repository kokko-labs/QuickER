using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using ProviderStrings = QuickER.Provider.Resources.Strings;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

internal static partial class SyncPlanExpectationRows
{
    /// <summary>MySQL の実 DB の段の行の数の下限（行を足したら・消したら合わせる）</summary>
    private const int RealMySqlRowCount = 50;

    /// <summary>実 DB の段：MySQL の同期（外部キー・主キー・一意制約・列の属性・説明・列順・参照アクション）</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealMySqlRows() =>
        [
            .. RealMySqlForeignKeyRows(),
            .. RealMySqlKeyRows(),
            .. RealMySqlColumnAttributeRows(),
            .. RealMySqlSchemaSyncRows(),
            .. RealMySqlDescriptionRows(),
        ];

    /// <summary>外部キーの両端の型・並び・共有の索引・SET NULL・型の食い違い</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealMySqlForeignKeyRows() =>
        [
            Row(
                    "realdb-mysql-fk-type-change-both-ends-keeps-fk",
                    "[Integration] MySQL 同期: 外部キーの両端を同じ型へ変える同期が外部キーを保って成功する",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                {
                    s.Col("md_parent", "id").DataType = "bigint";
                    s.Col("md_child", "parent_id").DataType = "bigint";
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(RealMySqlScenarios.ParentChildSql)
                        .Setup(RealMySqlScenarios.ParentChildRowsSql)
                        .Expect(e =>
                        {
                            e.Warnings.Empty();
                            e.Outcome.Exact("committed");
                            e.After.Single(
                                "col md_parent.id bigint *",
                                "col md_child.parent_id bigint *"
                            );
                            e.After.Count("fk *", 1);
                            e.After.Single("fk md_child fk_md *");
                        })
                ),
            Row(
                    "realdb-mysql-fk-string-length-change-one-end-keeps-fk",
                    "[Integration] MySQL 同期: 外部キーの片側の文字列の長さだけを変える同期は外部キーを保って成功する",
                    DialectNames.MySql
                )
                .TargetFromLive(s => s.Col("md_child", "parent_code").DataType = "varchar(100)")
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE `md_parent` (`code` varchar(50) NOT NULL, PRIMARY KEY (`code`));"
                                + "CREATE TABLE `md_child` (`id` int NOT NULL, `parent_code` varchar(50) NOT NULL, PRIMARY KEY (`id`), "
                                + "CONSTRAINT `fk_md` FOREIGN KEY (`parent_code`) REFERENCES `md_parent` (`code`));"
                        )
                        .Setup(
                            "INSERT INTO `md_parent` (`code`) VALUES ('a');"
                                + "INSERT INTO `md_child` (`id`, `parent_code`) VALUES (1, 'a');"
                        )
                        .Expect(e =>
                        {
                            e.Warnings.Empty();
                            e.Outcome.Exact("committed");
                            e.After.Single("col md_child.parent_code varchar(100) *");
                            e.After.Count("fk *", 1);
                            e.After.Single("fk md_child fk_md *");
                        })
                ),
            Row(
                    "realdb-mysql-fk-type-mismatch-one-end-warns-and-keeps-fk",
                    "[Integration] MySQL 同期: 外部キーの片側だけ型を変えると警告し、実行しても外部キーと型は元のまま",
                    DialectNames.MySql
                )
                .TargetFromLive(s => s.Col("md_parent", "id").DataType = "bigint")
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(RealMySqlScenarios.ParentChildSql)
                        .Setup(RealMySqlScenarios.ParentChildRowsSql)
                        .Expect(e =>
                        {
                            e.Warnings.Single(
                                "ForeignKeyColumnTypeMismatch(*)*",
                                "ForeignKeyColumnTypeMismatch(*): fk_md"
                            );
                            e.Outcome.Exact("failed: *");
                            e.After.Single("col md_parent.id int *");
                            e.After.Count("fk *", 1);
                            e.After.Single("fk md_child fk_md *");
                        })
                ),
            Row(
                    "realdb-mysql-fk-reorder-referenced-composite-pk-rebuilds-in-new-order",
                    "[Integration] MySQL 同期: 参照される複合主キーの並びだけを入れ替えると、外部キーを新しい並びで付け直して成功する",
                    DialectNames.MySql
                )
                .TargetFromLive(s => s.PrimaryKeyOrder("md_pair", "b", "a"))
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(RealMySqlScenarios.PairRefSql)
                        .Setup(
                            "INSERT INTO `md_pair` (`a`, `b`) VALUES (1, 2);"
                                + "INSERT INTO `md_ref` (`id`, `ra`, `rb`) VALUES (1, 1, 2);"
                        )
                        .Expect(e =>
                        {
                            e.Warnings.Empty();
                            e.Sections.Single(
                                "DropForeignKey: *",
                                "DropForeignKey: DropForeignKey md_ref fk=fk_md_ref"
                            );
                            e.Script.Contains(
                                "*FOREIGN KEY (`rb`, `ra`) REFERENCES `md_pair` (`b`, `a`)*"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Single(
                                "pk md_pair (b,a)",
                                "fk md_ref fk_md_ref (rb,ra) -> md_pair(b,a) *"
                            );
                            e.AfterDiff.Empty();
                        })
                ),
            Row(
                    "realdb-mysql-fk-two-on-same-columns-reorder-referenced-pk-rebuilds-both",
                    "[Integration] MySQL 同期: 同じ列の外部キー 2 本が参照する複合主キーの並びを入れ替えても、2 本とも付け直して成功する",
                    DialectNames.MySql
                )
                .TargetFromLive(s => s.PrimaryKeyOrder("md_pair", "b", "a"))
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE `md_pair` (`a` int NOT NULL, `b` int NOT NULL, PRIMARY KEY (`a`, `b`));"
                                + "CREATE TABLE `md_ref` (`id` int NOT NULL, `ra` int NOT NULL, `rb` int NOT NULL, PRIMARY KEY (`id`), "
                                + "UNIQUE KEY `uq_md_ref_rb` (`rb`));"
                                // 先に足した外部キー（fk_md_second）の名前で索引ができ、後から足した外部キー（fk_md_first）はその索引を使う
                                + "ALTER TABLE `md_ref` ADD CONSTRAINT `fk_md_second` FOREIGN KEY (`ra`, `rb`) REFERENCES `md_pair` (`a`, `b`) "
                                + "ON DELETE CASCADE;"
                                + "ALTER TABLE `md_ref` ADD CONSTRAINT `fk_md_first` FOREIGN KEY (`ra`, `rb`) REFERENCES `md_pair` (`a`, `b`);"
                        )
                        .Expect(e =>
                        {
                            e.Warnings.Empty();
                            e.Outcome.Exact("committed");
                            e.After.Single(
                                "fk md_ref fk_md_first (rb,ra) -> md_pair(b,a) *",
                                "fk md_ref fk_md_second (rb,ra) -> md_pair(b,a) *"
                            );
                            e.AfterDiff.Empty();
                        })
                ),
            Row(
                    "realdb-mysql-fk-alter-in-other-order-rebuilt-in-key-order",
                    "[Integration] MySQL 同期: 図の並びが参照先のキーと違う外部キーの作り直しは、キーの並びで付け直して成功する",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                {
                    var fk = s.Relationships.Single();
                    fk.ColumnPairs = [.. fk.ColumnPairs.AsEnumerable().Reverse()];
                    fk.OnDelete = ForeignKeyReferentialAction.Cascade;
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterForeignKey))
                .OnRealDatabase(r =>
                    r.LiveSql(RealMySqlScenarios.PairRefSql)
                        .Expect(e =>
                        {
                            e.Warnings.Empty();
                            e.Sections.Exact("DropForeignKey: *", "AddForeignKey: *");
                            e.Script.Contains(
                                "*FOREIGN KEY (`ra`, `rb`) REFERENCES `md_pair` (`a`, `b`)*"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Count("fk *", 1);
                            e.After.Single(
                                "fk md_ref fk_md_ref (ra,rb) -> md_pair(a,b) del=Cascade *"
                            );
                            e.AfterDiff.Empty();
                        })
                ),
            Row(
                    "realdb-mysql-fk-drop-keeps-index-used-by-other-fk",
                    "[Integration] MySQL 同期: 外部キーの削除は、ほかの外部キーが使う同じ名前の索引を残して成功する",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                    s.Relationships.RemoveAll(r => r.ConstraintName == "fk_second")
                )
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.DropForeignKey))
                .OnRealDatabase(r =>
                    r.LiveSql(RealMySqlScenarios.SharedIndexChildSql(withOtherColumn: false))
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.After.Count("fk *", 1);
                            e.After.Single("fk md_child fk_first *");
                        })
                ),
            Row(
                    "realdb-mysql-fk-alter-to-other-column",
                    "[Integration] MySQL 同期: 外部キーの子列を別の列へ変える作り直しが成功する",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                {
                    var fk = s.Relationships.Single();
                    fk.ColumnPairs =
                    [
                        new(fk.ColumnPairs[0].SourceColumnId, s.Col("md_child", "b_id").Id),
                    ];
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterForeignKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE `md_parent` (`id` int NOT NULL, PRIMARY KEY (`id`));"
                                + "CREATE TABLE `md_child` (`id` int NOT NULL, `a_id` int NOT NULL, `b_id` int NOT NULL, "
                                + "PRIMARY KEY (`id`), "
                                + "CONSTRAINT `fk_md` FOREIGN KEY (`a_id`) REFERENCES `md_parent` (`id`));"
                        )
                        .Setup(
                            "INSERT INTO `md_parent` (`id`) VALUES (1);"
                                + "INSERT INTO `md_child` (`id`, `a_id`, `b_id`) VALUES (1, 1, 1);"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.After.Count("fk *", 1);
                            e.After.Single("fk md_child fk_md (b_id) -> md_parent(id) *");
                        })
                ),
            MySqlSharedIndexAlterRow(renameTargetTaken: false),
            MySqlSharedIndexAlterRow(renameTargetTaken: true),
            Row(
                    "realdb-mysql-fk-drop-does-not-rename-index-to-other-column-fk",
                    "[Integration] MySQL 同期: 外部キーの削除は、別の列の外部キーの名前へ索引を付け替えずに外す",
                    DialectNames.MySql
                )
                .TargetFromLive(s => s.Relationships.RemoveAll(r => r.ConstraintName == "fk_a"))
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.DropForeignKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE `md_parent` (`id` int NOT NULL, PRIMARY KEY (`id`));"
                                + "CREATE TABLE `md_child` (`id` int NOT NULL, `col1` int NOT NULL, `col2` int NOT NULL, "
                                + "`col3` int NOT NULL, PRIMARY KEY (`id`), "
                                + "CONSTRAINT `fk_a` FOREIGN KEY (`col1`) REFERENCES `md_parent` (`id`), "
                                + "CONSTRAINT `fk_b` FOREIGN KEY (`col2`) REFERENCES `md_parent` (`id`));"
                        )
                        .Probe("indexes")
                        // 同じ名前の fk_a を別の列へ付け直せる（索引 fk_a が残っているとエラー 1061）
                        .Query(
                            "readd-fk-a",
                            "ALTER TABLE `md_child` ADD CONSTRAINT `fk_a` FOREIGN KEY (`col3`) REFERENCES `md_parent` (`id`);"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.Probe("indexes").Count("indexes md_child.* *", 2);
                            e.Probe("indexes")
                                .Single(
                                    "indexes md_child.PRIMARY first_column=id",
                                    "indexes md_child.fk_b first_column=col2"
                                );
                            e.Probe("readd-fk-a").Empty();
                        })
                ),
            Row(
                    "realdb-mysql-fk-alter-with-type-mismatch-excluded-and-changes-nothing",
                    "[Integration] MySQL 同期: 型が食い違う外部キーの作り直しは外して警告し、実行しても外部キーは元のまま",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                {
                    s.Col("md_parent", "id").DataType = "bigint";
                    s.Relationships.Single().OnDelete = ForeignKeyReferentialAction.Cascade;
                })
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(RealMySqlScenarios.ParentChildSql)
                        .Setup(RealMySqlScenarios.ParentChildRowsSql)
                        .Expect(e =>
                        {
                            e.Warnings.Single(
                                "ForeignKeyColumnTypeMismatch(*)*",
                                "ForeignKeyColumnTypeMismatch(*): fk_md"
                            );
                            e.Sections.Absent("DropForeignKey*", "AddForeignKey*");
                            e.Outcome.Exact("failed: *");
                            e.After.Single("col md_parent.id int *");
                            e.After.Count("fk *", 1);
                            e.After.Single("fk md_child fk_md * del=NoAction *");
                        })
                ),
            Row(
                    "realdb-mysql-fk-set-null-child-not-null-names-fk-and-changes-nothing",
                    "[Integration] MySQL 同期: SET NULL の子の列を NOT NULL にする変更は名指しし、どの外部キーも外さず何も変えない",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                {
                    s.Col("md_child", "parent_id").IsNullable = false;
                    s.Col("md_child", "other_id").DataType = "bigint";
                    s.Col("md_other", "id").DataType = "bigint";
                })
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE `md_parent` (`id` int NOT NULL, PRIMARY KEY (`id`));"
                                + "CREATE TABLE `md_other` (`id` int NOT NULL, PRIMARY KEY (`id`));"
                                + "CREATE TABLE `md_child` (`id` int NOT NULL, `parent_id` int NULL, `other_id` int NULL, "
                                + "PRIMARY KEY (`id`), "
                                + "CONSTRAINT `fk_parent` FOREIGN KEY (`parent_id`) REFERENCES `md_parent` (`id`) ON DELETE SET NULL, "
                                + "CONSTRAINT `fk_other` FOREIGN KEY (`other_id`) REFERENCES `md_other` (`id`));"
                        )
                        .Expect(e =>
                        {
                            e.Warnings.Single(
                                "SetNullOnNotNullColumn(*)*",
                                "SetNullOnNotNullColumn(md_child): fk_parent (parent_id)"
                            );
                            e.Sections.Absent("DropForeignKey*", "AddForeignKey*");
                            e.Outcome.Exact("failed: *");
                            e.After.Single(
                                "col md_child.parent_id * null*",
                                "col md_child.other_id int *"
                            );
                            e.After.Count("fk *", 2);
                            e.After.Single(
                                "fk md_child fk_parent * del=SetNull *",
                                "fk md_child fk_other *"
                            );
                        })
                ),
            Row(
                    "realdb-mysql-fk-same-column-one-mismatched-changes-nothing",
                    "[Integration] MySQL 同期: 同じ列の外部キー 2 本の片方だけ型が食い違うと、どの外部キーも外さず何も変えない",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                {
                    s.Col("md_parent", "id").DataType = "bigint";
                    s.Col("md_child", "parent_id").DataType = "bigint";
                    s.Col("md_item", "parent_id").DataType = "bigint";
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE `md_parent` (`id` int NOT NULL, PRIMARY KEY (`id`));"
                                + "CREATE TABLE `md_other` (`id` int NOT NULL, PRIMARY KEY (`id`));"
                                + "CREATE TABLE `md_child` (`id` int NOT NULL, `parent_id` int NOT NULL, PRIMARY KEY (`id`), "
                                + "CONSTRAINT `fk_parent` FOREIGN KEY (`parent_id`) REFERENCES `md_parent` (`id`), "
                                + "CONSTRAINT `fk_other` FOREIGN KEY (`parent_id`) REFERENCES `md_other` (`id`));"
                                + "CREATE TABLE `md_item` (`id` int NOT NULL, `parent_id` int NOT NULL, PRIMARY KEY (`id`), "
                                + "CONSTRAINT `fk_item` FOREIGN KEY (`parent_id`) REFERENCES `md_parent` (`id`));"
                        )
                        .Expect(e =>
                        {
                            e.Warnings.Single(
                                "ForeignKeyColumnTypeMismatch(*)*",
                                "ForeignKeyColumnTypeMismatch(*): fk_other"
                            );
                            e.Script.Absent("*DROP FOREIGN KEY*");
                            e.Outcome.Exact("failed: *");
                            e.After.Count("fk *", 3);
                            e.After.Single(
                                "fk md_child fk_parent *",
                                "fk md_child fk_other *",
                                "fk md_item fk_item *"
                            );
                            e.After.Single("col md_parent.id int *");
                        })
                ),
            Row(
                    "realdb-mysql-fk-composite-other-order-ddl-applies-no-diff",
                    "[Integration] MySQL: 列ペアの並びが参照先のキーと違う複合外部キーの DDL は適用でき、取り込み直しても差分が出ない",
                    DialectNames.MySql
                )
                .Live(RealScenarios.CompositeInOtherOrder)
                .Target(RealScenarios.CompositeInOtherOrder)
                .Select(items => RealScenarios.SelectKinds(items))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Before.Count("fk *", 1);
                        e.Diff.Empty();
                        e.Outcome.Exact("committed");
                    })
                ),
        ];

    /// <summary>
    /// 同じ列の外部キー 2 本の片方（MySQL が共有の索引に名前を付けた fk_second）を、同じ名前のまま別の列へ付け直す
    /// （<paramref name="renameTargetTaken"/> が真なら、付け替える先の名前 fk_first の索引を手で作っておく）
    /// </summary>
    private static ExpectationRow MySqlSharedIndexAlterRow(bool renameTargetTaken) =>
        Row(
                renameTargetTaken
                    ? "realdb-mysql-fk-sharing-index-alter-rename-target-taken"
                    : "realdb-mysql-fk-sharing-index-alter-to-other-column",
                renameTargetTaken
                    ? "[Integration] MySQL 同期: 共有の索引を付け替える先の名前の索引が既にあれば、付け替えずに付け直す"
                    : "[Integration] MySQL 同期: 同じ列の外部キー 2 本の片方を、同じ名前のまま別の列へ付け直せる",
                DialectNames.MySql
            )
            .TargetFromLive(s =>
            {
                var second = s.Relationships.Single(r => r.ConstraintName == "fk_second");
                second.ColumnPairs =
                [
                    new(second.ColumnPairs[0].SourceColumnId, s.Col("md_child", "other_id").Id),
                ];
            })
            .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterForeignKey))
            .OnRealDatabase(r =>
            {
                r.LiveSql(RealMySqlScenarios.SharedIndexChildSql(withOtherColumn: true));

                if (renameTargetTaken)
                {
                    r.Setup("ALTER TABLE `md_child` ADD INDEX `fk_first` (`other_id`);");
                }

                r.Expect(e =>
                {
                    e.Outcome.Exact("committed");
                    e.After.Single(
                        "fk md_child fk_first (parent_id) -> md_parent(id) *",
                        "fk md_child fk_second (other_id) -> md_other(id) *"
                    );

                    if (!renameTargetTaken)
                    {
                        e.AfterDiff.Empty();
                    }
                });
            });

    /// <summary>主キーの並べ替え・付け替え・一意制約の作り直しと削除・表と列の削除に伴う外部キー</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealMySqlKeyRows() =>
        [
            Row(
                    "realdb-mysql-pk-reorder-with-fk-on-leading-column",
                    "[Integration] MySQL 同期: 主キーの先頭に乗る外部キーを持つ表の主キーを並べ替えられる",
                    DialectNames.MySql
                )
                .TargetFromLive(s => s.PrimaryKeyOrder("md_link", "x", "p_id"))
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE `md_p` (`id` int NOT NULL, PRIMARY KEY (`id`));"
                                + "CREATE TABLE `md_link` (`p_id` int NOT NULL, `x` int NOT NULL, "
                                + "PRIMARY KEY (`p_id`, `x`), "
                                + "CONSTRAINT `fk_link` FOREIGN KEY (`p_id`) REFERENCES `md_p` (`id`));"
                        )
                        .Setup(
                            "INSERT INTO `md_p` (`id`) VALUES (1);"
                                + "INSERT INTO `md_link` (`p_id`, `x`) VALUES (1, 2);"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.After.Single("pk md_link (x,p_id)");
                            e.After.Count("fk *", 1);
                            e.After.Single("fk md_link fk_link *");
                        })
                ),
            Row(
                    "realdb-mysql-pk-move-auto-increment-removes-it",
                    "[Integration] MySQL 同期: AUTO_INCREMENT の主キーを付け替えられ、AUTO_INCREMENT は外れる",
                    DialectNames.MySql
                )
                .TargetFromLive(s => RealScenarios.MovePrimaryKeyToCode(s, "md_ai"))
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE `md_ai` (`id` int NOT NULL AUTO_INCREMENT COMMENT 'surrogate', "
                                + "`code` varchar(20) NOT NULL, PRIMARY KEY (`id`));"
                        )
                        .Setup("INSERT INTO `md_ai` (`code`) VALUES ('A');")
                        .Probe("columns")
                        .Expect(e =>
                        {
                            e.Script.Contains(
                                "*-- Note: AUTO_INCREMENT on an old primary key column is removed*"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Single("pk md_ai (code)");
                            // AUTO_INCREMENT だけが外れ、説明と NOT NULL は残る
                            e.Probe("columns").Single("columns md_ai.id.EXTRA *");
                            e.Probe("columns")
                                .Absent("columns md_ai.id.EXTRA value=*auto_increment*");
                            e.Probe("columns")
                                .Single(
                                    "columns md_ai.id.IS_NULLABLE value=NO",
                                    "columns md_ai.id.COLUMN_COMMENT value=surrogate"
                                );
                        })
                ),
            MySqlDropReferencedEndRow(SchemaDiffKind.DropTable),
            MySqlDropReferencedEndRow(SchemaDiffKind.DropColumn),
            Row(
                    "realdb-mysql-uq-columns-change-default-selection-changes-nothing",
                    "[Integration] MySQL 同期: 名前付き一意制約の列の変更は、既定では何もせず、選べば同じ名前で作り直す",
                    DialectNames.MySql
                )
                .TargetFromLive(RealMySqlScenarios.WidenUniqueToAB)
                .OnRealDatabase(r =>
                    r.LiveSql(RealMySqlScenarios.UniqueItemSql)
                        .Expect(e =>
                        {
                            e.Sections.Empty();
                            e.Rebuilds.Empty();
                            e.Reorders.Empty();
                            e.Outcome.Exact("committed");
                        })
                ),
            Row(
                    "realdb-mysql-uq-columns-change-selected-rebuilds-same-name",
                    "[Integration] MySQL 同期: 名前付き一意制約の列の変更は、既定では何もせず、選べば同じ名前で作り直す",
                    DialectNames.MySql
                )
                .TargetFromLive(RealMySqlScenarios.WidenUniqueToAB)
                .Select(items =>
                    RealScenarios.SelectKinds(items, SchemaDiffKind.AlterUniqueConstraint)
                )
                .OnRealDatabase(r =>
                    r.LiveSql(RealMySqlScenarios.UniqueItemSql)
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.After.Count("uq md_item *", 1);
                            e.After.Single("uq md_item uq_md (a,b)");
                        })
                ),
            Row(
                    "realdb-mysql-uq-drop-supporting-fk-keeps-fk",
                    "[Integration] MySQL 同期: 外部キーを支える一意制約を消しても外部キーを保って成功する",
                    DialectNames.MySql
                )
                .TargetFromLive(s => s["md_child"].UniqueConstraints.Clear())
                .Select(items =>
                    RealScenarios.SelectKinds(items, SchemaDiffKind.DropUniqueConstraint)
                )
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE `md_parent` (`id` int NOT NULL, PRIMARY KEY (`id`));"
                                + "CREATE TABLE `md_child` (`id` int NOT NULL, `parent_id` int NOT NULL, PRIMARY KEY (`id`), "
                                + "UNIQUE KEY `uq_md` (`parent_id`), "
                                + "CONSTRAINT `fk_md` FOREIGN KEY (`parent_id`) REFERENCES `md_parent` (`id`));"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.After.Absent("uq md_child *");
                            e.After.Count("fk *", 1);
                            e.After.Single("fk md_child fk_md *");
                        })
                ),
        ];

    /// <summary>参照される親の表だけ・外部キーの子列だけを消す（外部キーの削除は選ばない）</summary>
    private static ExpectationRow MySqlDropReferencedEndRow(SchemaDiffKind dropKind) =>
        Row(
                $"realdb-mysql-drop-referenced-end-without-fk-drop-{(dropKind == SchemaDiffKind.DropTable ? "table" : "column")}",
                "[Integration] MySQL 同期: 親の表・外部キーの列だけを消しても、端の消える外部キーを外して成功する",
                DialectNames.MySql
            )
            .TargetFromLive(s =>
            {
                s.Relationships.Clear();

                if (dropKind == SchemaDiffKind.DropTable)
                {
                    s.Entities.RemoveAll(e => e.TableName == "md_parent");
                }
                else
                {
                    s["md_child"].Columns.RemoveAll(c => c.Name == "parent_id");
                }
            })
            .Select(items => RealScenarios.SelectKinds(items, dropKind))
            .OnRealDatabase(r =>
                r.LiveSql(RealMySqlScenarios.ParentChildSql)
                    .Setup(RealMySqlScenarios.ParentChildRowsSql)
                    .Expect(e =>
                    {
                        e.Warnings.Empty();
                        e.Outcome.Exact("committed");
                        e.After.Absent("fk *");
                        e.After.Contains("table md_child");
                    })
            );

    /// <summary>
    /// 列の変更・説明の同期・列順の同期が、図に無い列の属性（information_schema.COLUMNS の 16 項目）を保つ
    /// </summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealMySqlColumnAttributeRows() =>
        [
            Row(
                    "realdb-mysql-column-description-sync-keeps-every-attribute",
                    "[Integration] B: 説明同期は COMMENT 以外の列属性を一切変えない",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                {
                    foreach (var column in s["attrs"].Columns)
                    {
                        column.Description = $"note for {column.Name}";
                    }
                })
                .Select(items =>
                    RealScenarios.SelectKinds(items, SchemaDiffKind.SetColumnDescription)
                )
                .OnRealDatabase(r =>
                    r.LiveSql(RealMySqlScenarios.AttrsTableSql)
                        .Probe("columns")
                        .Expect(e =>
                        {
                            RealMySqlScenarios.ExpectAttrsFixtureAttributes(e);
                            e.Outcome.Exact("committed");
                            // COMMENT だけが変わり、他は 1 項目も動かない
                            e.ProbeUnchanged("columns", "columns *.COLUMN_COMMENT *");

                            foreach (var column in RealMySqlScenarios.AttrsColumns)
                            {
                                e.Probe("columns")
                                    .Single(
                                        $"columns attrs.{column}.COLUMN_COMMENT value=note for {column}"
                                    );
                            }
                        })
                ),
            Row(
                    "realdb-mysql-column-reorder-keeps-every-attribute-and-comment",
                    "[Integration] B: 列順同期は位置以外の列属性と COMMENT を保つ",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                    RealMySqlScenarios.Reorder(s["attrs"], RealMySqlScenarios.AttrsReorderedColumns)
                )
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.ReorderColumns))
                .OnRealDatabase(r =>
                    r.LiveSql(RealMySqlScenarios.AttrsTableSql)
                        // 既存コメントを付けておき、列順同期が温存することを確かめる（' と \ を含める）
                        .Setup(
                            "ALTER TABLE `attrs` MODIFY COLUMN `ts` timestamp(6) NOT NULL "
                                + "DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) "
                                + "COMMENT 'stamp ''live'' \\\\ note';"
                        )
                        .Probe("columns")
                        .Expect(e =>
                        {
                            e.Reorders.Contains("*");
                            e.Outcome.Exact("committed");
                            e.ProbeUnchanged("columns", "columns *.ORDINAL_POSITION *");
                            RealMySqlScenarios.ExpectPositions(
                                e,
                                "attrs",
                                RealMySqlScenarios.AttrsReorderedColumns
                            );
                        })
                ),
            Row(
                    "realdb-mysql-column-description-sync-ignores-unselected-type-change",
                    "[Integration] B: 未選択の型変更は説明同期で適用されない",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                {
                    // 図では memo を varchar(200) NOT NULL にしてあるが、列の変更は選ばない
                    var memo = s.Col("t", "memo");
                    memo.DataType = "varchar(200)";
                    memo.IsNullable = false;
                    memo.Description = "説明だけ";
                })
                .Select(items =>
                    RealScenarios.SelectKinds(items, SchemaDiffKind.SetColumnDescription)
                )
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE `t` (`id` int NOT NULL, `memo` varchar(50) NULL DEFAULT 'keep', "
                                + "CONSTRAINT `PK_t` PRIMARY KEY (`id`));"
                        )
                        .Probe("columns")
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.Probe("columns")
                                .Single(
                                    "columns t.memo.COLUMN_TYPE value=varchar(50)",
                                    "columns t.memo.IS_NULLABLE value=YES",
                                    "columns t.memo.COLUMN_DEFAULT value=keep",
                                    "columns t.memo.COLUMN_COMMENT value=説明だけ"
                                );
                        })
                ),
            Row(
                    "realdb-mysql-column-description-round-trips-quotes-backslashes-newlines",
                    "[Integration] B: 説明の ' と \\ と改行がそのまま往復する",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                    s.Col("t", "memo").Description = RealMySqlScenarios.SpecialDescription
                )
                .Select(items =>
                    RealScenarios.SelectKinds(items, SchemaDiffKind.SetColumnDescription)
                )
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE `t` (`id` int NOT NULL, `memo` varchar(50) NULL DEFAULT 'keep');"
                        )
                        .Probe("columns")
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.Probe("columns")
                                .Single(
                                    "columns t.memo.COLUMN_COMMENT value="
                                        + RealMySqlScenarios.SpecialDescription,
                                    // 巻き添えで既定が消えていないこと
                                    "columns t.memo.COLUMN_DEFAULT value=keep"
                                );
                        })
                ),
            Row(
                    "realdb-mysql-column-reorder-keeps-generated-expression-with-string-literal",
                    "[Integration] B: 列順同期は文字列リテラル入りの生成列式を壊さない",
                    DialectNames.MySql
                )
                .TargetFromLive(s => RealMySqlScenarios.Reorder(s["gx"], ["g", "id", "s"]))
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.ReorderColumns))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            """
                            CREATE TABLE `gx` (
                              `id` int NOT NULL,
                              `s` varchar(20) NULL,
                              `g` varchar(60) GENERATED ALWAYS AS (concat(`s`, ' -- it''s \\ ok')) STORED
                                COMMENT 'generated'
                            );
                            """
                        )
                        .Setup("INSERT INTO `gx` (`id`, `s`) VALUES (1, 'x');")
                        .Probe("columns")
                        .Query("g", "SELECT `g` FROM `gx` WHERE `id` = 1;")
                        .Expect(e =>
                        {
                            e.Reorders.Contains("*");
                            e.Outcome.Exact("committed");
                            RealMySqlScenarios.ExpectPositions(e, "gx", ["g", "id", "s"]);
                            e.Probe("columns")
                                .Single(
                                    "columns gx.g.EXTRA value=STORED GENERATED",
                                    "columns gx.g.COLUMN_COMMENT value=generated"
                                );
                            // 式が生きていること＝生成値が元の定義どおりであること
                            e.Probe("g").Exact(@"x -- it's \ ok");
                        })
                ),
            Row(
                    "realdb-mysql-computed-column-type-change-from-diff-keeps-expression-and-kind",
                    "[Integration] B: 差分から選んだ生成列の型の変更は、式・種別・NULL 許容・索引を保って型だけを変える",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                {
                    foreach (var column in new[] { "gv", "gs", "gnn" })
                    {
                        s.Col("gc", column).DataType = "bigint";
                    }
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            """
                            CREATE TABLE `gc` (
                              `id` int NOT NULL,
                              `n` int NOT NULL,
                              `gv` int GENERATED ALWAYS AS (`n` + 1) VIRTUAL,
                              `gs` int GENERATED ALWAYS AS (`n` * 2) STORED,
                              `gnn` int GENERATED ALWAYS AS (`n` + 3) STORED NOT NULL,
                              CONSTRAINT `PK_gc` PRIMARY KEY (`id`),
                              KEY `ix_gc_gv` (`gv`)
                            );
                            """
                        )
                        .Setup("INSERT INTO `gc` (`id`, `n`) VALUES (1, 5);")
                        .Probe("columns")
                        .Query(
                            "values",
                            "SELECT CONCAT(`gv`, ',', `gs`, ',', `gnn`) FROM `gc` WHERE `id` = 1;"
                        )
                        .Expect(e =>
                        {
                            e.Diff.Count("AlterColumn *", 3);
                            // MySQL は生成列の型の変更を選べる
                            e.Diff.Single(
                                "AlterColumn gc.gv * able=1 *",
                                "AlterColumn gc.gs * able=1 *",
                                "AlterColumn gc.gnn * able=1 *"
                            );
                            // 計算列の項目は NULL 許容を DB の値のまま運ぶ
                            e.Diff.Single("AlterColumn gc.gnn * col=bigint notnull*");
                            // 生成列は既定値を持たないので J2 の警告も出ない
                            e.Warnings.Empty();
                            e.Outcome.Exact("committed");
                            e.Probe("columns")
                                .Single(
                                    "columns gc.gv.COLUMN_TYPE value=bigint",
                                    "columns gc.gs.COLUMN_TYPE value=bigint",
                                    "columns gc.gnn.COLUMN_TYPE value=bigint",
                                    "columns gc.gnn.IS_NULLABLE value=NO",
                                    // 索引は残る
                                    "columns gc.gv.COLUMN_KEY value=MUL"
                                );
                            e.ProbeUnchanged(
                                "columns",
                                [.. RealMySqlScenarios.TypeFieldPatterns("gc", "gv", "gs", "gnn")]
                            );
                            e.Probe("values").Exact("6,10,8");
                            e.AfterDiff.Empty();
                        })
                ),
            Row(
                    "realdb-mysql-computed-fk-column-type-change-rebuilds-fk",
                    "[Integration] B: 外部キーの子の STORED の生成列と親の主キーの型の変更は、外部キーを外して付け直し成功する",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                {
                    s.Col("gp", "id").DataType = "bigint";
                    s.Col("gk", "pid").DataType = "bigint";
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            """
                            CREATE TABLE `gp` (`id` int NOT NULL, CONSTRAINT `PK_gp` PRIMARY KEY (`id`));
                            CREATE TABLE `gk` (
                              `id` int NOT NULL,
                              `raw` int NOT NULL,
                              `pid` int GENERATED ALWAYS AS (`raw` + 0) STORED,
                              CONSTRAINT `PK_gk` PRIMARY KEY (`id`),
                              CONSTRAINT `FK_gk_gp` FOREIGN KEY (`pid`) REFERENCES `gp` (`id`)
                            );
                            """
                        )
                        .Setup(
                            "INSERT INTO `gp` (`id`) VALUES (1);"
                                + "INSERT INTO `gk` (`id`, `raw`) VALUES (10, 1);"
                        )
                        .Probe("columns", "referential")
                        .Query("pid", "SELECT `pid` FROM `gk` WHERE `id` = 10;")
                        .Expect(e =>
                        {
                            e.Diff.Absent("AlterColumn * able=0 *");
                            e.Warnings.Empty();
                            e.Sections.Contains("DropForeignKey: *fk=FK_gk_gp*");
                            e.Outcome.Exact("committed");
                            e.Probe("columns")
                                .Single(
                                    "columns gk.pid.COLUMN_TYPE value=bigint",
                                    "columns gp.id.COLUMN_TYPE value=bigint"
                                );
                            e.ProbeUnchanged(
                                "columns",
                                [
                                    .. RealMySqlScenarios.TypeFieldPatterns("gk", "pid"),
                                    .. RealMySqlScenarios.TypeFieldPatterns("gp", "id"),
                                ]
                            );
                            e.Probe("referential")
                                .Exact("referential gk.FK_gk_gp delete_rule=NO ACTION *");
                            e.Probe("pid").Exact("1");
                            e.AfterDiff.Empty();
                        })
                ),
            Row(
                    "realdb-mysql-generated-column-alter-keeps-expression",
                    "[Integration] B: 生成列の列の変更は式と種別を保って型だけを変える",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                {
                    s.Col("attrs", "gv").DataType = "bigint";
                    s.Col("attrs", "gs").DataType = "bigint";
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(RealMySqlScenarios.AttrsTableSql)
                        .Probe("columns")
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.Probe("columns")
                                .Single(
                                    "columns attrs.gv.COLUMN_TYPE value=bigint",
                                    "columns attrs.gs.COLUMN_TYPE value=bigint"
                                );
                            e.ProbeUnchanged(
                                "columns",
                                [.. RealMySqlScenarios.TypeFieldPatterns("attrs", "gv", "gs")]
                            );
                        })
                ),
            Row(
                    "realdb-mysql-column-type-kind-change-incompatible-default-warns-and-fails",
                    "[Integration] B: 合わない既定値のある列の型の種類の変更は、事前に警告され、実行はエラー 1067 で失敗する",
                    DialectNames.MySql
                )
                .TargetFromLive(s => s.Col("jd", "v").DataType = "int")
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE `jd` (`id` int NOT NULL, `v` varchar(20) NOT NULL DEFAULT 'abc', CONSTRAINT `PK_jd` PRIMARY KEY (`id`));"
                        )
                        .Probe("columns")
                        .Expect(e =>
                        {
                            e.Warnings.Single(
                                "DefaultOnColumnTypeKindChange(*)*",
                                "DefaultOnColumnTypeKindChange(*): v (*"
                            );
                            // エラー 1067
                            e.Outcome.Exact(
                                RealMySqlScenarios.StatementFailed("*Invalid default value*")
                            );
                            // 失敗した変更は列を変えない
                            e.Probe("columns")
                                .Single(
                                    "columns jd.v.COLUMN_TYPE value=varchar(20)",
                                    "columns jd.v.COLUMN_DEFAULT value=abc"
                                );
                        })
                ),
            Row(
                    "realdb-mysql-add-column-writes-description-only-through-selected-item",
                    "[Integration] B: 列の追加は説明を書かず、選んだ説明の項目だけが書く（ほかの列属性は変えない）",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                {
                    s["attrs"].Columns.Add(RealMySqlScenarios.Described("memo", "selected note"));
                    s["attrs"]
                        .Columns.Add(RealMySqlScenarios.Described("memo2", "unselected note"));
                    s.Col("attrs", "s").Description = "s note";
                })
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected =
                            item.Kind == SchemaDiffKind.AddColumn
                            || (
                                item.Kind == SchemaDiffKind.SetColumnDescription
                                && item.ColumnName is "memo" or "s"
                            );
                    }
                })
                .OnRealDatabase(r =>
                    r.LiveSql(RealMySqlScenarios.AttrsTableSql)
                        .Probe("columns")
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.Probe("columns")
                                .Single(
                                    "columns attrs.memo.COLUMN_COMMENT value=selected note",
                                    // 説明の項目を選んでいない列には説明が入らない
                                    "columns attrs.memo2.COLUMN_COMMENT value=",
                                    "columns attrs.s.COLUMN_COMMENT value=s note",
                                    "columns attrs.s.COLUMN_DEFAULT value=a'b",
                                    "columns attrs.s.COLLATION_NAME value=utf8mb4_bin"
                                );
                            e.ProbeUnchanged(
                                "columns",
                                "columns attrs.memo.*",
                                "columns attrs.memo2.*",
                                "columns *.COLUMN_COMMENT *"
                            );
                        })
                ),
        ];

    /// <summary>スキーマ同期の往復（表・列・外部キーの追加と削除・列順・主キー・一意制約・複合外部キー）</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealMySqlSchemaSyncRows() =>
        [
            Row(
                    "realdb-mysql-schema-sync-add-tables-fk-and-descriptions-from-empty",
                    "[Integration] C: スキーマ同期が Add/Alter/Drop/FK まで実 DB に適用される",
                    DialectNames.MySql
                )
                .Live(_ => { })
                .Target(s => RealMySqlScenarios.SchemaSyncParentChild(s, stage: 0))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Diff.Contains(
                            "AddTable parent *",
                            "AddTable child *",
                            "AddForeignKey *",
                            "SetTableDescription parent *newdesc=\"親テーブルの説明\"*"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Count("table *", 2);
                        e.After.Single(
                            "table parent",
                            "table child",
                            "desc parent \"親テーブルの説明\"",
                            "desc parent.name \"名称\""
                        );
                        e.After.Count("fk *", 1);
                        e.After.Single("fk child FK_child_parent * del=Cascade *");
                    })
                ),
            Row(
                    "realdb-mysql-schema-sync-add-alter-drop-columns",
                    "[Integration] C: スキーマ同期が Add/Alter/Drop/FK まで実 DB に適用される",
                    DialectNames.MySql
                )
                .Live(s => RealMySqlScenarios.SchemaSyncParentChild(s, stage: 0))
                .TargetFromLive(s =>
                {
                    // 子は図を組み直す（型変更＋NULL→NOT NULL・列の追加・列の削除）。外部キーは維持
                    s.Relationships.Clear();
                    s.Entities.RemoveAll(e => e.TableName == "child");
                    s.Table(
                        "child",
                        ExpectationSchema.Pk("id", "int"),
                        ExpectationSchema.Nullable("parent_id", "int"),
                        ExpectationSchema.NotNull("to_alter", "varchar(100)"),
                        ExpectationSchema.Nullable("added", "int")
                    );
                    s.Fk(
                        "parent",
                        "child",
                        "FK_child_parent",
                        ForeignKeyReferentialAction.Cascade,
                        ("id", "parent_id")
                    );
                })
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Diff.Contains(
                            "AddColumn child.added *",
                            "AlterColumn child.to_alter *",
                            "DropColumn child.to_drop *"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Contains("col child.added *");
                        e.After.Absent("col child.to_drop *");
                        e.After.Single("col child.to_alter varchar(100) notnull*");
                    })
                ),
            Row(
                    "realdb-mysql-schema-sync-drop-named-fk",
                    "[Integration] C: スキーマ同期が Add/Alter/Drop/FK まで実 DB に適用される",
                    DialectNames.MySql
                )
                .Live(s => RealMySqlScenarios.SchemaSyncParentChild(s, stage: 1))
                .TargetFromLive(s => s.Relationships.Clear())
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.DropForeignKey))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Diff.Single(
                            "DropForeignKey *",
                            "DropForeignKey child fk=FK_child_parent *"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Absent("fk *");
                    })
                ),
            Row(
                    "realdb-mysql-schema-sync-drop-table",
                    "[Integration] C: スキーマ同期が Add/Alter/Drop/FK まで実 DB に適用される",
                    DialectNames.MySql
                )
                .Live(s => RealMySqlScenarios.SchemaSyncParentChild(s, stage: 2))
                .TargetFromLive(s => s.Entities.RemoveAll(e => e.TableName == "parent"))
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.DropTable))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Diff.Single("DropTable parent *");
                        e.Outcome.Exact("committed");
                        e.After.Count("table *", 1);
                        e.After.Single("table child");
                    })
                ),
            Row(
                    "realdb-mysql-column-reorder-with-insertion-applies",
                    "[Integration] C: 列順変更（並び替え＋真ん中への列追加）が実 DB に反映される",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                {
                    var item = s["item"];
                    item.Columns.Insert(1, ExpectationSchema.Nullable("new_col", "int"));
                    RealMySqlScenarios.Reorder(item, ["id", "new_col", "b", "a"]);
                })
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE `item` (`id` int NOT NULL, `a` int NULL, `b` int NULL, "
                                + "CONSTRAINT `PK_item` PRIMARY KEY (`id`));"
                        )
                        .Setup("INSERT INTO `item` (`id`, `a`, `b`) VALUES (1, 10, 20);")
                        .Probe("columns")
                        .Query("row", "SELECT `a`, `b`, `new_col` FROM `item` WHERE `id` = 1;")
                        .Expect(e =>
                        {
                            e.Diff.Contains("ReorderColumns item *");
                            e.Outcome.Exact("committed");
                            // ORDINAL_POSITION で列順が id, new_col, b, a に反映される
                            RealMySqlScenarios.ExpectPositions(
                                e,
                                "item",
                                ["id", "new_col", "b", "a"]
                            );
                            // データは温存される（a=10, b=20・追加列は NULL）
                            e.Probe("row").Exact("10 b=20 new_col=-");
                        })
                ),
            Row(
                    "realdb-mysql-pk-move-to-code-keeps-rows",
                    "[Integration] C: 主キーを id から code へ付け替えてもデータが温存される",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                {
                    s.Col("pk_item", "id").IsPrimaryKey = false;
                    s.Col("pk_item", "code").IsPrimaryKey = true;
                })
                .Select(items => RealScenarios.AlsoSelect(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE `pk_item` (`code` varchar(20) NOT NULL, `id` int NOT NULL, "
                                + "CONSTRAINT `PK_pk_item` PRIMARY KEY (`id`));"
                        )
                        .Setup(
                            "INSERT INTO `pk_item` (`code`, `id`) VALUES ('A-001', 1), ('A-002', 2);"
                        )
                        .Query("rows", "SELECT `code`, `id` FROM `pk_item` ORDER BY `id`;")
                        .Expect(e =>
                        {
                            e.Diff.Single("AlterPrimaryKey pk_item *");
                            e.Outcome.Exact("committed");
                            e.After.Single("pk pk_item (code)", "col pk_item.code * pk*");
                            e.After.Absent("col pk_item.id * pk*");
                            e.Probe("rows").Exact("A-001 id=1", "A-002 id=2");
                        })
                ),
            Row(
                    "realdb-mysql-pk-change-breaking-referenced-fk-partially-applies",
                    "[Integration] C: AlterPrimaryKey: 被参照列が候補キーでなくなる変更は失敗し部分適用で残る",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                {
                    // 親の主キーを id から code へ移す（子の外部キーは id を参照したまま維持）
                    s.Col("pk_parent", "id").IsPrimaryKey = false;
                    s.Col("pk_parent", "code").IsPrimaryKey = true;
                })
                .Select(items => RealScenarios.AlsoSelect(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE `pk_parent` (`id` int NOT NULL, `code` varchar(20) NOT NULL, "
                                + "CONSTRAINT `PK_pk_parent` PRIMARY KEY (`id`));"
                                + "CREATE TABLE `pk_child` (`id` int NOT NULL, `parent_id` int NULL, "
                                + "CONSTRAINT `PK_pk_child` PRIMARY KEY (`id`), "
                                + "CONSTRAINT `FK_pk_child_pk_parent` FOREIGN KEY (`parent_id`) "
                                + "REFERENCES `pk_parent` (`id`));"
                        )
                        .Setup(
                            "INSERT INTO `pk_parent` (`id`, `code`) VALUES (1, 'P-1');"
                                + "INSERT INTO `pk_child` (`id`, `parent_id`) VALUES (1, 1);"
                        )
                        .Expect(e =>
                        {
                            e.Diff.Single("AlterPrimaryKey pk_parent *");
                            e.Warnings.Contains("ForeignKeyRebuildMayLoseCandidateKey(*)*");
                            // 再 ADD できない外部キーがあるため失敗し、部分適用があり得る旨の説明が付く
                            e.Outcome.Exact(RealMySqlScenarios.StatementFailed("*"));
                            // MySQL の DDL は暗黙コミット＝主キーの付け替えは適用済みのまま残り、外した外部キーは戻らない
                            e.After.Single("pk pk_parent (code)");
                            e.After.Absent("fk *");
                        })
                ),
            Row(
                    "realdb-mysql-uq-add",
                    "[Integration] C: 一意制約の追加・削除が実 DB へ反映される",
                    DialectNames.MySql
                )
                .TargetFromLive(s => s.Unique("uq_item", null, "code"))
                .OnRealDatabase(r =>
                    r.LiveSql(RealMySqlScenarios.UniqueTargetTableSql(withUnique: false))
                        .Expect(e =>
                        {
                            // 一意制約の追加は既定で選択される（制約を増やすだけで既存定義を壊さないため）
                            e.Diff.Single(
                                "AddUniqueConstraint uq_item *",
                                "AddUniqueConstraint uq_item * sel=1 *"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Count("uq uq_item *", 1);
                            e.After.Single("uq uq_item * (code)");
                        })
                ),
            Row(
                    "realdb-mysql-uq-drop",
                    "[Integration] C: 一意制約の追加・削除が実 DB へ反映される",
                    DialectNames.MySql
                )
                .TargetFromLive(s => s["uq_item"].UniqueConstraints.Clear())
                .Select(items =>
                    RealScenarios.AlsoSelect(items, SchemaDiffKind.DropUniqueConstraint)
                )
                .OnRealDatabase(r =>
                    r.LiveSql(RealMySqlScenarios.UniqueTargetTableSql(withUnique: true))
                        .Expect(e =>
                        {
                            e.Before.Single("uq uq_item UQ_uq_item_code (code)");
                            // 削除は破壊的のため既定では未選択
                            e.Diff.Single(
                                "DropUniqueConstraint uq_item *",
                                "DropUniqueConstraint uq_item * sel=0 *"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Absent("uq *");
                        })
                ),
            Row(
                    "realdb-mysql-fk-composite-add-keeps-pairs",
                    "[Integration] C: 複合外部キーの追加・削除が実 DB へ反映される",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                    s.Fk(
                        "cfk_parent",
                        "cfk_child",
                        "FK_cfk_child_cfk_parent",
                        ("a", "a_ref"),
                        ("b", "b_ref")
                    )
                )
                .OnRealDatabase(r =>
                    r.LiveSql(RealMySqlScenarios.CompositeParentChildSql(withForeignKey: false))
                        .Probe("fk-columns")
                        .Expect(e =>
                        {
                            e.Diff.Single(
                                "AddForeignKey *",
                                "AddForeignKey * pairs=(a>a_ref,b>b_ref) *"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Count("fk *", 1);
                            e.After.Single(
                                "fk cfk_child FK_cfk_child_cfk_parent (a_ref,b_ref) -> cfk_parent(a,b) *"
                            );
                            // 実 DB 側でも全構成列が FK に載っている（単列 FK へ縮んでいない）
                            e.Probe("fk-columns")
                                .Exact(RealMySqlScenarios.CompositeForeignKeyColumns);
                        })
                ),
            Row(
                    "realdb-mysql-fk-composite-drop",
                    "[Integration] C: 複合外部キーの追加・削除が実 DB へ反映される",
                    DialectNames.MySql
                )
                .TargetFromLive(s => s.Relationships.Clear())
                .Select(items => RealScenarios.AlsoSelect(items, SchemaDiffKind.DropForeignKey))
                .OnRealDatabase(r =>
                    r.LiveSql(RealMySqlScenarios.CompositeParentChildSql(withForeignKey: true))
                        .Expect(e =>
                        {
                            e.Diff.Single("DropForeignKey *");
                            e.Outcome.Exact("committed");
                            e.After.Absent("fk *");
                        })
                ),
            Row(
                    "realdb-mysql-fk-composite-survives-pk-change",
                    "[Integration] C: 複合主キー変更に伴う複合外部キーの張り直しで構成列が失われない",
                    DialectNames.MySql
                )
                .TargetFromLive(s => s.Col("cfk_parent", "code").IsPrimaryKey = false)
                .Select(items => RealScenarios.AlsoSelect(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE `cfk_parent` (`id` int NOT NULL, `code` int NOT NULL, "
                                + "`a` int NOT NULL, `b` int NOT NULL, "
                                + "CONSTRAINT `PK_cfk_parent` PRIMARY KEY (`id`, `code`), "
                                + "CONSTRAINT `UQ_cfk_parent_a_b` UNIQUE (`a`, `b`));"
                                + "CREATE TABLE `cfk_child` (`id` int NOT NULL, `a_ref` int NOT NULL, "
                                + "`b_ref` int NOT NULL, CONSTRAINT `PK_cfk_child` PRIMARY KEY (`id`), "
                                + "CONSTRAINT `FK_cfk_child_cfk_parent` FOREIGN KEY (`a_ref`, `b_ref`) "
                                + "REFERENCES `cfk_parent` (`a`, `b`));"
                        )
                        .Probe("fk-columns")
                        .Expect(e =>
                        {
                            // 被参照列 (a, b) は同期後も一意制約として残るため、候補キー喪失の警告は出ない
                            e.Warnings.Empty();
                            e.Outcome.Exact("committed");
                            e.After.Single("pk cfk_parent (id)");
                            e.After.Count("fk *", 1);
                            e.After.Single(
                                "fk cfk_child FK_cfk_child_cfk_parent (a_ref,b_ref) -> cfk_parent(a,b) *"
                            );
                            // 実 DB のカタログでも 2 列とも残っている（プレフィックス FK へ縮んでいない）
                            e.Probe("fk-columns")
                                .Exact(RealMySqlScenarios.CompositeForeignKeyColumns);
                        })
                ),
        ];

    /// <summary>説明（表と列の COMMENT）の設定・更新・削除と、列の変更での温存</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealMySqlDescriptionRows() =>
        [
            MySqlDescriptionRoundTripRow(stage: 0),
            MySqlDescriptionRoundTripRow(stage: 1),
            MySqlDescriptionRoundTripRow(stage: 2),
            Row(
                    "realdb-mysql-column-alter-keeps-existing-comment",
                    "[Integration] B: AlterColumn（MODIFY）で既存コメントが消えない",
                    DialectNames.MySql
                )
                .TargetFromLive(s => s.Col("t", "memo").DataType = "varchar(100)")
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE `t` (`id` int NOT NULL, "
                                + "`memo` varchar(50) NULL COMMENT '既存コメント', "
                                + "CONSTRAINT `PK_t` PRIMARY KEY (`id`));"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.After.Single(
                                "col t.memo varchar(100) null",
                                "desc t.memo \"既存コメント\""
                            );
                        })
                ),
            Row(
                    "realdb-mysql-column-type-only-keeps-live-comment",
                    "[Integration] MySQL 同期: 列の型だけを変える同期は DB の説明を保つ",
                    DialectNames.MySql
                )
                .TargetFromLive(s =>
                {
                    var name = s.Col("md_cmt", "name");
                    name.DataType = "varchar(100)";
                    name.Description = string.Empty;
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE `md_cmt` (`id` int NOT NULL, `name` varchar(50) NOT NULL COMMENT 'customer name', "
                                + "PRIMARY KEY (`id`));"
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.After.Single(
                                "col md_cmt.name varchar(100) *",
                                "desc md_cmt.name \"customer name\""
                            );
                        })
                ),
            Row(
                    "realdb-mysql-set-default-is-omitted-and-leaves-no-fk-diff",
                    "[Integration] MySQL: SET DEFAULT は効かないので句を省き、適用後に外部キーの差分が残らない",
                    DialectNames.MySql
                )
                .Live(RealMySqlScenarios.SetDefaultParentChild)
                .TargetFromLive(s =>
                {
                    // 宣言を持つ既存の外部キー（fk_ra_raw）は NO ACTION と、図の外部キー（fk_ra）は SET DEFAULT と比べる
                    var raw = s.Relationships.Single(r => r.ConstraintName == "fk_ra_raw");
                    raw.OnDelete = ForeignKeyReferentialAction.NoAction;
                    raw.OnUpdate = ForeignKeyReferentialAction.NoAction;
                    var drawn = s.Relationships.Single(r => r.ConstraintName == "fk_ra");
                    drawn.OnDelete = ForeignKeyReferentialAction.SetDefault;
                    drawn.OnUpdate = ForeignKeyReferentialAction.SetDefault;
                })
                .Select(items => RealScenarios.SelectKinds(items))
                .OnRealDatabase(r =>
                    // 網の図には生の SQL で作る ra_raw_* の表が無い
                    r.RealOnly()
                        .Setup(
                            """
                            CREATE TABLE ra_raw_parent (id int NOT NULL, PRIMARY KEY (id));
                            CREATE TABLE ra_raw_child (id int NOT NULL, parent_id int NULL DEFAULT 0, PRIMARY KEY (id),
                                CONSTRAINT fk_ra_raw FOREIGN KEY (parent_id) REFERENCES ra_raw_parent (id)
                                ON DELETE SET DEFAULT ON UPDATE SET DEFAULT);
                            INSERT INTO ra_raw_parent VALUES (1);
                            INSERT INTO ra_raw_child VALUES (1, 1);
                            """
                        )
                        // 前提: SET DEFAULT を宣言した外部キーでも、親を消すと外部キーエラーになる（NO ACTION と同じ）
                        .Query("delete-parent", "DELETE FROM ra_raw_parent;")
                        .Expect(e =>
                        {
                            // DB は宣言を保存する
                            e.Before.Single("fk ra_raw_child fk_ra_raw * del=SetDefault *");
                            e.Diff.Absent(
                                "AddForeignKey *",
                                "DropForeignKey *",
                                "AlterForeignKey *"
                            );
                            e.Outcome.Exact("committed");
                            e.Probe("delete-parent").Exact("error MySqlException: *");
                        })
                ),
        ];

    /// <summary>
    /// 表と列の説明の設定（<paramref name="stage"/> 0）・更新（1）・削除（2）。DB の前提は前の段の同期の後と同じ説明を持つ表
    /// </summary>
    private static ExpectationRow MySqlDescriptionRoundTripRow(int stage) =>
        Row(
                stage switch
                {
                    0 => "realdb-mysql-description-set",
                    1 => "realdb-mysql-description-update",
                    _ => "realdb-mysql-description-delete",
                },
                "[Integration] B: COMMENT 同期で説明が設定→更新→削除まで往復する",
                DialectNames.MySql
            )
            .TargetFromLive(s =>
            {
                var (table, column) = RealMySqlScenarios.ItemsDescriptions(stage + 1);
                s["items"].Description = table;
                s.Col("items", "name").Description = column;
            })
            .Select(items =>
                RealScenarios.SelectKinds(
                    items,
                    SchemaDiffKind.SetTableDescription,
                    SchemaDiffKind.SetColumnDescription
                )
            )
            .OnRealDatabase(r =>
                r.LiveSql(RealMySqlScenarios.ItemsTableSql(stage))
                    .Expect(e =>
                    {
                        var (table, column) = RealMySqlScenarios.ItemsDescriptions(stage + 1);
                        e.Outcome.Exact("committed");

                        if (stage < 2)
                        {
                            e.After.Single(
                                $"desc items \"{table}\"",
                                $"desc items.name \"{column}\"",
                                // 列の説明の MODIFY による再指定で型・NULL 制約が保持されていること
                                "col items.name varchar(50) null"
                            );
                        }
                        else
                        {
                            e.After.Absent("desc *");
                        }
                    })
            );
}

/// <summary>MySQL の実 DB の段の図・SQL・選び方の部品</summary>
internal static class RealMySqlScenarios
{
    /// <summary>親 md_parent(id int) と、外部キー fk_md で参照する子 md_child(id, parent_id int)</summary>
    public const string ParentChildSql =
        "CREATE TABLE `md_parent` (`id` int NOT NULL, PRIMARY KEY (`id`));"
        + "CREATE TABLE `md_child` (`id` int NOT NULL, `parent_id` int NOT NULL, PRIMARY KEY (`id`), "
        + "CONSTRAINT `fk_md` FOREIGN KEY (`parent_id`) REFERENCES `md_parent` (`id`));";

    /// <summary><see cref="ParentChildSql"/> の親子に 1 行ずつ入れる（親 1・子 1 → 1）</summary>
    public const string ParentChildRowsSql =
        "INSERT INTO `md_parent` (`id`) VALUES (1);"
        + "INSERT INTO `md_child` (`id`, `parent_id`) VALUES (1, 1);";

    /// <summary>複合主キー md_pair(a, b) を md_ref(id, ra, rb) の外部キー fk_md_ref が (a, b) の並びで参照する</summary>
    public const string PairRefSql =
        "CREATE TABLE `md_pair` (`a` int NOT NULL, `b` int NOT NULL, PRIMARY KEY (`a`, `b`));"
        + "CREATE TABLE `md_ref` (`id` int NOT NULL, `ra` int NOT NULL, `rb` int NOT NULL, PRIMARY KEY (`id`), "
        + "CONSTRAINT `fk_md_ref` FOREIGN KEY (`ra`, `rb`) REFERENCES `md_pair` (`a`, `b`));";

    /// <summary>
    /// md_parent(id)・md_other(id) と、同じ列 parent_id に外部キー fk_first（→ md_parent）・fk_second（→ md_other）を張った
    /// md_child（<paramref name="withOtherColumn"/> が真なら other_id 列も）。MySQL は 2 本に 1 つの自動索引を共有させ、
    /// その名前を後に張った fk_second へ付け替える
    /// </summary>
    public static string SharedIndexChildSql(bool withOtherColumn) =>
        "CREATE TABLE `md_parent` (`id` int NOT NULL, PRIMARY KEY (`id`));"
        + "CREATE TABLE `md_other` (`id` int NOT NULL, PRIMARY KEY (`id`));"
        + "CREATE TABLE `md_child` (`id` int NOT NULL, `parent_id` int NOT NULL, "
        + (withOtherColumn ? "`other_id` int NOT NULL, " : string.Empty)
        + "PRIMARY KEY (`id`), "
        + "CONSTRAINT `fk_first` FOREIGN KEY (`parent_id`) REFERENCES `md_parent` (`id`));"
        + "ALTER TABLE `md_child` ADD CONSTRAINT `fk_second` FOREIGN KEY (`parent_id`) REFERENCES `md_other` (`id`);";

    /// <summary>md_item(id, a, b) と、a だけの名前付き一意制約 uq_md</summary>
    public const string UniqueItemSql =
        "CREATE TABLE `md_item` (`id` int NOT NULL, `a` int NOT NULL, `b` int NOT NULL, "
        + "PRIMARY KEY (`id`), UNIQUE KEY `uq_md` (`a`));";

    /// <summary>md_item の一意制約 uq_md の列を (a, b) へ広げる</summary>
    public static void WidenUniqueToAB(ExpectationSchema s) =>
        s["md_item"].UniqueConstraints.Single().ColumnIds = [
            s.Col("md_item", "a").Id,
            s.Col("md_item", "b").Id,
        ];

    /// <summary>
    /// uq_item(id, code)（<paramref name="withUnique"/> が真なら、同期が付けた名前 UQ_uq_item_code の code の一意制約も）
    /// </summary>
    public static string UniqueTargetTableSql(bool withUnique) =>
        "CREATE TABLE `uq_item` (`id` int NOT NULL, `code` varchar(20) NOT NULL, "
        + "CONSTRAINT `PK_uq_item` PRIMARY KEY (`id`)"
        + (withUnique ? ", CONSTRAINT `UQ_uq_item_code` UNIQUE (`code`)" : string.Empty)
        + ");";

    /// <summary>
    /// 複合主キー cfk_parent(a, b) と cfk_child(id, a_ref, b_ref)（<paramref name="withForeignKey"/> が真なら
    /// FK_cfk_child_cfk_parent (a_ref, b_ref) → (a, b) も）
    /// </summary>
    public static string CompositeParentChildSql(bool withForeignKey) =>
        "CREATE TABLE `cfk_parent` (`a` int NOT NULL, `b` int NOT NULL, "
        + "CONSTRAINT `PK_cfk_parent` PRIMARY KEY (`a`, `b`));"
        + "CREATE TABLE `cfk_child` (`id` int NOT NULL, `a_ref` int NOT NULL, "
        + "`b_ref` int NOT NULL, CONSTRAINT `PK_cfk_child` PRIMARY KEY (`id`)"
        + (
            withForeignKey
                ? ", CONSTRAINT `FK_cfk_child_cfk_parent` FOREIGN KEY (`a_ref`, `b_ref`) REFERENCES `cfk_parent` (`a`, `b`)"
                : string.Empty
        )
        + ");";

    /// <summary>探針 <c>fk-columns</c> の、FK_cfk_child_cfk_parent の構成列 (a_ref → a)・(b_ref → b) の行</summary>
    public static string[] CompositeForeignKeyColumns =>
        [
            "fk-columns cfk_child.FK_cfk_child_cfk_parent.1 column=a_ref referenced=a",
            "fk-columns cfk_child.FK_cfk_child_cfk_parent.2 column=b_ref referenced=b",
        ];

    /// <summary>列属性を一通り備えた検証用の表 attrs（固定文は英語が正本）</summary>
    /// <remarks>
    /// 生成列の式に文字列リテラルを含めないのは、MySQL が再宣言時に文字リテラルの文字集合導入子を
    /// <c>_latin1</c> → <c>_utf8mb4</c> へ正規化し（<c>SHOW CREATE TABLE</c> は元から後者を表示する）、
    /// 意味は同じでもカタログ上の文字列が動くため。導入子つきの式は行
    /// realdb-mysql-column-reorder-keeps-generated-expression-with-string-literal が別途押さえる。
    /// </remarks>
    public const string AttrsTableSql = """
        CREATE TABLE `attrs` (
          `id` int NOT NULL AUTO_INCREMENT,
          `s` varchar(20) COLLATE utf8mb4_bin DEFAULT 'a''b',
          `n` int NOT NULL DEFAULT -1,
          `d` decimal(5,2) DEFAULT 0.5,
          `ts` timestamp(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
          `u` char(36) DEFAULT (uuid()),
          `b` bit(1) DEFAULT b'1',
          `vb` varbinary(20) DEFAULT 'ab',
          `b4` binary(4) DEFAULT 'ab',
          `e` enum('x','y') DEFAULT 'y',
          `se` set('a','b') DEFAULT 'a',
          `iz` int unsigned zerofill DEFAULT 7,
          `cs` varchar(10) CHARACTER SET latin1 DEFAULT 'x',
          `js` json DEFAULT (json_array()),
          `gv` int GENERATED ALWAYS AS (`n` + 1) VIRTUAL,
          `gs` int GENERATED ALWAYS AS (`n` * 2) STORED,
          `inv` int DEFAULT 7 INVISIBLE,
          `ivu` timestamp(3) NULL DEFAULT NULL ON UPDATE CURRENT_TIMESTAMP(3) INVISIBLE,
          `geo` geometry NOT NULL SRID 4326,
          `nl` varchar(10) NULL,
          `nn` varchar(10) NOT NULL,
          PRIMARY KEY (`id`)
        );
        """;

    /// <summary><see cref="AttrsTableSql"/> の列（宣言の並び）</summary>
    public static string[] AttrsColumns =>
        [
            "id",
            "s",
            "n",
            "d",
            "ts",
            "u",
            "b",
            "vb",
            "b4",
            "e",
            "se",
            "iz",
            "cs",
            "js",
            "gv",
            "gs",
            "inv",
            "ivu",
            "geo",
            "nl",
            "nn",
        ];

    /// <summary>
    /// attrs の ts / b / b4 / vb / inv を先頭（id の直後）へ寄せた並び（属性の濃い列を実際に動かす。バイナリ系のリテラル既定は
    /// 再指定の仕方を誤ると化ける／中断するので移動対象に含める）
    /// </summary>
    public static string[] AttrsReorderedColumns
    {
        get
        {
            string[] moved = ["ts", "b", "b4", "vb", "inv"];
            return
            [
                AttrsColumns[0],
                .. moved,
                .. AttrsColumns.Skip(1).Where(c => !moved.Contains(c)),
            ];
        }
    }

    /// <summary>attrs が意図した属性を実際に持っていること（検証の空洞化を防ぐ自己点検・同期の前の探針）</summary>
    public static void ExpectAttrsFixtureAttributes(ExpectationBuilder e) =>
        e.ProbeBefore("columns")
            .Single(
                "columns attrs.id.EXTRA value=*auto_increment*",
                // EXTRA は属性が並ぶ欄で、ON UPDATE の値の後ろに INVISIBLE が続き得る＝再構成は値だけを切り出す
                "columns attrs.ivu.EXTRA value=*on update CURRENT_TIMESTAMP(3)*",
                "columns attrs.ivu.EXTRA value=*INVISIBLE*",
                "columns attrs.geo.SRS_ID value=4326",
                "columns attrs.s.COLLATION_NAME value=utf8mb4_bin",
                "columns attrs.gs.EXTRA value=STORED GENERATED",
                // バイナリ系のリテラル既定はカタログでは 16 進テキスト（QUOTE すると文字列に化ける形）
                "columns attrs.vb.COLUMN_DEFAULT value=0x6162",
                "columns attrs.b4.COLUMN_DEFAULT value=0x6162",
                "columns attrs.cs.CHARACTER_SET_NAME value=latin1",
                "columns attrs.iz.COLUMN_TYPE value=*zerofill*"
            );

    /// <summary>
    /// 探針 <c>columns</c> の型の項目（COLUMN_TYPE・DATA_TYPE・NUMERIC_PRECISION）の行のパターン（型を変えた列の、比べない行）
    /// </summary>
    public static IEnumerable<string> TypeFieldPatterns(string table, params string[] columns) =>
        columns.SelectMany(column =>
            new[] { "COLUMN_TYPE", "DATA_TYPE", "NUMERIC_PRECISION" }.Select(field =>
                $"columns {table}.{column}.{field} *"
            )
        );

    /// <summary>探針 <c>columns</c> で、表の列の ORDINAL_POSITION がちょうど <paramref name="order"/> の並び</summary>
    public static void ExpectPositions(ExpectationBuilder e, string table, string[] order)
    {
        e.Probe("columns").Count($"columns {table}.*.ORDINAL_POSITION *", order.Length);

        for (var i = 0; i < order.Length; i++)
        {
            e.Probe("columns")
                .Single(
                    $"columns {table}.{order[i]}.ORDINAL_POSITION value="
                        + (i + 1).ToString(CultureInfo.InvariantCulture)
                );
        }
    }

    /// <summary>表の列を名前の並びへ並べ替える（並びに無い列は落とさない前提）</summary>
    public static void Reorder(Entity entity, string[] order)
    {
        var columns = order.Select(n => entity.Columns.Single(c => c.Name == n)).ToList();
        entity.Columns.Clear();
        entity.Columns.AddRange(columns);
    }

    /// <summary>説明つきの NULL 許容の varchar(10) の列</summary>
    public static Column Described(string name, string description)
    {
        var column = ExpectationSchema.Nullable(name, "varchar(10)");
        column.Description = description;
        return column;
    }

    /// <summary>' と \ と改行・タブを含む説明</summary>
    public const string SpecialDescription = "O'Brien \\ 「説明」\n2 行目 -- not a comment\t末尾";

    /// <summary>
    /// 実行器の失敗の行（MySQL の文の失敗の文言・部分適用の説明つき。<paramref name="fragment"/> は MySQL のエラーの断片のパターン）
    /// </summary>
    public static string StatementFailed(string fragment) =>
        "failed: "
        + string.Format(
            CultureInfo.InvariantCulture,
            ProviderStrings.Sync_Error_MySqlStatementFailed,
            "*",
            "*",
            fragment
        );

    /// <summary>
    /// スキーマ同期の往復の DB の図（<paramref name="stage"/>：0＝説明つきの parent と child(id, parent_id, to_alter, to_drop) と
    /// FK_child_parent（CASCADE）・1＝child を (id, parent_id, to_alter varchar(100) NOT NULL, added) へ変えた後・
    /// 2＝さらに外部キーを外した後）
    /// </summary>
    public static void SchemaSyncParentChild(ExpectationSchema s, int stage)
    {
        var name = ExpectationSchema.Nullable("name", "varchar(50)");
        name.Description = "名称";
        s.Table("parent", ExpectationSchema.Pk("id", "int"), name).Description = "親テーブルの説明";

        if (stage == 0)
        {
            s.Table(
                "child",
                ExpectationSchema.Pk("id", "int"),
                ExpectationSchema.Nullable("parent_id", "int"),
                ExpectationSchema.Nullable("to_alter", "varchar(50)"),
                ExpectationSchema.Nullable("to_drop", "int")
            );
        }
        else
        {
            s.Table(
                "child",
                ExpectationSchema.Pk("id", "int"),
                ExpectationSchema.Nullable("parent_id", "int"),
                ExpectationSchema.NotNull("to_alter", "varchar(100)"),
                ExpectationSchema.Nullable("added", "int")
            );
        }

        if (stage < 2)
        {
            s.Fk(
                "parent",
                "child",
                "FK_child_parent",
                ForeignKeyReferentialAction.Cascade,
                ("id", "parent_id")
            );
        }
    }

    /// <summary>説明の往復の段ごとの（表の説明, 列の説明）（0＝説明なし・1＝設定・2＝更新・3＝削除）</summary>
    public static (string Table, string Column) ItemsDescriptions(int stage) =>
        stage switch
        {
            0 => ("", ""),
            1 => ("商品テーブル 'マスタ'", "商品名 'name' 列"),
            2 => ("商品テーブル（更新後）", "商品名（更新後）"),
            _ => ("", ""),
        };

    /// <summary>items(id, name varchar(50) NULL) を、段 <paramref name="stage"/> の説明つきで作る</summary>
    public static string ItemsTableSql(int stage)
    {
        var (table, column) = ItemsDescriptions(stage);
        return "CREATE TABLE `items` (`id` int NOT NULL, `name` varchar(50) NULL"
            + (column.Length > 0 ? $" COMMENT '{column.Replace("'", "''")}'" : string.Empty)
            + ", CONSTRAINT `PK_items` PRIMARY KEY (`id`))"
            + (table.Length > 0 ? $" COMMENT='{table.Replace("'", "''")}'" : string.Empty)
            + ";";
    }

    /// <summary>
    /// 親 ra_parent ← 子 ra_child の外部キー fk_ra を ON DELETE / ON UPDATE とも SET DEFAULT で持つ図
    /// </summary>
    public static void SetDefaultParentChild(ExpectationSchema s)
    {
        s.Table("ra_parent", ExpectationSchema.Pk("id", "int"));
        s.Table(
            "ra_child",
            ExpectationSchema.Pk("id", "int"),
            ExpectationSchema.Nullable("parent_id", "int")
        );
        s.Fk(
            "ra_parent",
            "ra_child",
            "fk_ra",
            ForeignKeyReferentialAction.SetDefault,
            ForeignKeyReferentialAction.SetDefault,
            ("id", "parent_id")
        );
    }
}
