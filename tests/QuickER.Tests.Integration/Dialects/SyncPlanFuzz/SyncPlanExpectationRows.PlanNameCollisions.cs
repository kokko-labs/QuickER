using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;
using X = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.RealCrossDialectScenarios;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 計画の段と実 DB の段の期待の行（同期後も残る DB の制約と、方言の名前の範囲で同じ名前・Oracle では同じ定義になる制約の追加を、
/// その削除を前提にする項目として外して名指しする）
/// </summary>
/// <remarks>
/// 差分の既定の選び方は、制約の追加を選び、削除を選ばない。DB に残る制約と名前か定義が重なる追加は、SQL Server（2714）・MySQL（1826）・
/// PostgreSQL（42P07）・Oracle（ORA-02264・ORA-02275）が断る。計画はその追加を外して名指しし、削除も選べば両方を流す。
/// </remarks>
internal static partial class SyncPlanExpectationRows
{
    /// <summary>名前の重なりの行（形 × 既定の選び方・全部を選ぶ）</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanNameCollisionRows() =>
        [
            .. NameCollisionRows(
                "fk-moved-to-other-child",
                "外部キーを同じ名前のまま別の子の表へ移す",
                NameCollisionScenarios.ForeignKeyMovedLive,
                NameCollisionScenarios.ForeignKeyMovedTarget,
                [DialectNames.SqlServer, DialectNames.MySql, DialectNames.Oracle],
                "AddForeignKey *",
                "SelectedItemRequiresUnselectedItem(c2b): fk_mv (c2a.fk_mv)"
            ),
            .. NameCollisionRows(
                "uq-moved-to-other-table",
                "一意制約を同じ名前のまま別の表へ移す",
                NameCollisionScenarios.UniqueMovedLive,
                NameCollisionScenarios.UniqueMovedTarget,
                [DialectNames.SqlServer, DialectNames.PostgreSql, DialectNames.Oracle],
                "AddUniqueConstraint *",
                "SelectedItemRequiresUnselectedItem(t3b): uq_mv (t3a.uq_mv)"
            ),
            .. NameCollisionRows(
                "fk-renamed-same-definition",
                "外部キーの名前だけを変える（同じ定義の外部キーを 2 本持てない方言）",
                NameCollisionScenarios.ForeignKeyRenamedLive,
                NameCollisionScenarios.ForeignKeyRenamedTarget,
                [DialectNames.Oracle],
                "AddForeignKey *",
                "SelectedItemRequiresUnselectedItem(c1): fk_new (c1.fk_old)"
            ),
            .. NameCollisionRows(
                "fk-named-like-unique-of-same-table",
                "一意制約を外して、同じ表に同じ名前の外部キーを足す（種類をまたぐ名前の範囲。MySQL は外部キーの列の一意制約の索引を使うので実際には通るが、索引の有無を判定できないので外す）",
                NameCollisionScenarios.UniqueThenForeignKeyLive,
                NameCollisionScenarios.UniqueThenForeignKeyTarget,
                [
                    DialectNames.SqlServer,
                    DialectNames.PostgreSql,
                    DialectNames.MySql,
                    DialectNames.Oracle,
                ],
                "AddForeignKey *",
                "SelectedItemRequiresUnselectedItem(c4): uq_x (c4.uq_x)"
            ),
            .. NameCollisionRows(
                "fk-named-like-unique-of-other-table",
                "一意制約を外して、別の表に同じ名前の外部キーを足す（種類をまたぐ名前の範囲がスキーマの方言だけ）",
                NameCollisionScenarios.UniqueThenOtherForeignKeyLive,
                NameCollisionScenarios.UniqueThenOtherForeignKeyTarget,
                [DialectNames.SqlServer, DialectNames.Oracle],
                "AddForeignKey *",
                "SelectedItemRequiresUnselectedItem(c5): uq_y (t5.uq_y)"
            ),
            .. NameCollisionRows(
                "new-table-keeps-unique-name-of-old-table",
                "表の名前を変え、新しい表が古い表の一意制約の名前を引き継ぐ（表の追加を外す）",
                NameCollisionScenarios.RenamedTableLive,
                NameCollisionScenarios.RenamedTableTarget,
                [DialectNames.SqlServer, DialectNames.PostgreSql, DialectNames.Oracle],
                "AddTable *",
                "SelectedItemRequiresUnselectedItem(new_t): uq_keep (old_t.uq_keep)"
            ),
            .. NameCollisionRows(
                "new-table-unique-named-like-live-fk",
                "新しい表の一意制約が、残る外部キーと同じ名前（表の追加を外す・種類をまたぐ名前の範囲がスキーマの方言だけ）",
                NameCollisionScenarios.NewTableUniqueLikeForeignKeyLive,
                NameCollisionScenarios.NewTableUniqueLikeForeignKeyTarget,
                [DialectNames.SqlServer, DialectNames.Oracle],
                "AddTable *",
                "SelectedItemRequiresUnselectedItem(nt): fk_same (c6.fk_same)"
            ),
        ];

    /// <summary>形 1 つの網の 2 行（既定の選び方・全部を選ぶ）</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> NameCollisionRows(
        string shape,
        string description,
        Action<ExpectationSchema, string> live,
        Action<ExpectationSchema, string> target,
        string[] colliding,
        string excludedItem,
        string warning
    )
    {
        yield return Row(
                $"plan-name-collision-{shape}-default-selection-excludes-where-it-collides",
                $"名前の重なり: {description}。既定の選び方では、重なる方言で追加を外して名指しする",
                AllDialects
            )
            .LiveWith(w => live(w.Live, w.Capability))
            .TargetWith(w => target(w.Target, w.Capability))
            .Expect(e =>
            {
                foreach (var dialect in AllDialects)
                {
                    e.For(
                        dialect,
                        x =>
                        {
                            if (colliding.Contains(dialect))
                            {
                                x.Excluded.Exact(excludedItem).Warnings.Single(warning);
                                return;
                            }

                            x.Excluded.Empty()
                                .Warnings.Absent("SelectedItemRequiresUnselectedItem*");
                        }
                    );
                }
            });

        yield return Row(
                $"plan-name-collision-{shape}-all-selected-applies-both",
                $"名前の重なり: {description}。削除も選べば、どの方言でも追加を外さない",
                AllDialects
            )
            .LiveWith(w => live(w.Live, w.Capability))
            .TargetWith(w => target(w.Target, w.Capability))
            .Select(X.SelectSelectable)
            .Expect(e => e.Excluded.Empty().Warnings.Absent("SelectedItemRequiresUnselectedItem*"));
    }

    /// <summary>名前の重なりの実 DB の段（既定の選び方・5 方言）</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealCrossDialectNameCollisionRows() =>
        [
            .. DroppedKeyColumnNameCollisionRows(),
            Row(
                    "realdb-name-collision-new-table-keeps-unique-name-of-old-table-default-selection",
                    "[Integration] 同期: 表の名前を変え、新しい表が古い表の一意制約の名前を引き継ぐ図を既定の選び方で流すと、名前がスキーマで一意な方言では表の追加を外して名指しし、成功する",
                    AllDialects
                )
                .LiveWith(w => NameCollisionScenarios.RenamedTableLive(w.Live, w.Capability))
                .TargetWith(w => NameCollisionScenarios.RenamedTableTarget(w.Target, w.Capability))
                .OnRealDatabase(r =>
                    X.SetupPerDialect(r, d => X.Insert(d, "old_t", ["id", "u"], "1, 1"))
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");

                            foreach (var dialect in AllDialects)
                            {
                                e.For(
                                    dialect,
                                    x =>
                                    {
                                        if (dialect is DialectNames.MySql or DialectNames.Sqlite)
                                        {
                                            // MySQL は一意制約の名前を表ごとに持ち、SQLite は名前を持たないので、表の追加は通る
                                            x.Warnings.Absent(
                                                "SelectedItemRequiresUnselectedItem*"
                                            );
                                            x.After.Count("table *", 2);
                                            return;
                                        }

                                        x.Warnings.Single(
                                            "SelectedItemRequiresUnselectedItem(new_t): uq_keep (old_t.uq_keep)"
                                        );
                                        x.After.Count("table *", 1);
                                        x.After.Single("table old_t");
                                    }
                                );
                            }
                        })
                ),
            Row(
                    "realdb-name-collision-new-table-unique-named-like-live-fk-default-selection",
                    "[Integration] 同期: 新しい表の一意制約が残る外部キーと同じ名前の図を既定の選び方で流すと、制約名がスキーマで 1 つの範囲の方言では表の追加を外して名指しし、成功する",
                    AllDialects
                )
                .LiveWith(w =>
                    NameCollisionScenarios.NewTableUniqueLikeForeignKeyLive(w.Live, w.Capability)
                )
                .TargetWith(w =>
                    NameCollisionScenarios.NewTableUniqueLikeForeignKeyTarget(
                        w.Target,
                        w.Capability
                    )
                )
                .OnRealDatabase(r =>
                    X.SetupPerDialect(
                            r,
                            d =>
                                X.Insert(d, "p6", ["id"], "1")
                                + "\n"
                                + X.Insert(d, "c6", ["id", "pid"], "1, 1")
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");

                            foreach (var dialect in AllDialects)
                            {
                                e.For(
                                    dialect,
                                    x =>
                                    {
                                        if (
                                            dialect is DialectNames.SqlServer or DialectNames.Oracle
                                        )
                                        {
                                            x.Warnings.Single(
                                                "SelectedItemRequiresUnselectedItem(nt): fk_same (c6.fk_same)"
                                            );
                                            x.After.Count("table *", 2);
                                            return;
                                        }

                                        x.Warnings.Absent("SelectedItemRequiresUnselectedItem*");
                                        x.After.Count("table *", 3);
                                    }
                                );
                            }
                        })
                ),
            Row(
                    "realdb-name-collision-fk-moved-to-other-child-default-selection",
                    "[Integration] 同期: 外部キーを同じ名前のまま別の子の表へ移した図を既定の選び方で流すと、名前がスキーマで一意な方言では追加を外して名指しし、成功する",
                    AllDialects
                )
                .LiveWith(w => NameCollisionScenarios.ForeignKeyMovedLive(w.Live, w.Capability))
                .TargetWith(w =>
                    NameCollisionScenarios.ForeignKeyMovedTarget(w.Target, w.Capability)
                )
                .OnRealDatabase(r =>
                    X.SetupPerDialect(
                            r,
                            d =>
                                string.Join(
                                    "\n",
                                    X.Insert(d, "p2", ["id"], "1"),
                                    X.Insert(d, "c2a", ["id", "pid"], "1, 1"),
                                    X.Insert(d, "c2b", ["id", "pid"], "1, 1")
                                )
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");

                            foreach (var dialect in AllDialects)
                            {
                                e.For(
                                    dialect,
                                    x =>
                                    {
                                        if (
                                            dialect
                                            is DialectNames.PostgreSql
                                                or DialectNames.Sqlite
                                        )
                                        {
                                            // PostgreSQL は外部キーの名前を表ごとに持ち、SQLite は名前を持たないので、追加は通る
                                            x.Warnings.Absent(
                                                "SelectedItemRequiresUnselectedItem*"
                                            );
                                            x.After.Count("fk *", 2);
                                            return;
                                        }

                                        x.Warnings.Single(
                                            "SelectedItemRequiresUnselectedItem(c2b): fk_mv (c2a.fk_mv)"
                                        );
                                        x.After.Count("fk *", 1);
                                        x.After.Single("fk c2a fk_mv *");
                                    }
                                );
                            }
                        })
                ),
            Row(
                    "realdb-name-collision-uq-moved-to-other-table-default-selection",
                    "[Integration] 同期: 一意制約を同じ名前のまま別の表へ移した図を既定の選び方で流すと、名前がスキーマで一意な方言では追加を外して名指しし、成功する",
                    AllDialects
                )
                .LiveWith(w => NameCollisionScenarios.UniqueMovedLive(w.Live, w.Capability))
                .TargetWith(w => NameCollisionScenarios.UniqueMovedTarget(w.Target, w.Capability))
                .OnRealDatabase(r =>
                    X.SetupPerDialect(
                            r,
                            d =>
                                string.Join(
                                    "\n",
                                    X.Insert(d, "t3a", ["id", "u"], "1, 1"),
                                    X.Insert(d, "t3b", ["id", "w"], "1, 1")
                                )
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");

                            foreach (var dialect in AllDialects)
                            {
                                e.For(
                                    dialect,
                                    x =>
                                    {
                                        if (dialect is DialectNames.MySql or DialectNames.Sqlite)
                                        {
                                            // MySQL は一意制約の名前を表ごとに持ち、SQLite は名前を持たないので、追加は通る
                                            x.Warnings.Absent(
                                                "SelectedItemRequiresUnselectedItem*"
                                            );
                                            x.After.Count("uq *", 2);
                                            return;
                                        }

                                        x.Warnings.Single(
                                            "SelectedItemRequiresUnselectedItem(t3b): uq_mv (t3a.uq_mv)"
                                        );
                                        x.After.Count("uq *", 1);
                                        x.After.Single("uq t3a uq_mv (u)");
                                    }
                                );
                            }
                        })
                ),
            Row(
                    "realdb-name-collision-fk-renamed-same-definition-default-selection",
                    "[Integration] 同期: 外部キーの名前だけを変えた図を既定の選び方で流すと、同じ定義の外部キーを 2 本持てない方言では追加を外して名指しし、成功する",
                    AllDialects
                )
                .LiveWith(w => NameCollisionScenarios.ForeignKeyRenamedLive(w.Live, w.Capability))
                .TargetWith(w =>
                    NameCollisionScenarios.ForeignKeyRenamedTarget(w.Target, w.Capability)
                )
                .OnRealDatabase(r =>
                    X.SetupPerDialect(
                            r,
                            d =>
                                string.Join(
                                    "\n",
                                    X.Insert(d, "p1", ["id"], "1"),
                                    X.Insert(d, "c1", ["id", "pid"], "1, 1")
                                )
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");

                            foreach (var dialect in AllDialects)
                            {
                                e.For(
                                    dialect,
                                    x =>
                                    {
                                        switch (dialect)
                                        {
                                            case DialectNames.Oracle:
                                                x.Warnings.Single(
                                                    "SelectedItemRequiresUnselectedItem(c1): fk_new (c1.fk_old)"
                                                );
                                                x.After.Count("fk *", 1);
                                                x.After.Single("fk c1 fk_old *");
                                                return;
                                            case DialectNames.Sqlite:
                                                // SQLite は外部キーの名前を持たないので差分が出ない
                                                x.Warnings.Absent(
                                                    "SelectedItemRequiresUnselectedItem*"
                                                );
                                                x.After.Count("fk *", 1);
                                                return;
                                            default:
                                                x.Warnings.Absent(
                                                    "SelectedItemRequiresUnselectedItem*"
                                                );
                                                x.After.Count("fk *", 2);
                                                return;
                                        }
                                    }
                                );
                            }
                        })
                ),
        ];

    /// <summary>
    /// 名前の重なりの判定が、同じ段で外す列の削除（主キーの変更を選んでいない主キーの列の削除）で消える制約を「消える」と数えない
    /// （表の追加と一意制約の追加・網と 5 方言の実 DB）
    /// </summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > DroppedKeyColumnNameCollisionRows()
    {
        foreach (
            var (shape, onNewTable, kind, subject, colliding) in new (
                string,
                bool,
                SchemaDiffKind,
                string,
                string[]
            )[]
            {
                (
                    "new-table",
                    true,
                    SchemaDiffKind.AddTable,
                    "nt7",
                    [DialectNames.SqlServer, DialectNames.Oracle]
                ),
                (
                    "unique",
                    false,
                    SchemaDiffKind.AddUniqueConstraint,
                    "c7",
                    [
                        DialectNames.SqlServer,
                        DialectNames.PostgreSql,
                        DialectNames.MySql,
                        DialectNames.Oracle,
                    ]
                ),
            }
        )
        {
            yield return Row(
                    $"realdb-name-collision-{shape}-with-excluded-key-column-drop",
                    $"[Integration] 同期: 主キーの列の削除（主キーの変更を選ばない＝外す）と、その列を参照する外部キーと同じ名前の{(onNewTable ? "新しい表の" : "")}一意制約の追加を選ぶと、名前が重なる方言では追加も外して名指しし、成功する",
                    AllDialects
                )
                .LiveWith(w => NameCollisionScenarios.DroppedKeyColumnLive(w.Live, w.Capability))
                .TargetWith(w =>
                    NameCollisionScenarios.DroppedKeyColumnTarget(
                        w.Target,
                        w.Capability,
                        onNewTable
                    )
                )
                .Select(items => RealScenarios.SelectKinds(items, kind, SchemaDiffKind.DropColumn))
                .Expect(e =>
                {
                    foreach (var dialect in AllDialects)
                    {
                        e.For(
                            dialect,
                            x =>
                            {
                                x.Warnings.Single(
                                    "SelectedItemRequiresUnselectedItem(p7): id (PRIMARY KEY)"
                                );

                                if (colliding.Contains(dialect))
                                {
                                    x.Excluded.Count("*", 2)
                                        .Warnings.Single(
                                            $"SelectedItemRequiresUnselectedItem({subject}): fk_keep (c7.fk_keep)"
                                        );
                                    return;
                                }

                                x.Excluded.Count("*", 1);
                            }
                        );
                    }
                })
                .OnRealDatabase(r =>
                    X.SetupPerDialect(
                            r,
                            d =>
                                X.Insert(d, "p7", ["id", "v"], "1, 1")
                                + "\n"
                                + X.Insert(d, "c7", ["id", "pid", "u"], "1, 1, 1")
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.After.Count("fk c7 *", 1);
                        })
                );
        }

        // 同じ表の一意制約と同じ名前の外部キーの追加（MySQL は外部キーの列に索引が無ければ、外部キーと同じ名前の索引を作る）
        string[] sameTableColliding =
        [
            DialectNames.SqlServer,
            DialectNames.PostgreSql,
            DialectNames.MySql,
            DialectNames.Oracle,
        ];

        yield return Row(
                "realdb-name-collision-fk-named-like-unique-on-other-column-of-same-table-default-selection",
                "[Integration] 同期: 同じ表の別の列の一意制約と同じ名前の外部キーを足す図を既定の選び方で流すと、名前が重なる方言（MySQL は外部キーの索引が名前を取る）では外部キーの追加を外して名指しし、成功する",
                AllDialects
            )
            .LiveWith(w => NameCollisionScenarios.SameTableUniqueLive(w.Live, w.Capability))
            .TargetWith(w => NameCollisionScenarios.SameTableUniqueTarget(w.Target, w.Capability))
            .Expect(e =>
            {
                foreach (var dialect in AllDialects)
                {
                    e.For(
                        dialect,
                        x =>
                        {
                            if (sameTableColliding.Contains(dialect))
                            {
                                x.Excluded.Exact("AddForeignKey *")
                                    .Warnings.Single(
                                        "SelectedItemRequiresUnselectedItem(c8): fk_rev (c8.fk_rev)"
                                    );
                                return;
                            }

                            x.Excluded.Empty();
                        }
                    );
                }
            })
            .OnRealDatabase(r =>
                X.SetupPerDialect(
                        r,
                        d =>
                            X.Insert(d, "p8", ["id"], "1")
                            + "\n"
                            + X.Insert(d, "c8", ["id", "pid", "u"], "1, 1, 1")
                    )
                    .Expect(e =>
                    {
                        e.Outcome.Exact("committed");

                        foreach (var dialect in AllDialects)
                        {
                            e.For(
                                dialect,
                                x =>
                                    x.After.Count(
                                        "fk *",
                                        sameTableColliding.Contains(dialect) ? 0 : 1
                                    )
                            );
                        }
                    })
            );
    }
}

/// <summary>名前の重なりの行の図（型は方言の整数型）</summary>
internal static class NameCollisionScenarios
{
    /// <summary>親 p8(id) と子 c8(id, pid, u)、子の一意制約 fk_rev(u)（外部キーは無い）</summary>
    public static void SameTableUniqueLive(ExpectationSchema s, string dialect)
    {
        var type = X.IntType(dialect);
        s.Table("p8", S.Pk("id", type));
        s.Table("c8", S.Pk("id", type), S.NotNull("pid", type), S.NotNull("u", type));
        s.Unique("c8", "fk_rev", "u");
    }

    /// <summary>DB 側の図に、一意制約と同じ名前の外部キー fk_rev（c8.pid → p8.id）を足す</summary>
    public static void SameTableUniqueTarget(ExpectationSchema s, string dialect)
    {
        SameTableUniqueLive(s, dialect);
        s.Fk("p8", "c8", "fk_rev", ("id", "pid"));
    }

    /// <summary>親 p7(id, v) と子 c7(id, pid, u)、外部キー fk_keep（c7.pid → p7.id）</summary>
    public static void DroppedKeyColumnLive(ExpectationSchema s, string dialect)
    {
        var type = X.IntType(dialect);
        s.Table("p7", S.Pk("id", type), S.NotNull("v", type));
        s.Table("c7", S.Pk("id", type), S.NotNull("pid", type), S.NotNull("u", type));
        s.Fk("p7", "c7", "fk_keep", ("id", "pid"));
    }

    /// <summary>
    /// 親 p7 の主キーの列 id を消し（外部キー fk_keep も図から消える）、名前 fk_keep を、新しい表 nt7(id, u) の一意制約
    /// （<paramref name="onNewTable"/>）か子 c7(u) の一意制約に付ける
    /// </summary>
    public static void DroppedKeyColumnTarget(ExpectationSchema s, string dialect, bool onNewTable)
    {
        var type = X.IntType(dialect);
        s.Table("p7", S.NotNull("v", type));
        s.Table("c7", S.Pk("id", type), S.NotNull("pid", type), S.NotNull("u", type));

        if (onNewTable)
        {
            s.Table("nt7", S.Pk("id", type), S.NotNull("u", type));
            s.Unique("nt7", "fk_keep", "u");
            return;
        }

        s.Unique("c7", "fk_keep", "u");
    }

    /// <summary>親 p2(id) と子 c2a(id, pid)・c2b(id, pid)、外部キー fk_mv は c2a から</summary>
    public static void ForeignKeyMovedLive(ExpectationSchema s, string dialect)
    {
        var type = X.IntType(dialect);
        s.Table("p2", S.Pk("id", type));
        s.Table("c2a", S.Pk("id", type), S.NotNull("pid", type));
        s.Table("c2b", S.Pk("id", type), S.NotNull("pid", type));
        s.Fk("p2", "c2a", "fk_mv", ("id", "pid"));
    }

    /// <summary>外部キー fk_mv を c2b から張る（c2a からは外す）</summary>
    public static void ForeignKeyMovedTarget(ExpectationSchema s, string dialect)
    {
        var type = X.IntType(dialect);
        s.Table("p2", S.Pk("id", type));
        s.Table("c2a", S.Pk("id", type), S.NotNull("pid", type));
        s.Table("c2b", S.Pk("id", type), S.NotNull("pid", type));
        s.Fk("p2", "c2b", "fk_mv", ("id", "pid"));
    }

    /// <summary>t3a(id, u) の一意制約 uq_mv(u) と t3b(id, w)</summary>
    public static void UniqueMovedLive(ExpectationSchema s, string dialect)
    {
        var type = X.IntType(dialect);
        s.Table("t3a", S.Pk("id", type), S.NotNull("u", type));
        s.Table("t3b", S.Pk("id", type), S.NotNull("w", type));
        s.Unique("t3a", "uq_mv", "u");
    }

    /// <summary>一意制約 uq_mv を t3b(w) へ移す</summary>
    public static void UniqueMovedTarget(ExpectationSchema s, string dialect)
    {
        var type = X.IntType(dialect);
        s.Table("t3a", S.Pk("id", type), S.NotNull("u", type));
        s.Table("t3b", S.Pk("id", type), S.NotNull("w", type));
        s.Unique("t3b", "uq_mv", "w");
    }

    /// <summary>表 old_t(id, u) と一意制約 uq_keep(u)</summary>
    public static void RenamedTableLive(ExpectationSchema s, string dialect)
    {
        var type = X.IntType(dialect);
        s.Table("old_t", S.Pk("id", type), S.NotNull("u", type));
        s.Unique("old_t", "uq_keep", "u");
    }

    /// <summary>表の名前を new_t へ変え、一意制約の名前 uq_keep を引き継ぐ</summary>
    public static void RenamedTableTarget(ExpectationSchema s, string dialect)
    {
        var type = X.IntType(dialect);
        s.Table("new_t", S.Pk("id", type), S.NotNull("u", type));
        s.Unique("new_t", "uq_keep", "u");
    }

    /// <summary>親 p6(id) と子 c6(id, pid)、外部キー fk_same</summary>
    public static void NewTableUniqueLikeForeignKeyLive(ExpectationSchema s, string dialect)
    {
        var type = X.IntType(dialect);
        s.Table("p6", S.Pk("id", type));
        s.Table("c6", S.Pk("id", type), S.NotNull("pid", type));
        s.Fk("p6", "c6", "fk_same", ("id", "pid"));
    }

    /// <summary>外部キー fk_same を外し、新しい表 nt(id, u) に同じ名前の一意制約 fk_same(u) を張る</summary>
    public static void NewTableUniqueLikeForeignKeyTarget(ExpectationSchema s, string dialect)
    {
        var type = X.IntType(dialect);
        s.Table("p6", S.Pk("id", type));
        s.Table("c6", S.Pk("id", type), S.NotNull("pid", type));
        s.Table("nt", S.Pk("id", type), S.NotNull("u", type));
        s.Unique("nt", "fk_same", "u");
    }

    /// <summary>親 p1(id) と子 c1(id, pid)、外部キー fk_old</summary>
    public static void ForeignKeyRenamedLive(ExpectationSchema s, string dialect)
    {
        var type = X.IntType(dialect);
        s.Table("p1", S.Pk("id", type));
        s.Table("c1", S.Pk("id", type), S.NotNull("pid", type));
        s.Fk("p1", "c1", "fk_old", ("id", "pid"));
    }

    /// <summary>同じ外部キーの名前を fk_new へ変える</summary>
    public static void ForeignKeyRenamedTarget(ExpectationSchema s, string dialect)
    {
        var type = X.IntType(dialect);
        s.Table("p1", S.Pk("id", type));
        s.Table("c1", S.Pk("id", type), S.NotNull("pid", type));
        s.Fk("p1", "c1", "fk_new", ("id", "pid"));
    }

    /// <summary>親 p4(id) と子 c4(id, pid)、c4 の一意制約 uq_x(pid)</summary>
    public static void UniqueThenForeignKeyLive(ExpectationSchema s, string dialect)
    {
        var type = X.IntType(dialect);
        s.Table("p4", S.Pk("id", type));
        s.Table("c4", S.Pk("id", type), S.NotNull("pid", type));
        s.Unique("c4", "uq_x", "pid");
    }

    /// <summary>一意制約 uq_x を外し、c4 に同じ名前の外部キー uq_x を足す</summary>
    public static void UniqueThenForeignKeyTarget(ExpectationSchema s, string dialect)
    {
        var type = X.IntType(dialect);
        s.Table("p4", S.Pk("id", type));
        s.Table("c4", S.Pk("id", type), S.NotNull("pid", type));
        s.Fk("p4", "c4", "uq_x", ("id", "pid"));
    }

    /// <summary>親 p5(id)・子 c5(id, pid) と、別の表 t5(id, v) の一意制約 uq_y(v)</summary>
    public static void UniqueThenOtherForeignKeyLive(ExpectationSchema s, string dialect)
    {
        var type = X.IntType(dialect);
        s.Table("p5", S.Pk("id", type));
        s.Table("c5", S.Pk("id", type), S.NotNull("pid", type));
        s.Table("t5", S.Pk("id", type), S.NotNull("v", type));
        s.Unique("t5", "uq_y", "v");
    }

    /// <summary>t5 の一意制約 uq_y を外し、c5 に同じ名前の外部キー uq_y を足す</summary>
    public static void UniqueThenOtherForeignKeyTarget(ExpectationSchema s, string dialect)
    {
        var type = X.IntType(dialect);
        s.Table("p5", S.Pk("id", type));
        s.Table("c5", S.Pk("id", type), S.NotNull("pid", type));
        s.Table("t5", S.Pk("id", type), S.NotNull("v", type));
        s.Fk("p5", "c5", "uq_y", ("id", "pid"));
    }
}
