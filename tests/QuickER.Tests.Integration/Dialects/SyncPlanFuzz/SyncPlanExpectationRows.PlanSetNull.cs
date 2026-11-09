using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 計画の段の期待の行（参照アクション SET NULL の外部キーの子の列が同期後に NOT NULL になるとき：作れない方言＝SQL Server・MySQL は
/// 選んだ追加・作り直しを外し、DB にある外部キーを外し直さない。作れる方言は計画をそのままにして警告だけを出す）
/// </summary>
/// <remarks>
/// 図は親 parent(id)・other(id) ← 子 child(id, parent_id, other_id)。外部キーは FK_child_parent（子の列 parent_id）と
/// FK_child_other（子の列 other_id・NO ACTION）。DB 側の図と目標の図をそれぞれ組み、差分を計算して選んだ項目から計画を作る。
/// </remarks>
internal static partial class SyncPlanExpectationRows
{
    /// <summary>FK_child_parent の子の列が NOT NULL になることを名指しする警告</summary>
    private const string SetNullParentWarning =
        "SetNullOnNotNullColumn(child): FK_child_parent (parent_id)";

    /// <summary>計画の外部キーの追加に FK_child_parent がある（逐次 DDL の方言）</summary>
    private const string SetNullParentAdd =
        "AddForeignKey > AddForeignKey child* fk=FK_child_parent sel=*";

    /// <summary>SQLite の再構築で子の表に張る、parent を参照する外部キー</summary>
    private const string SetNullParentRebuildForeignKey = "child fk * -> parent(*";

    /// <summary>同期後の NULL 許容の決め方の形（形, 作れない方言で外すか）</summary>
    private static (string Shape, bool Excluded)[] SetNullPostSyncNullabilityShapes =>
        [
            ("alter-to-not-null", true),
            ("alter-to-nullable", false),
            ("unselected-alter-to-not-null", false),
            ("new-not-null-column", true),
            ("primary-key-change", true),
            ("alter-to-key-column-flagged-nullable", true),
        ];

    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanSetNullRows() =>
        [
            .. new[] { true, false }.Select(onDelete =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"plan-set-null-selected-add-on-not-null-child-{(onDelete ? "on-delete" : "on-update")}",
                            "追加: NOT NULL の子の列への SET NULL は、作れない方言で計画から外し、5 方言とも名指しする",
                            onDelete
                                ? AllDialects
                                :
                                [
                                    DialectNames.SqlServer,
                                    DialectNames.PostgreSql,
                                    DialectNames.MySql,
                                    DialectNames.Sqlite,
                                ]
                        )
                        .Live(s =>
                            SetNullSchema(
                                s,
                                new SetNullShape(ChildNullable: false, HasParentForeignKey: false)
                            )
                        )
                        .Target(s =>
                            SetNullSchema(
                                s,
                                new SetNullShape(
                                    ChildNullable: false,
                                    OnDelete: SetNullIf(onDelete),
                                    OnUpdate: SetNullIf(!onDelete)
                                )
                            )
                        )
                        .Select(SelectSelectable(_ => true))
                        .Expect(e =>
                        {
                            e.Warnings.Exact(SetNullParentWarning);
                            ExpectSetNullParentForeignKey(e, onDelete ? AllDialects : null);
                        })
            ),
            Row(
                    "plan-set-null-selected-add-oracle-on-update-only-is-not-named",
                    "追加: Oracle の ON UPDATE だけの SET NULL は名指しせずに足す",
                    DialectNames.Oracle
                )
                .Live(s =>
                    SetNullSchema(
                        s,
                        new SetNullShape(ChildNullable: false, HasParentForeignKey: false)
                    )
                )
                .Target(s =>
                    SetNullSchema(
                        s,
                        new SetNullShape(
                            ChildNullable: false,
                            OnDelete: ForeignKeyReferentialAction.NoAction,
                            OnUpdate: ForeignKeyReferentialAction.SetNull
                        )
                    )
                )
                .Select(SelectSelectable(_ => true))
                .Expect(e => e.Warnings.Empty().Items.Contains(SetNullParentAdd)),
            Row(
                    "plan-set-null-selected-recreate-on-not-null-child-is-excluded-with-its-drop",
                    "作り直し: NOT NULL の子の列への SET NULL への作り直しは、作れない方言で削除ごと外す",
                    AllDialects
                )
                .Live(s =>
                    SetNullSchema(
                        s,
                        new SetNullShape(
                            ChildNullable: false,
                            OnDelete: ForeignKeyReferentialAction.NoAction
                        )
                    )
                )
                .Target(s => SetNullSchema(s, new SetNullShape(ChildNullable: false)))
                .Select(SelectSelectable(_ => true))
                .Expect(e =>
                {
                    e.Warnings.Exact(SetNullParentWarning);

                    // 作れない方言は作り直しを削除ごと外す
                    foreach (var dialect in new[] { DialectNames.SqlServer, DialectNames.MySql })
                    {
                        e.For(dialect, d => d.Sections.Empty());
                    }

                    e.For(
                        DialectNames.Sqlite,
                        d =>
                            d.Rebuilds.Count(SetNullParentRebuildForeignKey, 1)
                                .Rebuilds.Single("child fk * -> parent(*) del=SetNull *")
                    );

                    foreach (var dialect in new[] { DialectNames.PostgreSql, DialectNames.Oracle })
                    {
                        e.For(
                            dialect,
                            d => d.Sections.Exact("DropForeignKey: *", "AddForeignKey: *")
                        );
                    }
                }),
            .. SetNullPostSyncNullabilityShapes.Select(shape =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"plan-set-null-post-sync-nullability-{shape.Shape}",
                            "同期後の NULL 許容: 列の変更・live・図・主キーの変更の順で決める",
                            DialectNames.SqlServer,
                            DialectNames.MySql
                        )
                        .Live(s => SetNullSchema(s, SetNullPostSyncLiveShape(shape.Shape)))
                        .Target(s => SetNullSchema(s, SetNullPostSyncTargetShape(shape.Shape)))
                        .Select(
                            SelectSelectable(
                                shape.Shape
                                    is "unselected-alter-to-not-null"
                                        or "primary-key-change"
                                    ? i => i.Kind != SchemaDiffKind.AlterColumn
                                : shape.Shape == "alter-to-key-column-flagged-nullable"
                                    ? i => i.Kind != SchemaDiffKind.AlterPrimaryKey
                                : _ => true
                            )
                        )
                        .Expect(e =>
                        {
                            if (shape.Excluded)
                            {
                                e.Items.Absent(SetNullParentAdd)
                                    .Rebuilds.Absent(SetNullParentRebuildForeignKey)
                                    .Warnings.Contains(SetNullParentWarning);
                            }
                            else
                            {
                                e.Items.Contains(SetNullParentAdd)
                                    .Warnings.Absent("SetNullOnNotNullColumn*");
                            }
                        })
            ),
            Row(
                    "plan-set-null-alter-child-to-not-null-names-live-fk-and-does-not-rebuild-it",
                    "暗黙の付け直し: SET NULL の子の列を NOT NULL にする列の変更は名指しし、その外部キーを外し直さない",
                    AllDialects
                )
                .Live(s => SetNullSchema(s, new SetNullShape(ChildNullable: true)))
                .Target(s => SetNullSchema(s, new SetNullShape(ChildNullable: false)))
                .Select(SelectSelectable(_ => true))
                .Expect(e =>
                {
                    e.Warnings.Exact(SetNullParentWarning);
                    e.For(
                        DialectNames.Sqlite,
                        d =>
                            d.Rebuilds.Count("* cols *", 1)
                                .Rebuilds.Count(SetNullParentRebuildForeignKey, 1)
                                .Rebuilds.Single("child fk * -> parent(*) del=SetNull *")
                    );

                    foreach (
                        var dialect in AllDialects.Where(d => d != DialectNames.Sqlite).ToArray()
                    )
                    {
                        e.For(dialect, d => d.Sections.Exact("AlterColumn: *"));
                    }
                }),
            Row(
                    "plan-set-null-alter-child-to-not-null-with-other-rebuild",
                    "暗黙の付け直し: SQL Server はその外部キーだけ、MySQL はどの外部キーも外し直さない",
                    DialectNames.SqlServer,
                    DialectNames.MySql
                )
                .Live(s => SetNullSchema(s, new SetNullShape(ChildNullable: true)))
                .Target(s =>
                    SetNullSchema(s, new SetNullShape(ChildNullable: false, OtherKeyType: "bigint"))
                )
                .Select(SelectSelectable(_ => true))
                .Expect(e =>
                    e.Warnings.Count("SetNullOnNotNullColumn*", 1)
                        .Warnings.Single(SetNullParentWarning)
                        // DDL がトランザクションで巻き戻る SQL Server はほかの外部キーだけを外し直す
                        .For(
                            DialectNames.SqlServer,
                            d =>
                                d.Items.Count("DropForeignKey > *", 1)
                                    .Items.Single("DropForeignKey > * fk=FK_child_other sel=*")
                                    .Items.Count("AddForeignKey > *", 1)
                                    .Items.Single("AddForeignKey > * fk=FK_child_other sel=*")
                        )
                        // DDL が暗黙コミットされる MySQL はどの外部キーも外し直さない
                        .For(
                            DialectNames.MySql,
                            d => d.Items.Absent("DropForeignKey > *", "AddForeignKey > *")
                        )
                ),
            Row(
                    "plan-set-null-pk-change-making-child-not-null-names-and-does-not-rebuild",
                    "暗黙の付け直し: 主キーの変更で子の列が NOT NULL になる形も名指しし、外し直さない",
                    AllDialects
                )
                .Live(s => SetNullSchema(s, new SetNullShape(ChildNullable: true)))
                .Target(s => SetNullSchema(s, new SetNullShape(ParentIdInChildKey: true)))
                .Select(SelectSelectable(i => i.Kind == SchemaDiffKind.AlterPrimaryKey))
                .Expect(e =>
                {
                    e.Warnings.Count("SetNullOnNotNullColumn*", 1)
                        .Warnings.Single(SetNullParentWarning);

                    foreach (var dialect in new[] { DialectNames.SqlServer, DialectNames.MySql })
                    {
                        e.For(
                            dialect,
                            d => d.Items.Absent("AddForeignKey > *", "DropForeignKey > *")
                        );
                    }
                }),
            // 主キーの変更と列の変更で子の列を NOT NULL にする同期は、MySQL が断る列の変更（1830）を先頭へ移す。図で子の列に
            // NULL 許容の印が残っていても（主キーの列は印に依らず NOT NULL）、印の無い図と同じ計画になる
            .. new[] { false, true }.Select(flagged =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"plan-set-null-pk-change-and-alter-making-child-not-null-moves-alter-first-{(flagged ? "key-flagged-nullable" : "key-not-null")}",
                            "前寄せ: 主キーの変更で SET NULL の子の列を NOT NULL にする列の変更は、図の NULL 許容の印に依らず MySQL で先頭へ移す",
                            DialectNames.MySql
                        )
                        .Live(s => SetNullSchema(s, new SetNullShape(ChildNullable: true)))
                        .Target(s =>
                        {
                            SetNullSchema(
                                s,
                                new SetNullShape(
                                    ParentIdInChildKey: true,
                                    ParentIdFlaggedNullable: flagged
                                )
                            );
                        })
                        .Select(SelectSelectable(_ => true))
                        .Expect(e =>
                            e.Warnings.Count("SetNullOnNotNullColumn*", 1)
                                .Warnings.Single(SetNullParentWarning)
                                // 固定順は主キーの解除 → 列の変更 → 主キーの付与。断られる列の変更を先頭へ移す
                                .Sections.Exact(
                                    "AlterColumn: *",
                                    "AlterPrimaryKey/Drop: *",
                                    "AlterPrimaryKey/Add: *"
                                )
                        )
            ),
            Row(
                    "plan-set-null-not-applicable-non-set-null-fk-is-not-named",
                    "対象外: SET NULL でない外部キーと、NULL を許したままの子の列は名指ししない",
                    DialectNames.SqlServer,
                    DialectNames.MySql,
                    DialectNames.PostgreSql
                )
                .Live(s =>
                    SetNullSchema(
                        s,
                        new SetNullShape(
                            ChildNullable: true,
                            OnDelete: ForeignKeyReferentialAction.Cascade
                        )
                    )
                )
                .Target(s =>
                    SetNullSchema(
                        s,
                        new SetNullShape(
                            ChildNullable: false,
                            OnDelete: ForeignKeyReferentialAction.Cascade
                        )
                    )
                )
                .Select(SelectSelectable(_ => true))
                .Expect(e => e.Warnings.Absent("SetNullOnNotNullColumn*")),
            Row(
                    "plan-set-null-not-applicable-nullable-child-is-not-named",
                    "対象外: SET NULL でない外部キーと、NULL を許したままの子の列は名指ししない",
                    DialectNames.SqlServer,
                    DialectNames.MySql,
                    DialectNames.PostgreSql
                )
                .Live(s =>
                    SetNullSchema(
                        s,
                        new SetNullShape(ChildNullable: true, HasParentForeignKey: false)
                    )
                )
                .Target(s => SetNullSchema(s, new SetNullShape(ChildNullable: true)))
                .Select(SelectSelectable(_ => true))
                .Expect(e =>
                    e.Warnings.Absent("SetNullOnNotNullColumn*").Items.Contains(SetNullParentAdd)
                ),
        ];

    /// <summary>
    /// FK_child_parent の追加が計画にある・無いを方言ごとに書く（作れない方言＝SQL Server・MySQL は無い。逐次 DDL の方言は外部キーの
    /// 追加の項目、SQLite は子の表の再構築の外部キー）
    /// </summary>
    /// <param name="e">期待</param>
    /// <param name="dialects">行の方言（null なら Oracle を除く 4 方言）</param>
    private static void ExpectSetNullParentForeignKey(
        ExpectationBuilder e,
        IReadOnlyList<string>? dialects
    )
    {
        foreach (
            var dialect in dialects
                ??
                [
                    DialectNames.SqlServer,
                    DialectNames.PostgreSql,
                    DialectNames.MySql,
                    DialectNames.Sqlite,
                ]
        )
        {
            if (dialect is DialectNames.SqlServer or DialectNames.MySql)
            {
                e.For(
                    dialect,
                    d =>
                        d.Items.Absent(SetNullParentAdd)
                            .Rebuilds.Absent(SetNullParentRebuildForeignKey)
                );
            }
            else if (dialect == DialectNames.Sqlite)
            {
                e.For(dialect, d => d.Rebuilds.Count(SetNullParentRebuildForeignKey, 1));
            }
            else
            {
                e.For(dialect, d => d.Items.Contains(SetNullParentAdd));
            }
        }
    }

    /// <summary>同期後の NULL 許容の形の DB 側の図</summary>
    private static SetNullShape SetNullPostSyncLiveShape(string shape) =>
        shape switch
        {
            "alter-to-not-null"
            or "unselected-alter-to-not-null"
            or "primary-key-change"
            or "alter-to-key-column-flagged-nullable" => new SetNullShape(
                ChildNullable: true,
                HasParentForeignKey: false
            ),
            "alter-to-nullable" => new SetNullShape(
                ChildNullable: false,
                HasParentForeignKey: false
            ),
            "new-not-null-column" => new SetNullShape(
                HasParentColumn: false,
                HasParentForeignKey: false
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

    /// <summary>同期後の NULL 許容の形の目標の図</summary>
    private static SetNullShape SetNullPostSyncTargetShape(string shape) =>
        shape switch
        {
            "alter-to-not-null" or "unselected-alter-to-not-null" or "new-not-null-column" =>
                new SetNullShape(ChildNullable: false),
            "alter-to-nullable" => new SetNullShape(ChildNullable: true),
            "primary-key-change" => new SetNullShape(ParentIdInChildKey: true),
            // 図で parent_id を主キーへ加え NULL 許容の印を残す（主キーの変更は選ばず、列の変更だけで NOT NULL になる）
            "alter-to-key-column-flagged-nullable" => new SetNullShape(
                ParentIdInChildKey: true,
                ParentIdFlaggedNullable: true
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

    /// <summary>選べる項目のうち、条件に合うものだけを選ぶ</summary>
    private static Action<IReadOnlyList<SchemaDiffItem>> SelectSelectable(
        Func<SchemaDiffItem, bool> select
    ) =>
        items =>
        {
            foreach (var item in items)
            {
                item.IsSelected = item.IsSelectable && select(item);
            }
        };

    private static ForeignKeyReferentialAction SetNullIf(bool condition) =>
        condition ? ForeignKeyReferentialAction.SetNull : ForeignKeyReferentialAction.NoAction;

    /// <summary>parent(id)・other(id) ← child(id, parent_id, other_id) の図を足す</summary>
    private static void SetNullSchema(S s, SetNullShape shape)
    {
        s.Table("parent", S.Pk("id", "int"));
        s.Table("other", S.Pk("id", shape.OtherKeyType));
        var child = s.Table("child", S.Pk("id", "int"));

        if (shape.HasParentColumn)
        {
            child.Columns.Add(
                shape.ParentIdInChildKey ? KeyColumn(shape.ParentIdFlaggedNullable)
                : shape.ChildNullable ? S.Nullable("parent_id", "int")
                : S.NotNull("parent_id", "int")
            );
        }

        child.Columns.Add(S.Nullable("other_id", shape.OtherKeyType));
        s.Fk("other", "child", "FK_child_other", ("id", "other_id"));

        if (shape.HasParentForeignKey && shape.HasParentColumn)
        {
            s.Fk(
                "parent",
                "child",
                "FK_child_parent",
                shape.OnDelete,
                shape.OnUpdate,
                ("id", "parent_id")
            );
        }
    }

    /// <summary>子の主キーへ加える parent_id（<paramref name="flaggedNullable"/> なら NULL 許容の印を残す。主キーの列は印に依らず NOT NULL）</summary>
    private static Column KeyColumn(bool flaggedNullable)
    {
        var column = S.Pk("parent_id", "int");
        column.IsNullable = flaggedNullable;

        return column;
    }

    /// <summary>SET NULL の図の形</summary>
    /// <param name="ChildNullable">子の列 parent_id が NULL を許すか</param>
    /// <param name="OnDelete">FK_child_parent の ON DELETE</param>
    /// <param name="OnUpdate">FK_child_parent の ON UPDATE</param>
    /// <param name="HasParentForeignKey">FK_child_parent を持つか</param>
    /// <param name="HasParentColumn">子の列 parent_id を持つか</param>
    /// <param name="ParentIdInChildKey">parent_id を子の主キーへ加えるか（主キーの列は NOT NULL）</param>
    /// <param name="ParentIdFlaggedNullable">主キーへ加えた parent_id に NULL 許容の印を残すか（図のファイルの手書きでだけ作れる形）</param>
    /// <param name="OtherKeyType">other.id と child.other_id の型</param>
    private sealed record SetNullShape(
        bool ChildNullable = true,
        ForeignKeyReferentialAction OnDelete = ForeignKeyReferentialAction.SetNull,
        ForeignKeyReferentialAction OnUpdate = ForeignKeyReferentialAction.NoAction,
        bool HasParentForeignKey = true,
        bool HasParentColumn = true,
        bool ParentIdInChildKey = false,
        bool ParentIdFlaggedNullable = false,
        string OtherKeyType = "int"
    );
}
