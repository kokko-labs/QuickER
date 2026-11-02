using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

internal static partial class SyncPlanExpectationRows
{
    /// <summary>SQL Server の実 DB の段の行の数の下限（行を足したら・消したら合わせる）</summary>
    private const int RealSqlServerRowCount = 58;

    /// <summary>実 DB の段：SQL Server の同期（外部キー・主キー・列の属性・実行時の制約名の逆引き）</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealSqlServerRows() =>
        [
            .. RealSqlServerForeignKeyRows(),
            .. RealSqlServerKeyAndDropRows(),
            .. RealSqlServerColumnAttributeRows(),
            .. RealSqlServerConstraintNameRows(),
            .. RealSqlServerCompositeForeignKeyRows(),
            .. RealSqlServerScriptRows(),
        ];

    /// <summary>外部キーの照合・作り直し・型の食い違い・SET DEFAULT / SET NULL</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealSqlServerForeignKeyRows() =>
        [
            Row(
                    "realdb-fk-unnamed-relationship-no-diff-after-ddl",
                    "[Integration] SQL Server 同期: 名前の無いリレーションの図は DDL 適用後に外部キーの差分を出さない",
                    DialectNames.SqlServer
                )
                .Live(s => RealScenarios.ParentChild(s, null))
                .Target(s => RealScenarios.ParentChild(s, null))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Diff.Absent("AddForeignKey *", "DropForeignKey *", "AlterForeignKey *");
                        e.Outcome.Exact("committed");
                        e.After.Count("fk *", 1);
                    })
                ),
            Row(
                    "realdb-fk-unnamed-same-pair-ddl-applies-no-diff",
                    "[Integration] SQL Server 同期: 名前の無い同じ組の外部キー 2 本の図は DDL が通り、適用後に外部キーの差分を出さない",
                    DialectNames.SqlServer
                )
                .Live(RealScenarios.UnnamedSamePair)
                .Target(RealScenarios.UnnamedSamePair)
                .Select(items => RealScenarios.SelectKinds(items))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Before.Count("fk *", 2);
                        e.Before.Single(
                            "fk sd_child FK_sd_child_sd_parent (parent_id) -> sd_parent(id) *",
                            "fk sd_child FK_sd_child_sd_parent_backup_parent_id (backup_parent_id) -> sd_parent(id) *"
                        );
                        e.Diff.Absent("AddForeignKey *", "DropForeignKey *", "AlterForeignKey *");
                        e.Outcome.Exact("committed");
                    })
                ),
            Row(
                    "realdb-fk-drop-one-of-same-pair-keeps-the-other",
                    "[Integration] SQL Server 同期: 同じ列ペアの外部キー 2 本の片方を消して列の型を変えても、もう片方を残して成功する",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    RealScenarios.ParentChild(s, "fk_sd_a");
                    s.Fk("sd_parent", "sd_child", "fk_sd_b", ("id", "parent_id"));
                })
                .TargetFromLive(s =>
                {
                    s.Relationships.RemoveAll(r => r.ConstraintName == "fk_sd_b");
                    s.Col("sd_parent", "id").DataType = "bigint";
                    s.Col("sd_child", "parent_id").DataType = "bigint";
                })
                .Select(items =>
                    RealScenarios.SelectKinds(
                        items,
                        SchemaDiffKind.DropForeignKey,
                        SchemaDiffKind.AlterColumn
                    )
                )
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Script.Contains(
                            "*ALTER TABLE [sd_child] DROP CONSTRAINT [fk_sd_b];*",
                            "*ALTER COLUMN [parent_id]*"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Count("fk *", 1);
                        e.After.Single("fk sd_child fk_sd_a (parent_id) -> sd_parent(id) *");
                        e.After.Single("col sd_child.parent_id bigint *");
                    })
                ),
            Row(
                    "realdb-fk-unnamed-many-to-many-first-no-diff-with-default-named-db",
                    "[Integration] SQL Server 同期: 名前の無い多対多が先に並んでも外部キーは既定名で作られ、既定名の DB と差分を出さない",
                    DialectNames.SqlServer
                )
                .Live(s => RealScenarios.ParentChild(s, null))
                .Target(RealScenarios.ManyToManyFirst)
                .Select(items => RealScenarios.SelectKinds(items))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Before.Count("fk *", 1);
                        e.Before.Single("fk sd_child FK_sd_child_sd_parent *");
                        e.Diff.Absent("AddForeignKey *", "DropForeignKey *", "AlterForeignKey *");
                        e.Outcome.Exact("committed");
                    })
                ),
            Row(
                    "realdb-fk-unnamed-many-to-many-first-ddl-uses-default-name",
                    "[Integration] SQL Server 同期: 名前の無い多対多が先に並んでも外部キーは既定名で作られ、既定名の DB と差分を出さない",
                    DialectNames.SqlServer
                )
                .Live(RealScenarios.ManyToManyFirst)
                .TargetFromLive(_ => { })
                .Select(items => RealScenarios.SelectKinds(items))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Before.Count("fk *", 1);
                        e.Before.Single("fk sd_child FK_sd_child_sd_parent *");
                        e.Outcome.Exact("committed");
                    })
                ),
            Row(
                    "realdb-fk-referential-action-change-default-selection-changes-nothing",
                    "[Integration] SQL Server 同期: 参照アクションの変更は外部キー変更として作り直され、既定のままでも失敗しない",
                    DialectNames.SqlServer
                )
                .Live(s => RealScenarios.ParentChild(s, null))
                .TargetFromLive(s =>
                    s.Relationships[0].OnDelete = ForeignKeyReferentialAction.Cascade
                )
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Diff.Single("AlterForeignKey *");
                        e.Diff.Absent("AddForeignKey *", "DropForeignKey *");
                        e.Outcome.Exact("committed");
                        e.After.Count("fk *", 1);
                        e.After.Single("fk * del=NoAction *");
                    })
                ),
            Row(
                    "realdb-fk-referential-action-change-selected-rebuilds",
                    "[Integration] SQL Server 同期: 参照アクションの変更は外部キー変更として作り直され、既定のままでも失敗しない",
                    DialectNames.SqlServer
                )
                .Live(s => RealScenarios.ParentChild(s, null))
                .TargetFromLive(s =>
                    s.Relationships[0].OnDelete = ForeignKeyReferentialAction.Cascade
                )
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterForeignKey))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Outcome.Exact("committed");
                        e.After.Count("fk *", 1);
                        e.After.Single("fk sd_child FK_sd_child_sd_parent * del=Cascade *");
                    })
                ),
            SetDefaultOnNotNullRow(hasDefault: false),
            SetDefaultOnNotNullRow(hasDefault: true),
            Row(
                    "realdb-fk-type-mismatch-one-end-warns-and-fails",
                    "[Integration] SQL Server 同期: 外部キーの片側だけ型を変えると警告し、実行しても外部キーと型は元のまま",
                    DialectNames.SqlServer
                )
                .Live(s => RealScenarios.ParentChild(s, null))
                .TargetFromLive(s => s.Col("sd_parent", "id").DataType = "bigint")
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Warnings.Single(
                            "ForeignKeyColumnTypeMismatch(*)*",
                            "ForeignKeyColumnTypeMismatch(*): FK_sd_child_sd_parent"
                        );
                        e.Outcome.Exact("failed: *");
                        e.After.Single("col sd_parent.id int *");
                        e.After.Count("fk *", 1);
                    })
                ),
            Row(
                    "realdb-fk-alter-with-type-mismatch-excluded-and-fails",
                    "[Integration] SQL Server 同期: 型が食い違う外部キーの作り直しは外して警告し、実行しても外部キーは元のまま",
                    DialectNames.SqlServer
                )
                .Live(s => RealScenarios.ParentChild(s, null))
                .TargetFromLive(s =>
                {
                    s.Col("sd_parent", "id").DataType = "bigint";
                    s.Relationships.Single().OnDelete = ForeignKeyReferentialAction.Cascade;
                })
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Warnings.Single(
                            "ForeignKeyColumnTypeMismatch(*)*",
                            "ForeignKeyColumnTypeMismatch(*): FK_sd_child_sd_parent"
                        );
                        e.Sections.Absent("DropForeignKey*", "AddForeignKey*");
                        e.Outcome.Exact("failed: *");
                        e.After.Single("col sd_parent.id int *");
                        e.After.Count("fk *", 1);
                        e.After.Single("fk * del=NoAction *");
                    })
                ),
            Row(
                    "realdb-fk-set-null-child-not-null-keeps-that-fk",
                    "[Integration] SQL Server 同期: SET NULL の子の列を NOT NULL にする変更は名指しし、その外部キーだけ外さずに通る",
                    DialectNames.SqlServer
                )
                .TargetFromLive(s =>
                {
                    s.Col("sd_child", "parent_id").IsNullable = false;
                    s.Col("sd_child", "other_id").DataType = "bigint";
                    s.Col("sd_other", "id").DataType = "bigint";
                })
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(RealScenarios.SetNullChildSql)
                        .Expect(e =>
                        {
                            e.Warnings.Single(
                                "SetNullOnNotNullColumn(*)*",
                                "SetNullOnNotNullColumn(sd_child): fk_sd_parent (parent_id)"
                            );
                            e.Items.Count("DropForeignKey > *", 1);
                            e.Items.Single("DropForeignKey > DropForeignKey * fk=fk_sd_other *");
                            e.Outcome.Exact("committed");
                            e.After.Single(
                                "col sd_child.parent_id * notnull*",
                                "col sd_child.other_id bigint *",
                                "fk sd_child fk_sd_parent * del=SetNull *"
                            );
                            e.After.Contains("fk sd_child fk_sd_other *");
                        })
                ),
            Row(
                    "realdb-fk-set-null-child-type-and-not-null-fails-5074",
                    "[Integration] SQL Server 同期: SET NULL の子の列の型も変える変更は 5074 で失敗し、何も変えない",
                    DialectNames.SqlServer
                )
                .TargetFromLive(s =>
                {
                    var parentId = s.Col("sd_child", "parent_id");
                    parentId.IsNullable = false;
                    parentId.DataType = "bigint";
                })
                .Select(RealScenarios.SelectAll)
                .OnRealDatabase(r =>
                    r.LiveSql(RealScenarios.SetNullChildSql)
                        .Expect(e =>
                        {
                            e.Warnings.Unordered(
                                "ForeignKeyColumnTypeMismatch(*)*",
                                "SetNullOnNotNullColumn(*)*"
                            );
                            e.Sections.Absent("DropForeignKey*", "AddForeignKey*");
                            e.Outcome.Exact("failed: *is dependent on column*");
                            e.After.Single(
                                "col sd_child.parent_id int null",
                                "fk sd_child fk_sd_parent * del=SetNull *"
                            );
                        })
                ),
            Row(
                    "realdb-fk-reorder-referenced-composite-pk-rebuilds-in-new-order",
                    "[Integration] SQL Server 同期: 参照される複合主キーの列順だけを入れ替えると、外部キーを新しい並びで付け直して成功する",
                    DialectNames.SqlServer
                )
                .TargetFromLive(s => s.PrimaryKeyOrder("sd_pair", "b", "a"))
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(RealScenarios.PairRefSql)
                        .Setup(
                            "INSERT INTO [sd_pair] ([a], [b]) VALUES (1, 2);"
                                + "INSERT INTO [sd_ref] ([id], [ra], [rb]) VALUES (1, 1, 2);"
                        )
                        .Expect(e =>
                        {
                            e.Warnings.Empty();
                            e.Sections.Single(
                                "DropForeignKey: *",
                                "DropForeignKey: DropForeignKey sd_ref fk=fk_sd_ref"
                            );
                            e.Script.Contains(
                                "*FOREIGN KEY ([rb], [ra]) REFERENCES [sd_pair] ([b], [a])*",
                                "*ALTER TABLE [sd_pair] ADD CONSTRAINT [PK_sd_pair] PRIMARY KEY ([b], [a]);*"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Single(
                                "pk sd_pair (b,a)",
                                "fk sd_ref fk_sd_ref (rb,ra) -> sd_pair(b,a) *"
                            );
                            e.AfterDiff.Empty();
                        })
                ),
            Row(
                    "realdb-fk-alter-in-other-order-rebuilt-in-key-order",
                    "[Integration] SQL Server 同期: 図の並びが参照先のキーと違う外部キーの作り直しは、キーの並びで付け直して成功する",
                    DialectNames.SqlServer
                )
                .TargetFromLive(s =>
                {
                    var fk = s.Relationships.Single();
                    fk.ColumnPairs = [.. fk.ColumnPairs.AsEnumerable().Reverse()];
                    fk.OnDelete = ForeignKeyReferentialAction.Cascade;
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterForeignKey))
                .OnRealDatabase(r =>
                    r.LiveSql(RealScenarios.PairRefSql)
                        .Expect(e =>
                        {
                            e.Diff.Exact("AlterForeignKey *");
                            e.Warnings.Empty();
                            e.Script.Contains(
                                "*FOREIGN KEY ([ra], [rb]) REFERENCES [sd_pair] ([a], [b])*"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Count("fk *", 1);
                            e.After.Single(
                                "fk sd_ref fk_sd_ref (ra,rb) -> sd_pair(a,b) del=Cascade *"
                            );
                            e.AfterDiff.Empty();
                        })
                ),
            Row(
                    "realdb-fk-add-on-unselected-new-column-skipped-with-warning",
                    "[Integration] SQL Server 同期: 選んでいない列の追加を前提にする外部キーは外して警告し、DB を変えない",
                    DialectNames.SqlServer
                )
                .Live(s => RealScenarios.ParentChild(s, "FK_sd_child_parent"))
                .TargetFromLive(s =>
                {
                    s["sd_child"].Columns.Add(ExpectationSchema.Nullable("other_parent_id", "int"));
                    s.Fk("sd_parent", "sd_child", "FK_sd_child_other", ("id", "other_parent_id"));
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AddForeignKey))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Warnings.Exact("*: FK_sd_child_other (sd_child.other_parent_id)");
                        e.Outcome.Exact("committed");
                        e.After.Count("fk *", 1);
                        e.After.Count("col sd_child.*", 2);
                    })
                ),
            Row(
                    "realdb-fk-composite-other-order-ddl-applies-no-diff",
                    "[Integration] SQL Server: 列ペアの並びが参照先のキーと違う複合外部キーの DDL は適用でき、取り込み直しても差分が出ない",
                    DialectNames.SqlServer
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

    /// <summary>NOT NULL の子の列への SET DEFAULT の外部キーの追加（DB 側の列の DEFAULT 制約の有無）</summary>
    private static ExpectationRow SetDefaultOnNotNullRow(bool hasDefault) =>
        Row(
                $"realdb-fk-set-default-on-not-null-warns-{(hasDefault ? "with-default-commits" : "without-default-fails")}",
                "[Integration] SQL Server 同期: NOT NULL の子の列への SET DEFAULT の追加は警告し、DEFAULT 制約があれば通る",
                DialectNames.SqlServer
            )
            .TargetFromLive(s =>
                s.Fk(
                    "sd_parent",
                    "sd_child",
                    "FK_sd_child_sd_parent",
                    ForeignKeyReferentialAction.SetDefault,
                    ("id", "parent_id")
                )
            )
            .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AddForeignKey))
            .OnRealDatabase(r =>
                r.LiveSql(
                        "CREATE TABLE [sd_parent] ([id] int NOT NULL, CONSTRAINT [PK_sd_parent] PRIMARY KEY ([id]));"
                    )
                    .LiveSql(
                        "CREATE TABLE [sd_child] ([id] int NOT NULL, [parent_id] int NOT NULL"
                            + (
                                hasDefault
                                    ? " CONSTRAINT [DF_sd_child_parent_id] DEFAULT 1"
                                    : string.Empty
                            )
                            + ", CONSTRAINT [PK_sd_child] PRIMARY KEY ([id]));"
                    )
                    .Expect(e =>
                    {
                        e.Warnings.Single(
                            "SetDefaultOnNotNullColumn(*)*",
                            "SetDefaultOnNotNullColumn(*): FK_sd_child_sd_parent (parent_id)"
                        );

                        if (hasDefault)
                        {
                            e.Outcome.Exact("committed");
                            e.After.Count("fk *", 1);
                            e.After.Single("fk * del=SetDefault *");
                        }
                        else
                        {
                            e.Outcome.Exact("failed: *default constraint*");
                        }
                    })
            );

    /// <summary>主キーの型・主キーの変更と、表・列の削除に伴う外部キー</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealSqlServerKeyAndDropRows() =>
        [
            Row(
                    "realdb-pk-column-type-change-both-ends-keeps-keys",
                    "[Integration] SQL Server 同期: 主キー列と参照する列を同じ型へ変える同期が主キー・外部キーを保って成功する",
                    DialectNames.SqlServer
                )
                .Live(s => RealScenarios.ParentChild(s, null))
                .TargetFromLive(s =>
                {
                    s.Col("sd_parent", "id").DataType = "bigint";
                    s.Col("sd_child", "parent_id").DataType = "bigint";
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.Setup(RealScenarios.ParentChildRowsSql)
                        .Expect(e =>
                        {
                            e.Warnings.Empty();
                            e.Outcome.Exact("committed");
                            e.After.Single(
                                "col sd_parent.id bigint *",
                                "pk sd_parent (id)",
                                "col sd_child.parent_id bigint *"
                            );
                            e.After.Count("fk *", 1);
                        })
                ),
            Row(
                    "realdb-pk-change-with-child-drop-table",
                    "[Integration] SQL Server 同期: 参照される主キーの変更と子表の削除を同時に選んでも成功する",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    RealScenarios.ParentChild(s, null);
                    s["sd_parent"].Columns.Add(ExpectationSchema.NotNull("code", "int"));
                })
                .TargetFromLive(s =>
                {
                    s.Col("sd_parent", "id").IsPrimaryKey = false;
                    s.Col("sd_parent", "code").IsPrimaryKey = true;
                    s.Entities.RemoveAll(e => e.TableName == "sd_child");
                    s.Relationships.Clear();
                })
                .Select(items =>
                    RealScenarios.SelectKinds(
                        items,
                        SchemaDiffKind.AlterPrimaryKey,
                        SchemaDiffKind.DropTable
                    )
                )
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Outcome.Exact("committed");
                        e.After.Count("table *", 1);
                        e.After.Single("table sd_parent", "pk sd_parent (code)");
                    })
                ),
            Row(
                    "realdb-drop-parent-and-child-tables-without-fk-drop",
                    "[Integration] SQL Server 同期: 親と子の表の削除だけを選んでも、親が先に並んで失敗しない",
                    DialectNames.SqlServer
                )
                .Live(s => RealScenarios.ParentChild(s, null, "sd_a_parent", "sd_b_child"))
                .TargetFromLive(s =>
                {
                    s.Entities.Clear();
                    s.Relationships.Clear();
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.DropTable))
                .OnRealDatabase(r =>
                    r.Setup(
                            "INSERT INTO sd_a_parent (id) VALUES (1); INSERT INTO sd_b_child (id, parent_id) VALUES (1, 1);"
                        )
                        .Expect(e =>
                        {
                            e.Diff.Exact(
                                "DropTable sd_a_parent *",
                                "DropTable sd_b_child *",
                                "DropForeignKey sd_b_child *"
                            );
                            e.Warnings.Empty();
                            e.Outcome.Exact("committed");
                            e.After.Absent("table *");
                        })
                ),
            DropReferencedEndRow(SchemaDiffKind.DropTable),
            DropReferencedEndRow(SchemaDiffKind.DropColumn),
            Row(
                    "realdb-pk-change-only-makes-new-key-column-not-null",
                    "[Integration] SQL Server 同期: 主キーの変更だけを選んでも、新しい主キー列を NOT NULL にして成功する",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    RealScenarios.ParentChild(s, null);
                    s["sd_child"].Columns.Add(ExpectationSchema.Nullable("code", "int"));
                })
                .TargetFromLive(s =>
                {
                    var code = s.Col("sd_child", "code");
                    code.IsPrimaryKey = true;
                    code.IsNullable = false;
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.Setup(
                            "INSERT INTO sd_parent (id) VALUES (1); INSERT INTO sd_child (id, parent_id, code) VALUES (1, 1, 10);"
                        )
                        .Expect(e =>
                        {
                            e.Warnings.Absent("SelectedItemRequiresUnselectedItem*");
                            e.Outcome.Exact("committed");
                            e.After.Single(
                                "pk sd_child (id,code)",
                                "col sd_child.code int notnull*"
                            );
                        })
                ),
        ];

    /// <summary>参照される親の表だけ・外部キーの子列だけを消す（外部キーの削除は選ばない）</summary>
    private static ExpectationRow DropReferencedEndRow(SchemaDiffKind dropKind) =>
        Row(
                $"realdb-drop-referenced-end-without-fk-drop-{(dropKind == SchemaDiffKind.DropTable ? "table" : "column")}",
                "[Integration] SQL Server 同期: 親の表・外部キーの列だけを消しても、端の消える外部キーを外して成功する",
                DialectNames.SqlServer
            )
            .Live(s => RealScenarios.ParentChild(s, "FK_sd_child_parent"))
            .TargetFromLive(s =>
            {
                s.Relationships.Clear();

                if (dropKind == SchemaDiffKind.DropTable)
                {
                    s.Entities.RemoveAll(e => e.TableName == "sd_parent");
                }
                else
                {
                    s["sd_child"].Columns.RemoveAll(c => c.Name == "parent_id");
                }
            })
            .Select(items => RealScenarios.SelectKinds(items, dropKind))
            .OnRealDatabase(r =>
                r.Setup(RealScenarios.ParentChildRowsSql)
                    .Expect(e =>
                    {
                        e.Warnings.Empty();
                        e.Outcome.Exact("committed");
                        e.After.Absent("fk *");
                        e.After.Contains("table sd_child");
                    })
            );

    /// <summary>列の変更・削除が図に無い列属性（照合順序・DEFAULT 制約）を保つ</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealSqlServerColumnAttributeRows() =>
        [
            Row(
                    "realdb-column-alter-keeps-collation",
                    "[Integration] SQL Server 同期: 列の変更は照合順序を保つ",
                    DialectNames.SqlServer
                )
                .TargetFromLive(s =>
                {
                    var name = s.Col("ca", "name");
                    name.DataType = "varchar(40)";
                    name.IsNullable = true;
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE [ca] ([id] int NOT NULL PRIMARY KEY, "
                                + "[name] varchar(20) COLLATE Japanese_CI_AS NOT NULL);"
                        )
                        .Probe("columns")
                        .Expect(e =>
                        {
                            e.Script.Contains("*ISNULL(N' COLLATE ' + @coll, N'')*");
                            e.Outcome.Exact("committed");
                            e.Probe("columns")
                                .Single(
                                    "columns dbo.ca.name type=varchar len=40 nullable=True collation=Japanese_CI_AS *"
                                );
                        })
                ),
            AlterColumnWithDefaultRow(
                "varchar-to-nvarchar",
                "varchar(20)",
                "('it''s')",
                "nvarchar(40)"
            ),
            AlterColumnWithDefaultRow("int-to-bigint", "int", "((5))", "bigint"),
            Row(
                    "realdb-column-alter-incompatible-default-commits-insert-fails",
                    "[Integration] SQL Server 同期: 既定値が新しい型に合わない型の変更は通るが、既定値を使う追加が失敗する",
                    DialectNames.SqlServer
                )
                .TargetFromLive(s => s.Col("cx", "v").DataType = "int")
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE [cx] ([id] int NOT NULL PRIMARY KEY, [v] varchar(20) NOT NULL "
                                + "CONSTRAINT [DF_cx_v] DEFAULT ('abc'));"
                        )
                        .Probe("columns")
                        .Query("insert", "INSERT INTO [cx] ([id]) VALUES (1);")
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.Probe("columns")
                                .Single(
                                    "columns dbo.cx.v type=int * default=DF_cx_v definition=('abc')"
                                );
                            e.Probe("insert").Exact("error SqlException: *");
                        })
                ),
            Row(
                    "realdb-column-type-kind-change-with-default-warns",
                    "[Integration] SQL Server 同期: 既定値のある列の型の種類の変更は事前に警告される",
                    DialectNames.SqlServer
                )
                .TargetFromLive(s => s.Col("jd", "v").DataType = "int")
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE [jd] ([id] int NOT NULL PRIMARY KEY, [v] varchar(20) NOT NULL CONSTRAINT [DF_jd_v] DEFAULT ('abc'));"
                        )
                        .Expect(e =>
                        {
                            e.Warnings.Single(
                                "DefaultOnColumnTypeKindChange(*)*",
                                "DefaultOnColumnTypeKindChange(*): v (*"
                            );
                            e.Outcome.Exact("committed");
                        })
                ),
            DropColumnWithDefaultRow(withDefault: true),
            DropColumnWithDefaultRow(withDefault: false),
        ];

    /// <summary>DEFAULT 制約のある列の型の変更（制約名に ] と '、定義に ' を含む）</summary>
    private static ExpectationRow AlterColumnWithDefaultRow(
        string shape,
        string oldType,
        string definition,
        string newType
    ) =>
        Row(
                $"realdb-column-alter-readds-default-{shape}",
                "[Integration] SQL Server 同期: DEFAULT 制約のある列の型の変更は、同じ名前と定義で付け直して成功する",
                DialectNames.SqlServer
            )
            .TargetFromLive(s => s.Col("cd", "v").DataType = newType)
            .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
            .OnRealDatabase(r =>
                r.LiveSql(
                        $"CREATE TABLE [cd] ([id] int NOT NULL PRIMARY KEY, [v] {oldType} NOT NULL "
                            + $"CONSTRAINT [DF_a]]b'c] DEFAULT {definition});"
                    )
                    .Probe("columns")
                    .Expect(e =>
                    {
                        // 型の変わる変更は DEFAULT 制約を読んで外し、同じ名前で付け直す（素の ALTER COLUMN は 5074 で断られる）
                        e.Script.Contains(
                            "*FROM sys.default_constraints dc*",
                            "*WHERE dc.parent_object_id = OBJECT_ID(N'[cd]') AND c.name = N'v';*",
                            $"*SET @sql = N'ALTER TABLE [cd] ALTER COLUMN [v] {newType}' + ISNULL(N' COLLATE ' + @coll, N'') + N' NOT NULL';*",
                            "*QUOTENAME(@df) + N' DEFAULT ' + @dfdef + N' FOR [v]';*"
                        );
                        e.Script.Absent("ALTER TABLE [cd] ALTER COLUMN *");

                        // 文字列型の列だけ照合順序を読み直す
                        if (newType.StartsWith("nvarchar", StringComparison.Ordinal))
                        {
                            e.Script.Contains(
                                "*SELECT @coll = c.collation_name FROM sys.columns c*"
                            );
                        }
                        else
                        {
                            e.Script.Absent("*SELECT @coll = *");
                        }

                        e.Outcome.Exact("committed");
                        e.Probe("columns")
                            .Single(
                                $"columns dbo.cd.v type={newType.Split('(')[0]} * default=DF_a]b'c definition={definition}"
                            );
                    })
            );

    /// <summary>DEFAULT 制約のある（無い）列の削除（別の表の同じ名前の列の DEFAULT 制約は残る）</summary>
    private static ExpectationRow DropColumnWithDefaultRow(bool withDefault) =>
        Row(
                $"realdb-column-drop-{(withDefault ? "with" : "without")}-default-constraint",
                "[Integration] SQL Server 同期: DEFAULT 制約のある列の削除は、制約を外して成功する",
                DialectNames.SqlServer
            )
            .TargetFromLive(s => s["dd"].Columns.RemoveAll(c => c.Name == "v"))
            .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.DropColumn))
            .OnRealDatabase(r =>
                r.LiveSql(
                        "CREATE TABLE [dd] ([id] int NOT NULL PRIMARY KEY, [v] int NOT NULL"
                            + (withDefault ? " CONSTRAINT [DF_a]]b'c] DEFAULT ((5))" : "")
                            + ");"
                            + "CREATE TABLE [dd2] ([id] int NOT NULL PRIMARY KEY, "
                            + "[v] int NOT NULL CONSTRAINT [DF_other] DEFAULT ((7)));"
                    )
                    .Probe("columns")
                    .Expect(e =>
                    {
                        e.Outcome.Exact("committed");
                        e.Script.Contains(
                            "*N'ALTER TABLE [dd] DROP CONSTRAINT ' + QUOTENAME(@df)*",
                            "*ALTER TABLE [dd] DROP COLUMN [v];*"
                        );
                        e.Probe("columns").Absent("columns dbo.dd.v *");
                        e.Probe("columns").Single("columns dbo.dd2.v * default=DF_other *");
                    })
            );

    /// <summary>実行時に読む旧主キー名（特殊文字・別スキーマの同名表）</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealSqlServerConstraintNameRows() =>
        [
            Row(
                    "realdb-pk-alter-constraint-name-with-bracket-and-quote",
                    "[Integration] 主キー名に ] と ' を含む表でも主キーの付け替えが成功する",
                    DialectNames.SqlServer
                )
                .TargetFromLive(s => RealScenarios.MovePrimaryKeyToCode(s, "odd_orders"))
                .Select(items => RealScenarios.AlsoSelect(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE [odd_orders] ([id] int NOT NULL, [code] int NOT NULL, "
                                + "CONSTRAINT [PK_odd]]name'x] PRIMARY KEY ([id]));"
                        )
                        .Probe("keys")
                        .Expect(e =>
                        {
                            e.Diff.Single("AlterPrimaryKey *");
                            e.Script.Contains("*QUOTENAME(@pk)*");
                            e.Outcome.Exact("committed");
                            e.Probe("keys").Exact("keys dbo.odd_orders kind=PK name=PK_odd_orders");
                        })
                ),
            Row(
                    "realdb-pk-alter-ignores-same-name-in-other-schema",
                    "[Integration] 主キーの付け替えは別スキーマの同名表の主キーを拾わない",
                    DialectNames.SqlServer
                )
                .TargetFromLive(s => RealScenarios.MovePrimaryKeyToCode(s, "Orders"))
                .Select(items => RealScenarios.AlsoSelect(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.Prepare("IF SCHEMA_ID(N'other') IS NULL EXEC(N'CREATE SCHEMA [other]');")
                        .LiveSql(
                            "CREATE TABLE [dbo].[Orders] ([id] int NOT NULL, [code] int NOT NULL, "
                                + "CONSTRAINT [PK_dbo_Orders] PRIMARY KEY ([id]));"
                                + "CREATE TABLE [other].[Orders] ([id] int NOT NULL, "
                                + "CONSTRAINT [PK_other_Orders] PRIMARY KEY ([id]));"
                        )
                        .Probe("keys")
                        .Expect(e =>
                        {
                            e.Diff.Single("AlterPrimaryKey *");
                            e.Outcome.Exact("committed");
                            e.Probe("keys")
                                .Exact(
                                    "keys dbo.Orders kind=PK name=PK_Orders",
                                    "keys other.Orders kind=PK name=PK_other_Orders"
                                );
                        })
                ),
        ];

    /// <summary>複合外部キーの追加・削除・主キーの変更に伴う張り直し</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealSqlServerCompositeForeignKeyRows() =>
        [
            Row(
                    "realdb-fk-composite-add-keeps-pairs",
                    "[Integration] C: 複合外部キーの追加・削除が実 DB へ反映される",
                    DialectNames.SqlServer
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
                    r.LiveSql(RealScenarios.CompositeParentChildSql(withForeignKey: false))
                        .Expect(e =>
                        {
                            e.Diff.Single(
                                "AddForeignKey *",
                                "AddForeignKey * pairs=(a>a_ref,b>b_ref) *"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Count("fk *", 1);
                            e.After.Single("fk cfk_child * (a_ref,b_ref) -> cfk_parent(a,b) *");
                        })
                ),
            Row(
                    "realdb-fk-composite-drop",
                    "[Integration] C: 複合外部キーの追加・削除が実 DB へ反映される",
                    DialectNames.SqlServer
                )
                .TargetFromLive(s => s.Relationships.Clear())
                .Select(items => RealScenarios.AlsoSelect(items, SchemaDiffKind.DropForeignKey))
                .OnRealDatabase(r =>
                    r.LiveSql(RealScenarios.CompositeParentChildSql(withForeignKey: true))
                        .Expect(e =>
                        {
                            e.Diff.Single("DropForeignKey *");
                            e.Script.Contains(
                                "*ALTER TABLE [cfk_child] DROP CONSTRAINT [FK_cfk_child_cfk_parent];*"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Absent("fk *");
                        })
                ),
            Row(
                    "realdb-fk-composite-survives-pk-change",
                    "[Integration] C: 複合主キー変更に伴う複合外部キーの張り直しで構成列が失われない",
                    DialectNames.SqlServer
                )
                .TargetFromLive(s => s.Col("cfk_parent", "code").IsPrimaryKey = false)
                .Select(items => RealScenarios.AlsoSelect(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE [cfk_parent] ([id] int NOT NULL, [code] int NOT NULL, "
                                + "[a] int NOT NULL, [b] int NOT NULL, "
                                + "CONSTRAINT [PK_cfk_parent] PRIMARY KEY ([id], [code]), "
                                + "CONSTRAINT [UQ_cfk_parent_a_b] UNIQUE ([a], [b]));"
                        )
                        .LiveSql(
                            "CREATE TABLE [cfk_child] ([id] int NOT NULL, [a_ref] int NOT NULL, "
                                + "[b_ref] int NOT NULL, CONSTRAINT [PK_cfk_child] PRIMARY KEY ([id]), "
                                + "CONSTRAINT [FK_cfk_child_cfk_parent] FOREIGN KEY ([a_ref], [b_ref]) "
                                + "REFERENCES [cfk_parent] ([a], [b]));"
                        )
                        .Expect(e =>
                        {
                            e.Warnings.Empty();
                            e.Outcome.Exact("committed");
                            e.After.Single("pk cfk_parent (id)");
                            e.After.Count("fk *", 1);
                            e.After.Single("fk cfk_child * (a_ref,b_ref) -> cfk_parent(a,b) *");
                        })
                ),
        ];

    /// <summary>
    /// 同期スクリプトの文の形（表・列・外部キー・一意制約・主キー・説明の文）を実 DB で流し、結果と流れた文の形を見る
    /// </summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealSqlServerScriptRows() =>
        [
            .. RealSqlServerScriptTableAndColumnRows(),
            .. RealSqlServerScriptForeignKeyRows(),
            .. RealSqlServerScriptDescriptionRows(),
            .. RealSqlServerScriptKeyRows(),
        ];

    /// <summary>表・列の追加と列の変更・削除の文</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealSqlServerScriptTableAndColumnRows() =>
        [
            Row(
                    "realdb-script-add-table-create",
                    "AddTable は CREATE TABLE と PK を含む",
                    DialectNames.SqlServer
                )
                .Live(_ => { })
                .TargetFromLive(s =>
                    s.Table(
                        "Customer",
                        ExpectationSchema.Pk("Id", "int"),
                        ExpectationSchema.Nullable("Name", "nvarchar(50)")
                    )
                )
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AddTable))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Script.Contains(
                            "*CREATE TABLE [Customer]*",
                            "*[Id] int NOT NULL*",
                            "*PRIMARY KEY ([Id])*"
                        );
                        e.Script.Absent("*UNIQUE*");
                        e.Outcome.Exact("committed");
                        e.After.Single(
                            "table Customer",
                            "col Customer.Id int notnull pk",
                            "col Customer.Name nvarchar(50) null",
                            "pk Customer (Id)"
                        );
                        e.After.Absent("uq *");
                    })
                ),
            Row(
                    "realdb-script-add-table-unique-constraints-sync",
                    "AddTable の UNIQUE 句は DDL 生成と同形",
                    DialectNames.SqlServer
                )
                .Live(_ => { })
                .TargetFromLive(Shops)
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AddTable))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Script.Contains(
                            "*CONSTRAINT [PK_shops] PRIMARY KEY ([id]),*",
                            "*CONSTRAINT [UQ_shops_code] UNIQUE ([code]),*",
                            "*CONSTRAINT [UQ_shops_region_code] UNIQUE ([region], [code])"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Count("uq *", 2);
                        e.After.Single(
                            "uq shops UQ_shops_code (code)",
                            "uq shops UQ_shops_region_code (region,code)"
                        );
                    })
                ),
            Row(
                    "realdb-script-add-table-unique-constraints-ddl",
                    "AddTable の UNIQUE 句は DDL 生成と同形",
                    DialectNames.SqlServer
                )
                .Live(Shops)
                .Target(Shops)
                .Select(items => RealScenarios.SelectKinds(items))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Before.Count("uq *", 2);
                        e.Before.Single(
                            "uq shops UQ_shops_code (code)",
                            "uq shops UQ_shops_region_code (region,code)"
                        );
                        e.Diff.Empty();
                        e.Outcome.Exact("committed");
                    })
                ),
            Row(
                    "realdb-script-add-column-not-null",
                    "AddColumn は ALTER TABLE ADD を生成する",
                    DialectNames.SqlServer
                )
                .Live(s => s.Table("Customer", ExpectationSchema.Pk("Id", "int")))
                .TargetFromLive(s =>
                    s["Customer"].Columns.Add(ExpectationSchema.NotNull("Email", "nvarchar(200)"))
                )
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AddColumn))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Script.Contains(
                            "*ALTER TABLE [Customer] ADD [Email] nvarchar(200) NOT NULL;*"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Single("col Customer.Email nvarchar(200) notnull");
                    })
                ),
            Row(
                    "realdb-script-unselected-add-column-not-emitted",
                    "選択されていない項目はスクリプトに含まれない",
                    DialectNames.SqlServer
                )
                .Live(s => s.Table("Customer", ExpectationSchema.Pk("Id", "int")))
                .TargetFromLive(s =>
                    s["Customer"].Columns.Add(ExpectationSchema.Nullable("Email", "nvarchar(200)"))
                )
                .Select(items => RealScenarios.SelectKinds(items))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Diff.Single("AddColumn Customer.Email *");
                        e.Script.Absent("*Email*");
                        e.Outcome.Exact("committed");
                        e.After.Absent("col Customer.Email *");
                    })
                ),
            AlterNullabilityOnlyRow(
                "int-keeps-default",
                "[code] int NOT NULL CONSTRAINT [DF_ra_code] DEFAULT ((1))",
                "int",
                "type=int * nullable=True * default=DF_ra_code definition=((1))"
            ),
            AlterNullabilityOnlyRow(
                "string-keeps-collation-and-default",
                "[code] nvarchar(10) COLLATE Japanese_CI_AS NOT NULL CONSTRAINT [DF_ra_code] DEFAULT (N'x')",
                "nvarchar(10)",
                "type=nvarchar len=10 nullable=True collation=Japanese_CI_AS default=DF_ra_code definition=(N'x')"
            ),
            Row(
                    "realdb-script-drop-column-default-with-quoted-names",
                    "DropColumn は列の DEFAULT 制約を同じ表に絞って読み、列を消す前に外す",
                    DialectNames.SqlServer
                )
                .TargetFromLive(s => s["Cu]st'omer"].Columns.RemoveAll(c => c.Name == "O'ld"))
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.DropColumn))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE [Cu]]st'omer] ([id] int NOT NULL PRIMARY KEY, "
                                + "[O'ld] int NOT NULL CONSTRAINT [DF_cust_old] DEFAULT ((1)));"
                        )
                        .Expect(e =>
                        {
                            e.Script.Contains(
                                "*FROM sys.default_constraints dc*",
                                "*WHERE dc.parent_object_id = OBJECT_ID(N'[Cu]]st''omer]') AND c.name = N'O''ld';*",
                                "*SET @sql = N'ALTER TABLE [Cu]]st''omer] DROP CONSTRAINT ' + QUOTENAME(@df);*",
                                "*ALTER TABLE [Cu]]st'omer] DROP COLUMN [O'ld];*"
                            );
                            e.Script.Absent("*ADD CONSTRAINT*");
                            e.Outcome.Exact("committed");
                            e.After.Single("col Cu]st'omer.id *");
                            e.After.Absent("col Cu]st'omer.O'ld *");
                        })
                ),
        ];

    /// <summary>
    /// shops(id 主キー・code・region) と、名前つき単一列の一意制約 UQ_shops_code (code) と、名前なしの複合の一意制約
    /// (region, code)（宣言の並びは列の並びと逆）
    /// </summary>
    private static void Shops(ExpectationSchema s)
    {
        s.Table(
            "shops",
            ExpectationSchema.Pk("id", "int"),
            ExpectationSchema.NotNull("code", "nvarchar(20)"),
            ExpectationSchema.NotNull("region", "nvarchar(10)")
        );
        s.Unique("shops", "UQ_shops_code", "code");
        s.Unique("shops", null, "region", "code");
    }

    /// <summary>
    /// DEFAULT 制約のある列の NULL 許容だけの変更（型は変わらない＝DEFAULT 制約に触れない。文字列型なら照合順序を保つ）
    /// </summary>
    private static ExpectationRow AlterNullabilityOnlyRow(
        string shape,
        string columnDefinition,
        string type,
        string probe
    ) =>
        Row(
                $"realdb-column-alter-nullability-only-{shape}",
                "AlterColumn は、照合順序も DEFAULT 制約も関わらない変更を素の ALTER COLUMN で書く",
                DialectNames.SqlServer
            )
            .TargetFromLive(s => s.Col("ra", "code").IsNullable = true)
            .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
            .OnRealDatabase(r =>
                r.LiveSql($"CREATE TABLE [ra] ([id] int NOT NULL PRIMARY KEY, {columnDefinition});")
                    .Probe("columns", "defaults")
                    .Expect(e =>
                    {
                        // 型が変わらないので DEFAULT 制約を読まない（外して付け直すと object_id が変わる）
                        e.Script.Absent("*sys.default_constraints*");

                        if (type.StartsWith("nvarchar", StringComparison.Ordinal))
                        {
                            // 文字列型は照合順序を読み直して書き添える（素の ALTER COLUMN は DB の既定へ戻す）
                            e.Script.Contains(
                                "*SELECT @coll = c.collation_name FROM sys.columns c*",
                                $"*SET @sql = N'ALTER TABLE [ra] ALTER COLUMN [code] {type}' + ISNULL(N' COLLATE ' + @coll, N'') + N' NULL';*"
                            );
                            e.Script.Absent("ALTER TABLE [ra] ALTER COLUMN *");
                        }
                        else
                        {
                            e.Script.Contains($"ALTER TABLE [ra] ALTER COLUMN [code] {type} NULL;");
                        }

                        e.Outcome.Exact("committed");
                        e.Probe("columns").Single($"columns dbo.ra.code {probe}");
                        e.ProbeUnchanged("defaults");
                        e.Probe("defaults").Single("defaults dbo.ra.code name=DF_ra_code *");
                    })
            );

    /// <summary>外部キーの追加と、表・列の追加と外部キーの並び</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealSqlServerScriptForeignKeyRows() =>
        [
            Row(
                    "realdb-script-add-fk-unnamed-uses-default-name",
                    "AddForeignKey は ALTER ADD CONSTRAINT FOREIGN KEY を生成する",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    s.Table("Customer", ExpectationSchema.Pk("Id", "int"));
                    s.Table(
                        "Order",
                        ExpectationSchema.Pk("Id", "int"),
                        ExpectationSchema.Nullable("Customer_Id", "int"),
                        ExpectationSchema.Nullable("CustomerRef", "int")
                    );
                })
                .TargetFromLive(s => s.Fk("Customer", "Order", null, ("Id", "CustomerRef")))
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AddForeignKey))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Script.Contains(
                            "*ALTER TABLE [Order] ADD CONSTRAINT [FK_Order_Customer]*",
                            "*FOREIGN KEY ([CustomerRef]) REFERENCES [Customer] ([Id])*"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Count("fk *", 1);
                        e.After.Single(
                            "fk Order FK_Order_Customer (CustomerRef) -> Customer(Id) *"
                        );
                    })
                ),
            Row(
                    "realdb-script-add-fk-named-with-referential-actions",
                    "AddForeignKey は制約名と参照アクションを生成する",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    s.Table("Customer", ExpectationSchema.Pk("Id", "int"));
                    s.Table(
                        "Order",
                        ExpectationSchema.Pk("Id", "int"),
                        ExpectationSchema.Nullable("CustomerId", "int")
                    );
                })
                .TargetFromLive(s =>
                    s.Fk(
                        "Customer",
                        "Order",
                        "FK_Order_Customer_Custom",
                        ForeignKeyReferentialAction.Cascade,
                        ForeignKeyReferentialAction.SetDefault,
                        ("Id", "CustomerId")
                    )
                )
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AddForeignKey))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Script.Contains(
                            "*CONSTRAINT [FK_Order_Customer_Custom]*",
                            "*ON DELETE CASCADE*",
                            "*ON UPDATE SET DEFAULT*"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Single(
                            "fk Order FK_Order_Customer_Custom (CustomerId) -> Customer(Id) del=Cascade upd=SetDefault"
                        );
                    })
                ),
            Row(
                    "realdb-script-add-table-and-column-before-fk",
                    "実行順序: AddTable → AddColumn → AddForeignKey",
                    DialectNames.SqlServer
                )
                .Live(s => s.Table("Order", ExpectationSchema.Pk("Id", "int")))
                .TargetFromLive(s =>
                {
                    s.Table("Customer", ExpectationSchema.Pk("Id", "int"));
                    s["Order"].Columns.Add(ExpectationSchema.Nullable("Customer_Id", "int"));
                    s.Fk("Customer", "Order", null, ("Id", "Customer_Id"));
                })
                .Select(items =>
                    RealScenarios.SelectKinds(
                        items,
                        SchemaDiffKind.AddTable,
                        SchemaDiffKind.AddColumn,
                        SchemaDiffKind.AddForeignKey
                    )
                )
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Script.Contains(
                            "*CREATE TABLE [Customer]*",
                            "*ALTER TABLE [Order] ADD [Customer_Id] int NULL;*",
                            "*ALTER TABLE [Order] ADD CONSTRAINT [FK_Order_Customer] FOREIGN KEY ([Customer_Id])*"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Single(
                            "table Customer",
                            "col Order.Customer_Id int null",
                            "fk Order FK_Order_Customer (Customer_Id) -> Customer(Id) *"
                        );
                    })
                ),
        ];

    /// <summary>説明（MS_Description）の追加・更新・削除と、修飾の有無で変わるスキーマの指し方</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealSqlServerScriptDescriptionRows() =>
        [
            Row(
                    "realdb-script-table-description-added",
                    "SetTableDescription は sp_addextendedproperty / sp_updateextendedproperty を出力する",
                    DialectNames.SqlServer
                )
                .Live(s => s.Table("Customer", ExpectationSchema.Pk("Id", "int")))
                .TargetFromLive(s => s["Customer"].Description = "顧客マスタ")
                .Select(items =>
                    RealScenarios.SelectKinds(items, SchemaDiffKind.SetTableDescription)
                )
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Script.Contains(
                            "*sp_addextendedproperty*",
                            "*sp_updateextendedproperty*",
                            "*MS_Description*",
                            "*@level1type=N'TABLE'*",
                            "*@level1name=N'Customer'*",
                            "*N'顧客マスタ'*"
                        );
                        e.Script.Absent("*@level2type*");
                        e.Outcome.Exact("committed");
                        e.After.Single("desc Customer \"顧客マスタ\"");
                    })
                ),
            Row(
                    "realdb-script-column-description-updated",
                    "SetColumnDescription は @level2type=COLUMN を含む",
                    DialectNames.SqlServer
                )
                .Live(s =>
                    s.Table(
                            "Customer",
                            ExpectationSchema.Pk("Id", "int"),
                            ExpectationSchema.Nullable("Name", "nvarchar(50)")
                        )
                        .Columns[1]
                        .Description = "旧"
                )
                .TargetFromLive(s => s.Col("Customer", "Name").Description = "顧客名")
                .Select(items =>
                    RealScenarios.SelectKinds(items, SchemaDiffKind.SetColumnDescription)
                )
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Before.Single("desc Customer.Name \"旧\"");
                        e.Script.Contains(
                            "*@level2type=N'COLUMN'*",
                            "*@level2name=N'Name'*",
                            "*N'顧客名'*"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Single("desc Customer.Name \"顧客名\"");
                        e.After.Absent("desc Customer.Name \"旧\"");
                    })
                ),
            Row(
                    "realdb-script-empty-table-description-dropped",
                    "新値が空ならば sp_dropextendedproperty が出力される",
                    DialectNames.SqlServer
                )
                .Live(s =>
                    s.Table("Customer", ExpectationSchema.Pk("Id", "int")).Description = "古い"
                )
                .TargetFromLive(s => s["Customer"].Description = "")
                .Select(items =>
                    RealScenarios.SelectKinds(items, SchemaDiffKind.SetTableDescription)
                )
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Before.Single("desc Customer \"古い\"");
                        e.Script.Contains("*sp_dropextendedproperty*");
                        e.Script.Absent("*sp_addextendedproperty*");
                        e.Outcome.Exact("committed");
                        e.After.Absent("desc *");
                    })
                ),
            Row(
                    "realdb-script-description-unqualified-and-qualified-tables",
                    "修飾の無い表の説明は既定スキーマ（SCHEMA_NAME()）を指し、修飾つきはスキーマ名を書く",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    s.Table(
                        "Customer",
                        ExpectationSchema.Pk("Id", "int"),
                        ExpectationSchema.Nullable("Name", "nvarchar(50)")
                    );
                    s.Table("sales.Order", ExpectationSchema.Pk("Id", "int"));
                })
                .TargetFromLive(s =>
                {
                    s.Col("Customer", "Name").Description = "顧客名";
                    s["sales.Order"].Description = "注文";
                })
                .Select(items =>
                    RealScenarios.SelectKinds(
                        items,
                        SchemaDiffKind.SetColumnDescription,
                        SchemaDiffKind.SetTableDescription
                    )
                )
                .OnRealDatabase(r =>
                    r.Prepare("IF SCHEMA_ID(N'sales') IS NULL EXEC(N'CREATE SCHEMA [sales]');")
                        .Probe("descriptions")
                        .Expect(e =>
                        {
                            e.Script.Contains(
                                "DECLARE @default_schema sysname = SCHEMA_NAME();",
                                "*@level0name=@default_schema*",
                                "*OBJECT_ID(N'[Customer]')*",
                                "*@level0name=N'sales'*",
                                "*OBJECT_ID(N'sales.Order')*"
                            );
                            e.Script.Count("DECLARE @default_schema *", 1);
                            e.Outcome.Exact("committed");
                            e.Probe("descriptions")
                                .Exact(
                                    "descriptions dbo.Customer.Name value=顧客名",
                                    "descriptions sales.Order value=注文"
                                );
                            e.After.Single(
                                "desc Customer.Name \"顧客名\"",
                                "desc sales.Order \"注文\""
                            );
                        })
                ),
        ];

    /// <summary>主キーの付け替え・付与・解除と一意制約の追加・削除の文</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealSqlServerScriptKeyRows() =>
        [
            Row(
                    "realdb-script-pk-single-to-composite",
                    "AlterPrimaryKey は旧 PK の動的 DROP と複合 PK の ADD を生成する",
                    DialectNames.SqlServer
                )
                .TargetFromLive(s =>
                {
                    s.Col("Order", "LineNo").IsPrimaryKey = true;
                    s.PrimaryKeyOrder("Order", "OrderId", "LineNo");
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE [Order] ([OrderId] int NOT NULL, [LineNo] int NOT NULL, "
                                + "[Memo] nvarchar(50) NULL, CONSTRAINT [PK_Order_Old] PRIMARY KEY ([OrderId]));"
                        )
                        .Probe("keys")
                        .Expect(e =>
                        {
                            e.Script.Contains(
                                "DECLARE @pk sysname;",
                                "*FROM sys.key_constraints kc*",
                                "*WHERE kc.type = 'PK' AND kc.parent_object_id = OBJECT_ID(N'[Order]');*",
                                "*DECLARE @sql nvarchar(max) = N'ALTER TABLE [Order] DROP CONSTRAINT ' + QUOTENAME(@pk);*",
                                "*IF @pk IS NOT NULL EXEC sys.sp_executesql @sql;*",
                                "*ALTER TABLE [Order] ADD CONSTRAINT [PK_Order] PRIMARY KEY ([OrderId], [LineNo]);*"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Single("pk Order (OrderId,LineNo)");
                            e.Probe("keys").Exact("keys dbo.Order kind=PK name=PK_Order");
                        })
                ),
            Row(
                    "realdb-script-pk-add-only-guarded-drop",
                    "AlterPrimaryKey は PK 付与のみでも DROP を no-op 形で出す",
                    DialectNames.SqlServer
                )
                .TargetFromLive(s => s.Col("Customer", "Id").IsPrimaryKey = true)
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE [Customer] ([Id] int NOT NULL, [Memo] nvarchar(50) NULL);"
                        )
                        .Probe("keys")
                        .Expect(e =>
                        {
                            e.Before.Absent("pk *");
                            e.Script.Contains(
                                "*IF @pk IS NOT NULL EXEC sys.sp_executesql @sql;*",
                                "*ALTER TABLE [Customer] ADD CONSTRAINT [PK_Customer] PRIMARY KEY ([Id]);*"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Single("pk Customer (Id)");
                            e.Probe("keys").Exact("keys dbo.Customer kind=PK name=PK_Customer");
                        })
                ),
            Row(
                    "realdb-script-pk-drop-only-omits-add",
                    "AlterPrimaryKey は PK 解除のみなら ADD を出さない",
                    DialectNames.SqlServer
                )
                .TargetFromLive(s => s.Col("Customer", "Id").IsPrimaryKey = false)
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE [Customer] ([Id] int NOT NULL, [Memo] nvarchar(50) NULL, "
                                + "CONSTRAINT [PK_Customer] PRIMARY KEY ([Id]));"
                        )
                        .Probe("keys")
                        .Expect(e =>
                        {
                            e.Script.Contains("*IF @pk IS NOT NULL EXEC sys.sp_executesql @sql;*");
                            e.Script.Absent("*ADD CONSTRAINT*", "*PRIMARY KEY (*");
                            e.Outcome.Exact("committed");
                            e.After.Absent("pk *");
                            e.Probe("keys").Empty();
                        })
                ),
            Row(
                    "realdb-script-pk-change-splits-around-alter-column",
                    "AlterPrimaryKey は AlterColumn を挟んで DROP → ALTER → ADD の順に出る",
                    DialectNames.SqlServer
                )
                .TargetFromLive(s =>
                {
                    var oldId = s.Col("Order", "OldId");
                    oldId.IsPrimaryKey = false;
                    oldId.IsNullable = true;
                    s.Col("Order", "OrderId").IsPrimaryKey = true;
                    s["Order"].PrimaryKeyColumnIds.Clear();
                })
                .Select(items =>
                    RealScenarios.SelectKinds(
                        items,
                        SchemaDiffKind.AlterPrimaryKey,
                        SchemaDiffKind.AlterColumn
                    )
                )
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE [Order] ([OldId] int NOT NULL, [OrderId] int NOT NULL, "
                                + "[Memo] nvarchar(50) NULL, CONSTRAINT [PK_Order_Old] PRIMARY KEY ([OldId]));"
                        )
                        .Expect(e =>
                        {
                            // 主キーが残ったままの ALTER COLUMN（NULL 許容化）は断られる＝コミットが並びを示す
                            e.Script.Contains(
                                "DECLARE @pk sysname;",
                                "ALTER TABLE [Order] ALTER COLUMN [OldId] int NULL;",
                                "*ALTER TABLE [Order] ADD CONSTRAINT [PK_Order] PRIMARY KEY ([OrderId]);*"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Single("pk Order (OrderId)", "col Order.OldId int null");
                        })
                ),
            Row(
                    "realdb-script-pk-change-table-name-with-quote",
                    "AlterPrimaryKey の動的 SQL はテーブル名の ' を二重化する",
                    DialectNames.SqlServer
                )
                .TargetFromLive(s => RealScenarios.MovePrimaryKeyToCode(s, "o'rders"))
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.LiveSql(
                            "CREATE TABLE [o'rders] ([id] int NOT NULL, [code] int NOT NULL, "
                                + "CONSTRAINT [PK_o_old] PRIMARY KEY ([id]));"
                        )
                        .Probe("keys")
                        .Expect(e =>
                        {
                            e.Script.Contains(
                                "*DECLARE @sql nvarchar(max) = N'ALTER TABLE [o''rders] DROP CONSTRAINT ' + QUOTENAME(@pk);*",
                                "*OBJECT_ID(N'[o''rders]')*"
                            );
                            e.Script.Absent(
                                "*N'ALTER TABLE [o'rders]*",
                                "*OBJECT_ID(N'[o'rders]')*"
                            );
                            e.Outcome.Exact("committed");
                            e.After.Single("pk o'rders (code)");
                            e.Probe("keys").Exact("keys dbo.o'rders kind=PK name=PK_o'rders");
                        })
                ),
            Row(
                    "realdb-script-pk-change-qualified-table-scoped-by-object-id",
                    "AlterPrimaryKey の逆引きは OBJECT_ID でスキーマ込みの 1 テーブルに絞る",
                    DialectNames.SqlServer
                )
                .TargetFromLive(s => RealScenarios.MovePrimaryKeyToCode(s, "sales.Orders"))
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterPrimaryKey))
                .OnRealDatabase(r =>
                    r.Prepare("IF SCHEMA_ID(N'sales') IS NULL EXEC(N'CREATE SCHEMA [sales]');")
                        .LiveSql(
                            "CREATE TABLE [dbo].[Orders] ([id] int NOT NULL, [code] int NOT NULL, "
                                + "CONSTRAINT [PK_dbo_Orders] PRIMARY KEY ([id]));"
                                + "CREATE TABLE [sales].[Orders] ([id] int NOT NULL, [code] int NOT NULL, "
                                + "CONSTRAINT [PK_sales_Orders_Old] PRIMARY KEY ([id]));"
                        )
                        .Probe("keys")
                        .Expect(e =>
                        {
                            e.Diff.Single("AlterPrimaryKey *");
                            e.Script.Contains(
                                "*WHERE kc.type = 'PK' AND kc.parent_object_id = OBJECT_ID(N'[sales].[Orders]');*"
                            );
                            e.Outcome.Exact("committed");
                            e.Probe("keys")
                                .Exact(
                                    "keys dbo.Orders kind=PK name=PK_dbo_Orders",
                                    "keys sales.Orders kind=PK name=PK_sales_Orders"
                                );
                        })
                ),
            Row(
                    "realdb-script-unique-add-synthesized-name",
                    "AddUniqueConstraint は ADD CONSTRAINT UNIQUE を生成する",
                    DialectNames.SqlServer
                )
                .Live(s =>
                    s.Table(
                        "Customer",
                        ExpectationSchema.Pk("Id", "int"),
                        ExpectationSchema.NotNull("Code", "nvarchar(10)"),
                        ExpectationSchema.NotNull("Kind", "nvarchar(10)")
                    )
                )
                .TargetFromLive(s => s.Unique("Customer", null, "Code", "Kind"))
                .Select(items =>
                    RealScenarios.SelectKinds(items, SchemaDiffKind.AddUniqueConstraint)
                )
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Script.Contains(
                            "*ALTER TABLE [Customer] ADD CONSTRAINT [UQ_Customer_Code_Kind] UNIQUE ([Code], [Kind]);*"
                        );
                        e.Outcome.Exact("committed");
                        e.After.Single("uq Customer UQ_Customer_Code_Kind (Code,Kind)");
                    })
                ),
            Row(
                    "realdb-script-unique-drop-by-live-name",
                    "DropUniqueConstraint は実名で DROP CONSTRAINT を生成する",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    s.Table(
                        "Customer",
                        ExpectationSchema.Pk("Id", "int"),
                        ExpectationSchema.NotNull("Code", "nvarchar(10)")
                    );
                    s.Unique("Customer", "UQ_Legacy", "Code");
                })
                .TargetFromLive(s => s["Customer"].UniqueConstraints.Clear())
                .Select(items =>
                    RealScenarios.SelectKinds(items, SchemaDiffKind.DropUniqueConstraint)
                )
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Before.Single("uq Customer UQ_Legacy (Code)");
                        e.Script.Contains("*ALTER TABLE [Customer] DROP CONSTRAINT [UQ_Legacy];*");
                        e.Outcome.Exact("committed");
                        e.After.Absent("uq *");
                    })
                ),
        ];
}

/// <summary>実 DB の段の行が使う、同じ形の図と選び方</summary>
internal static class RealScenarios
{
    /// <summary>
    /// 親 sd_parent(id) と子 sd_child(id, parent_id) と parent_id → id の外部キー（名前 <paramref name="constraintName"/>）を足す
    /// （主キー id は NOT NULL の int・parent_id は NOT NULL の int）
    /// </summary>
    public static void ParentChild(
        ExpectationSchema s,
        string? constraintName,
        string parent = "sd_parent",
        string child = "sd_child"
    )
    {
        s.Table(parent, ExpectationSchema.Pk("id", "int"));
        s.Table(
            child,
            ExpectationSchema.Pk("id", "int"),
            ExpectationSchema.NotNull("parent_id", "int")
        );
        s.Fk(parent, child, constraintName, ("id", "parent_id"));
    }

    /// <summary><see cref="ParentChild"/> の親子に 1 行ずつ入れる（親 1・子 1 → 1）</summary>
    public const string ParentChildRowsSql =
        "INSERT INTO sd_parent (id) VALUES (1); INSERT INTO sd_child (id, parent_id) VALUES (1, 1);";

    /// <summary>
    /// 名前の無い <see cref="ParentChild"/> に、子の NULL 許容の列 backup_parent_id と、それが親の id を参照する名前の無い外部キーを足す
    /// （同じ表の組に名前の無い外部キーが 2 本）
    /// </summary>
    public static void UnnamedSamePair(ExpectationSchema s)
    {
        ParentChild(s, null);
        s["sd_child"].Columns.Add(ExpectationSchema.Nullable("backup_parent_id", "int"));
        s.Fk("sd_parent", "sd_child", null, ("id", "backup_parent_id"));
    }

    /// <summary>名前の無い <see cref="ParentChild"/> の先頭に、同じ組の名前の無い多対多を入れる</summary>
    public static void ManyToManyFirst(ExpectationSchema s)
    {
        ParentChild(s, null);
        s.ManyToMany("sd_parent", "sd_child", index: 0);
    }

    /// <summary>
    /// 複合主キー ko_parent(a, b) を、ko_child(id, x, y) の外部キー FK_ko_child が主キーと逆の並び (b → y, a → x) で参照する図
    /// </summary>
    public static void CompositeInOtherOrder(ExpectationSchema s)
    {
        s.Table("ko_parent", ExpectationSchema.Pk("a", "int"), ExpectationSchema.Pk("b", "int"));
        s.Table(
            "ko_child",
            ExpectationSchema.Pk("id", "int"),
            ExpectationSchema.NotNull("x", "int"),
            ExpectationSchema.NotNull("y", "int")
        );
        s.Fk("ko_parent", "ko_child", "FK_ko_child", ("b", "y"), ("a", "x"));
    }

    /// <summary>
    /// sd_parent(id)・sd_other(id) ← sd_child(id, parent_id NULL, other_id NULL)。fk_sd_parent は ON DELETE SET NULL、
    /// fk_sd_other は NO ACTION
    /// </summary>
    public const string SetNullChildSql =
        "CREATE TABLE [sd_parent] ([id] int NOT NULL CONSTRAINT [PK_sd_parent] PRIMARY KEY);"
        + "CREATE TABLE [sd_other] ([id] int NOT NULL CONSTRAINT [PK_sd_other] PRIMARY KEY);"
        + "CREATE TABLE [sd_child] ([id] int NOT NULL CONSTRAINT [PK_sd_child] PRIMARY KEY, "
        + "[parent_id] int NULL, [other_id] int NULL, "
        + "CONSTRAINT [fk_sd_parent] FOREIGN KEY ([parent_id]) REFERENCES [sd_parent] ([id]) ON DELETE SET NULL, "
        + "CONSTRAINT [fk_sd_other] FOREIGN KEY ([other_id]) REFERENCES [sd_other] ([id]));";

    /// <summary>複合主キー sd_pair(a, b) を sd_ref(id, ra, rb) の外部キー fk_sd_ref が (a, b) の並びで参照する</summary>
    public const string PairRefSql =
        "CREATE TABLE [sd_pair] ([a] int NOT NULL, [b] int NOT NULL, CONSTRAINT [PK_sd_pair] PRIMARY KEY ([a], [b]));"
        + "CREATE TABLE [sd_ref] ([id] int NOT NULL, [ra] int NOT NULL, [rb] int NOT NULL, "
        + "CONSTRAINT [PK_sd_ref] PRIMARY KEY ([id]), "
        + "CONSTRAINT [fk_sd_ref] FOREIGN KEY ([ra], [rb]) REFERENCES [sd_pair] ([a], [b]));";

    /// <summary>
    /// 複合主キー cfk_parent(a, b) と cfk_child(id, a_ref, b_ref)（<paramref name="withForeignKey"/> が真なら
    /// FK_cfk_child_cfk_parent (a_ref, b_ref) → (a, b) も）
    /// </summary>
    public static string CompositeParentChildSql(bool withForeignKey) =>
        "CREATE TABLE [cfk_parent] ([a] int NOT NULL, [b] int NOT NULL, "
        + "CONSTRAINT [PK_cfk_parent] PRIMARY KEY ([a], [b]));"
        + "CREATE TABLE [cfk_child] ([id] int NOT NULL, [a_ref] int NOT NULL, "
        + "[b_ref] int NOT NULL, CONSTRAINT [PK_cfk_child] PRIMARY KEY ([id])"
        + (
            withForeignKey
                ? ", CONSTRAINT [FK_cfk_child_cfk_parent] FOREIGN KEY ([a_ref], [b_ref]) REFERENCES [cfk_parent] ([a], [b])"
                : ""
        )
        + ");";

    /// <summary>表の主キーを id から code へ付け替える（主キーの順序の宣言は消す）</summary>
    public static void MovePrimaryKeyToCode(ExpectationSchema s, string table)
    {
        s.Col(table, "id").IsPrimaryKey = false;
        s.Col(table, "code").IsPrimaryKey = true;
        s[table].PrimaryKeyColumnIds.Clear();
    }

    /// <summary>種類で選ぶ（ほかの項目は選ばない）</summary>
    public static void SelectKinds(
        IReadOnlyList<SchemaDiffItem> items,
        params SchemaDiffKind[] kinds
    )
    {
        foreach (var item in items)
        {
            item.IsSelected = kinds.Contains(item.Kind);
        }
    }

    /// <summary>全部の項目を選ぶ</summary>
    public static void SelectAll(IReadOnlyList<SchemaDiffItem> items)
    {
        foreach (var item in items)
        {
            item.IsSelected = true;
        }
    }

    /// <summary>既定の選択に加えて、この種類の項目も選ぶ</summary>
    public static void AlsoSelect(
        IReadOnlyList<SchemaDiffItem> items,
        params SchemaDiffKind[] kinds
    )
    {
        foreach (var item in items.Where(i => kinds.Contains(i.Kind)))
        {
            item.IsSelected = true;
        }
    }
}
