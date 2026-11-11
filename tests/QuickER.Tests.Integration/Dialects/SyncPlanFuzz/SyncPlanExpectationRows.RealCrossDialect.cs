using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.PostgreSql;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

internal static partial class SyncPlanExpectationRows
{
    /// <summary>
    /// 方言をまたぐ実 DB の段の行のうち、その方言で流す行の数の下限（行を足したら・消したら合わせる。方言の行のファイルの数に足す）
    /// </summary>
    private static int RealCrossDialectRowCount(string dialect) =>
        dialect switch
        {
            DialectNames.SqlServer => 24,
            DialectNames.PostgreSql => 27,
            DialectNames.MySql => 24,
            DialectNames.Oracle => 26,
            DialectNames.Sqlite => 25,
            _ => 0,
        };

    /// <summary>実 DB の段：方言をまたぐ同期（5 方言で同じ形の行）</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealCrossDialectRows() =>
        [
            .. RealCrossDialectSetNullRows(),
            .. RealCrossDialectDropMemberColumnRows(),
            .. RealCrossDialectTableWithoutColumnsRows(),
            .. RealCrossDialectNameRows(),
            .. RealCrossDialectDescriptionRows(),
            .. RealCrossDialectLongDefaultNameRows(),
            .. RealCrossDialectIntegerWidthRows(),
            .. RealCrossDialectEmptyColumnChangeRows(),
            .. RealCrossDialectRowDependentChangeRows(),
        ];

    /// <summary>
    /// 既存の行（とモデルの外の依存）で断られうる文の前で外し、その文か後ろで付け直す制約の名指し（暗黙コミットの方言だけ）と、断られた
    /// ときの部分適用（名指しした制約だけが外れたまま残る）・トランザクションの方言の巻き戻り
    /// </summary>
    /// <remarks>
    /// どの行も、DB は断られうる文を実際に断るデータ（重複・NULL・行のある表・計算列）を持つ。計画は値を見ないので、トランザクションの方言は
    /// 警告せずに失敗して巻き戻る（データの都合の失敗）。暗黙コミットの方言は名指しした制約が外れたまま残り、実 DB の表明 1b はその消失だけを
    /// 受け入れる。
    /// </remarks>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealCrossDialectRowDependentChangeRows() =>
        [
            Row(
                    "plan-row-dependent-change-pk-onto-duplicates-names-primary-key",
                    "[Integration] 主キーを重複のある列へ移す：暗黙コミットの方言だけ、付与が断られて外れたまま残りうる主キーを名指しする",
                    AllDialects
                )
                .LiveWith(w =>
                    w.Live.Table(
                        "rd_pk",
                        ExpectationSchema.Pk("id", RealCrossDialectScenarios.IntType(w.Capability)),
                        ExpectationSchema.NotNull(
                            "k",
                            RealCrossDialectScenarios.IntType(w.Capability)
                        )
                    )
                )
                .TargetWith(w =>
                    w.Target.Table(
                        "rd_pk",
                        ExpectationSchema.NotNull(
                            "id",
                            RealCrossDialectScenarios.IntType(w.Capability)
                        ),
                        ExpectationSchema.Pk("k", RealCrossDialectScenarios.IntType(w.Capability))
                    )
                )
                .Select(RealScenarios.SelectAll)
                .Expect(e =>
                    ExpectRowDependentWarnings(
                        e,
                        [
                            "ConstraintRebuildAroundRowDependentChange(rd_pk): PRIMARY KEY (PRIMARY KEY rd_pk (k))",
                        ]
                    )
                )
                .OnRealDatabase(r =>
                    RowDependentFailure(
                        RealCrossDialectScenarios.SetupPerDialect(
                            r,
                            d =>
                                RealCrossDialectScenarios.Insert(d, "rd_pk", ["id", "k"], "1, 5")
                                + "\n"
                                + RealCrossDialectScenarios.Insert(d, "rd_pk", ["id", "k"], "2, 5")
                        ),
                        "データの都合：k は重複する値（5）を持つので主キーにできない（計画は列の値を見ない）",
                        DuplicateKeyErrors("ORA-02437")
                    )
                ),
            Row(
                    "plan-row-dependent-change-pk-onto-null-names-primary-key",
                    "[Integration] 主キーを NULL のある整数の列へ移す：どの方言も断って値を変えない（SQLite の rowid の別名でも NULL を採番の値に変えない）。暗黙コミットの方言だけ主キーを名指しする",
                    AllDialects
                )
                .LiveWith(w =>
                    w.Live.Table(
                        "rd_pkn",
                        ExpectationSchema.Pk("id", RealCrossDialectScenarios.IntType(w.Capability)),
                        ExpectationSchema.Nullable(
                            "k",
                            RealCrossDialectScenarios.IntType(w.Capability)
                        )
                    )
                )
                .TargetWith(w =>
                    w.Target.Table(
                        "rd_pkn",
                        ExpectationSchema.NotNull(
                            "id",
                            RealCrossDialectScenarios.IntType(w.Capability)
                        ),
                        ExpectationSchema.Pk("k", RealCrossDialectScenarios.IntType(w.Capability))
                    )
                )
                .Select(RealScenarios.SelectAll)
                .Expect(e =>
                    ExpectRowDependentWarnings(
                        e,
                        [
                            "ConstraintRebuildAroundRowDependentChange(rd_pkn): PRIMARY KEY (rd_pkn.k: NOT NULL, PRIMARY KEY rd_pkn (k))",
                        ]
                    )
                )
                .OnRealDatabase(r =>
                {
                    RowDependentFailure(
                        RealCrossDialectScenarios.SetupPerDialect(
                            r,
                            d =>
                                RealCrossDialectScenarios.Insert(
                                    d,
                                    "rd_pkn",
                                    ["id", "k"],
                                    "1, NULL"
                                )
                                + "\n"
                                + RealCrossDialectScenarios.Insert(d, "rd_pkn", ["id", "k"], "2, 7")
                        ),
                        "データの都合：k は NULL を持つので主キーにできない（計画は列の値を見ない）",
                        NullKeyErrors
                    );
                    RealCrossDialectScenarios.QueryPerDialect(
                        r,
                        "values",
                        d =>
                            $"SELECT {RealCrossDialectScenarios.Quote(d, "id")}, {RealCrossDialectScenarios.Quote(d, "k")}"
                            + $" FROM {RealCrossDialectScenarios.Quote(d, "rd_pkn")} ORDER BY {RealCrossDialectScenarios.Quote(d, "id")}"
                    );

                    // 値は同期の前のまま（NULL が採番の値に変わらない）
                    r.Expect(e => e.Probe("values").Exact("1 k=-", "2 k=7"));
                }),
            Row(
                    "plan-row-dependent-change-unique-rebuild-onto-duplicates-names-unique",
                    "[Integration] 名前つきの一意制約を重複のある列へ作り直す：暗黙コミットの方言だけ、作り直しが断られて外れたまま残りうる一意制約を名指しする",
                    AllDialects
                )
                .LiveWith(w => RowDependentUniqueTable(w.Live, w.Capability, "u"))
                .TargetWith(w => RowDependentUniqueTable(w.Target, w.Capability, "w"))
                .Select(RealScenarios.SelectAll)
                .Expect(e =>
                    ExpectRowDependentWarnings(
                        e,
                        [
                            "ConstraintRebuildAroundRowDependentChange(rd_uq): uq_rd_uq (UNIQUE rd_uq (w))",
                        ]
                    )
                )
                .OnRealDatabase(r =>
                    RowDependentFailure(
                        RealCrossDialectScenarios.SetupPerDialect(
                            r,
                            d =>
                                RealCrossDialectScenarios.Insert(
                                    d,
                                    "rd_uq",
                                    ["id", "u", "w"],
                                    "1, 1, 7"
                                )
                                + "\n"
                                + RealCrossDialectScenarios.Insert(
                                    d,
                                    "rd_uq",
                                    ["id", "u", "w"],
                                    "2, 2, 7"
                                )
                        ),
                        "データの都合：w は重複する値（7）を持つので一意制約を張れない（計画は列の値を見ない）",
                        DuplicateKeyErrors("ORA-02299")
                    )
                ),
            Row(
                    "plan-row-dependent-change-not-null-on-fk-column-names-fk",
                    "[Integration] 外部キーの子の列を NULL のまま NOT NULL にする：列の変更で外部キーを外し直す暗黙コミットの方言だけ、外部キーを名指しする",
                    AllDialects
                )
                .LiveWith(w =>
                    RowDependentParentChild(w.Live, w.Capability, "rd_nn", nullable: true)
                )
                .TargetWith(w =>
                    RowDependentParentChild(w.Target, w.Capability, "rd_nn", nullable: false)
                )
                .Select(RealScenarios.SelectAll)
                .Expect(e =>
                {
                    foreach (var dialect in AllDialects)
                    {
                        var c = SyncPlanExpectationCapabilities.Get(dialect);
                        e.For(
                            dialect,
                            x =>
                            {
                                if (!c.TransactionalDdl && c.AlterColumnRequiresForeignKeyRebuild)
                                {
                                    x.Warnings.Exact(
                                        "ConstraintRebuildAroundRowDependentChange(rd_nn_child): fk_rd_nn (rd_nn_child.pid: NOT NULL)"
                                    );
                                    return;
                                }

                                x.Warnings.Absent("ConstraintRebuild*");
                            }
                        );
                    }
                })
                .OnRealDatabase(r =>
                {
                    RealCrossDialectScenarios
                        .SetupPerDialect(
                            r,
                            d =>
                                RealCrossDialectScenarios.Insert(d, "rd_nn_parent", ["id"], "1")
                                + "\n"
                                + RealCrossDialectScenarios.Insert(
                                    d,
                                    "rd_nn_child",
                                    ["id", "pid"],
                                    "1, NULL"
                                )
                        )
                        .ExpectsUnwarnedFailure(
                            "データの都合：pid は NULL を持つので NOT NULL にできない（計画は列の値を見ない）"
                        )
                        .Expect(e =>
                        {
                            var errors = ErrorFragments(
                                sqlServer: "Cannot insert the value NULL",
                                postgreSql: "contains null values",
                                mySql: "Invalid use of NULL value",
                                oracle: "ORA-02296",
                                sqlite: "NOT NULL constraint failed"
                            );

                            foreach (var dialect in AllDialects)
                            {
                                var c = SyncPlanExpectationCapabilities.Get(dialect);
                                e.For(
                                    dialect,
                                    x => x.Outcome.Exact($"failed: *{errors[dialect]}*")
                                );

                                // 外部キーを外し直す暗黙コミットの方言（MySQL）だけ、名指しした外部キーが外れたまま残る
                                if (c.TransactionalDdl || !c.AlterColumnRequiresForeignKeyRebuild)
                                {
                                    e.For(dialect, x => x.Unchanged());
                                }
                                else
                                {
                                    e.For(dialect, x => x.After.Absent("fk rd_nn_child *"));
                                }
                            }
                        });
                }),
            Row(
                    "plan-row-dependent-change-add-unique-names-collateral-fk",
                    "[Integration] 重複のある列へ一意制約を足し、別の外部キーを作り直す：暗黙コミットの方言だけ、追加の前で外す作り直しの外部キーを名指しする",
                    AllDialects
                )
                .LiveWith(w =>
                    RowDependentParentChild(
                        w.Live,
                        w.Capability,
                        "rd_au",
                        nullable: false,
                        withValue: true
                    )
                )
                .TargetWith(w =>
                {
                    RowDependentParentChild(
                        w.Target,
                        w.Capability,
                        "rd_au",
                        nullable: false,
                        withValue: true,
                        onDelete: ForeignKeyReferentialAction.Cascade
                    );
                    w.Target.Unique("rd_au_child", "uq_rd_au_v", "v");
                })
                .Select(RealScenarios.SelectAll)
                .Expect(e =>
                    ExpectRowDependentWarnings(
                        e,
                        [
                            "ConstraintRebuildAroundRowDependentChange(rd_au_child): fk_rd_au (UNIQUE rd_au_child (v))",
                        ]
                    )
                )
                .OnRealDatabase(r =>
                    RowDependentFailure(
                        RealCrossDialectScenarios.SetupPerDialect(
                            r,
                            d =>
                                RealCrossDialectScenarios.Insert(d, "rd_au_parent", ["id"], "1")
                                + "\n"
                                + RealCrossDialectScenarios.Insert(
                                    d,
                                    "rd_au_child",
                                    ["id", "pid", "v"],
                                    "1, 1, 9"
                                )
                                + "\n"
                                + RealCrossDialectScenarios.Insert(
                                    d,
                                    "rd_au_child",
                                    ["id", "pid", "v"],
                                    "2, 1, 9"
                                )
                        ),
                        "データの都合：v は重複する値（9）を持つので一意制約を張れない（計画は列の値を見ない）",
                        DuplicateKeyErrors("ORA-02299")
                    )
                ),
            Row(
                    "plan-row-dependent-change-add-not-null-column-names-collateral-fk",
                    "[Integration] 行のある表へ NOT NULL の列を足し、別の外部キーを作り直す：表が空でないと足せない暗黙コミットの方言だけ、作り直しの外部キーを名指しする",
                    AllDialects
                )
                .LiveWith(w =>
                    RowDependentParentChild(w.Live, w.Capability, "rd_ac", nullable: false)
                )
                .TargetWith(w =>
                {
                    RowDependentParentChild(
                        w.Target,
                        w.Capability,
                        "rd_ac",
                        nullable: false,
                        onDelete: ForeignKeyReferentialAction.Cascade
                    );
                    w.Target["rd_ac_child"]
                        .Columns.Add(
                            ExpectationSchema.NotNull(
                                "extra",
                                RealCrossDialectScenarios.IntType(w.Capability)
                            )
                        );
                })
                .Select(RealScenarios.SelectAll)
                .Expect(e =>
                {
                    foreach (var dialect in AllDialects)
                    {
                        var c = SyncPlanExpectationCapabilities.Get(dialect);
                        e.For(
                            dialect,
                            x =>
                            {
                                if (!c.TransactionalDdl && c.AddNotNullColumnRequiresEmptyTable)
                                {
                                    x.Warnings.Exact(
                                        "ConstraintRebuildAroundRowDependentChange(rd_ac_child): fk_rd_ac (rd_ac_child.extra: ADD NOT NULL)"
                                    );
                                    return;
                                }

                                x.Warnings.Absent("ConstraintRebuild*");
                            }
                        );
                    }
                })
                .OnRealDatabase(r =>
                {
                    RealCrossDialectScenarios
                        .SetupPerDialect(
                            r,
                            d =>
                                RealCrossDialectScenarios.Insert(d, "rd_ac_parent", ["id"], "1")
                                + "\n"
                                + RealCrossDialectScenarios.Insert(
                                    d,
                                    "rd_ac_child",
                                    ["id", "pid"],
                                    "1, 1"
                                )
                        )
                        .ExpectsUnwarnedFailure(
                            "データの都合：rd_ac_child は行を持つので、既定値の無い NOT NULL の列を足せない（計画は表の行の有無を見ない）"
                        )
                        .Expect(e =>
                        {
                            foreach (var dialect in AllDialects)
                            {
                                var c = SyncPlanExpectationCapabilities.Get(dialect);
                                e.For(
                                    dialect,
                                    x =>
                                    {
                                        // MySQL は型の暗黙の既定値で埋めて通す
                                        if (!c.AddNotNullColumnRequiresEmptyTable)
                                        {
                                            x.Outcome.Exact("committed");
                                            x.AfterDiff.Empty();
                                            return;
                                        }

                                        x.Outcome.Exact(
                                            "failed: *"
                                                + ErrorFragments(
                                                    sqlServer: "cannot be added to non-empty table",
                                                    postgreSql: "contains null values",
                                                    mySql: "",
                                                    oracle: "ORA-01758",
                                                    sqlite: "NOT NULL constraint failed"
                                                )[dialect]
                                                + "*"
                                        );

                                        if (c.TransactionalDdl)
                                        {
                                            x.Unchanged();
                                        }
                                        else
                                        {
                                            x.After.Absent("fk rd_ac_child *");
                                        }
                                    }
                                );
                            }
                        });
                }),
            Row(
                    "plan-row-dependent-change-pk-reorder-is-not-named",
                    "[Integration] 主キーの列を並べ替える（参照する外部キーあり・行あり）：live の主キーと同じ列集合は一意と証明できるので、どの方言も名指しせずに通る",
                    AllDialects
                )
                .LiveWith(w =>
                    RealCrossDialectScenarios.CompositeParentChild(
                        w.Live,
                        w.Capability,
                        wideA: false,
                        reordered: false
                    )
                )
                .TargetWith(w =>
                    RealCrossDialectScenarios.CompositeParentChild(
                        w.Target,
                        w.Capability,
                        wideA: false,
                        reordered: true
                    )
                )
                .Select(RealScenarios.SelectAll)
                .Expect(e => e.Warnings.Absent("ConstraintRebuild*"))
                .OnRealDatabase(r =>
                    RealCrossDialectScenarios
                        .SetupPerDialect(
                            r,
                            d =>
                                RealCrossDialectScenarios.Insert(d, "ep_parent", ["a", "b"], "1, 1")
                                + "\n"
                                + RealCrossDialectScenarios.Insert(
                                    d,
                                    "ep_child",
                                    ["id", "pa", "pb"],
                                    "1, 1, 1"
                                )
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.AfterDiff.Empty();
                        })
                ),
            Row(
                    "plan-row-dependent-change-computed-source-names-collateral-fk",
                    "[Integration] 計算列のある表の列の型を広げ、別の外部キーを作り直す：計算列が使う列の変更を断る暗黙コミットの方言だけ、作り直しの外部キーを名指しする",
                    AllDialects
                )
                .LiveWith(w =>
                {
                    w.Live.Table(
                        "rd_cs",
                        ExpectationSchema.Pk("id", RealCrossDialectScenarios.IntType(w.Capability)),
                        ExpectationSchema.Nullable(
                            "x",
                            RealCrossDialectScenarios.IntType(w.Capability)
                        )
                    );
                    RowDependentParentChild(w.Live, w.Capability, "rd_cs", nullable: false);
                })
                .TargetWith(w =>
                {
                    w.Target.CopyFrom(w.Live, preserveIds: false);
                    w.Target.Col("rd_cs", "x").DataType = RealCrossDialectScenarios.BigIntType(
                        w.Capability
                    );
                    w.Target.Relationships.Single().OnDelete = ForeignKeyReferentialAction.Cascade;
                })
                .Select(items =>
                    RealScenarios.SelectKinds(
                        items,
                        SchemaDiffKind.AlterColumn,
                        SchemaDiffKind.AlterForeignKey
                    )
                )
                .Expect(e =>
                {
                    foreach (var dialect in AllDialects)
                    {
                        var c = SyncPlanExpectationCapabilities.Get(dialect);
                        e.For(
                            dialect,
                            x =>
                            {
                                // 網の図は計算列を持たない（実 DB の段で足す）ので、網では名指ししない
                                x.Warnings.Absent("ConstraintRebuild*");
                            }
                        );
                    }
                })
                .OnRealDatabase(r =>
                {
                    foreach (var dialect in AllDialects)
                    {
                        r.Setup(RowDependentComputedColumnSql(dialect), dialect);
                    }

                    r.ExpectsUnwarnedFailure(
                            "モデルの外の物：計算列 calc の式が x を参照するので、x の型の変更を DB が断る（図は式の依存を持たない）"
                        )
                        .Expect(e =>
                        {
                            foreach (var dialect in AllDialects)
                            {
                                var c = SyncPlanExpectationCapabilities.Get(dialect);
                                e.For(
                                    dialect,
                                    x =>
                                    {
                                        if (!c.AlterColumnBlockedByComputedColumn)
                                        {
                                            x.Outcome.Exact("committed");
                                            return;
                                        }

                                        x.Outcome.Exact(
                                            "failed: *"
                                                + ErrorFragments(
                                                    sqlServer: "is dependent on column",
                                                    postgreSql: "generated column",
                                                    mySql: "",
                                                    oracle: "ORA-54031",
                                                    sqlite: ""
                                                )[dialect]
                                                + "*"
                                        );

                                        if (c.TransactionalDdl)
                                        {
                                            x.Unchanged();
                                            return;
                                        }

                                        // 取り込んだ DB には計算列があるので、実 DB の段の計画は作り直しの外部キーを名指しする
                                        x.Warnings.Exact(
                                            "ConstraintRebuildAroundRowDependentChange(rd_cs_child): fk_rd_cs (rd_cs.x: *)"
                                        );
                                        x.After.Absent("fk rd_cs_child *");
                                    }
                                );
                            }
                        });
                }),
        ];

    /// <summary>
    /// 暗黙コミットの方言だけ断られうる文の警告を <paramref name="expected"/> のとおりに出し、トランザクションの方言は名指ししない
    /// </summary>
    private static void ExpectRowDependentWarnings(ExpectationBuilder e, string[] expected)
    {
        foreach (var dialect in AllDialects)
        {
            var transactional = SyncPlanExpectationCapabilities.Get(dialect).TransactionalDdl;
            e.For(
                dialect,
                x =>
                {
                    if (transactional)
                    {
                        x.Warnings.Absent("ConstraintRebuild*");
                        return;
                    }

                    x.Warnings.Exact(expected);
                }
            );
        }
    }

    /// <summary>
    /// データの都合で全方言が断る行の実 DB の期待：トランザクションの方言は巻き戻って DB が変わらず、暗黙コミットの方言は名指しした制約
    /// だけが外れたまま残る（実 DB の表明 1b が名指しの外の消失を断る）
    /// </summary>
    private static RealStage RowDependentFailure(
        RealStage stage,
        string reason,
        IReadOnlyDictionary<string, string> errors
    ) =>
        stage
            .ExpectsUnwarnedFailure(reason)
            .Expect(e =>
            {
                foreach (var dialect in AllDialects)
                {
                    e.For(dialect, x => x.Outcome.Exact($"failed: *{errors[dialect]}*"));

                    if (SyncPlanExpectationCapabilities.Get(dialect).TransactionalDdl)
                    {
                        e.For(dialect, x => x.Unchanged());
                    }
                }
            });

    /// <summary>方言ごとのエラー文の断片（失敗した実行の結果の行に添える）</summary>
    private static IReadOnlyDictionary<string, string> ErrorFragments(
        string sqlServer,
        string postgreSql,
        string mySql,
        string oracle,
        string sqlite
    ) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DialectNames.SqlServer] = sqlServer,
            [DialectNames.PostgreSql] = postgreSql,
            [DialectNames.MySql] = mySql,
            [DialectNames.Oracle] = oracle,
            [DialectNames.Sqlite] = sqlite,
        };

    /// <summary>NULL のある列を主キーにできないときのエラー文の断片</summary>
    private static IReadOnlyDictionary<string, string> NullKeyErrors =>
        ErrorFragments(
            sqlServer: "Cannot insert the value NULL",
            postgreSql: "contains null values",
            mySql: "Invalid use of NULL value",
            oracle: "ORA-02296",
            sqlite: "NOT NULL constraint failed"
        );

    /// <summary>重複で主キー・一意制約を張れないときのエラー文の断片（Oracle は主キーと一意制約で番号が違う）</summary>
    private static IReadOnlyDictionary<string, string> DuplicateKeyErrors(string oracle) =>
        ErrorFragments(
            sqlServer: "duplicate key",
            postgreSql: "could not create unique index",
            mySql: "Duplicate entry",
            oracle: oracle,
            sqlite: "UNIQUE constraint failed"
        );

    /// <summary>表 rd_uq (id PK, u NOT NULL, w NOT NULL) と、列 <paramref name="column"/> の名前つきの一意制約 uq_rd_uq</summary>
    private static void RowDependentUniqueTable(ExpectationSchema s, string dialect, string column)
    {
        var type = RealCrossDialectScenarios.IntType(dialect);
        s.Table(
            "rd_uq",
            ExpectationSchema.Pk("id", type),
            ExpectationSchema.NotNull("u", type),
            ExpectationSchema.NotNull("w", type)
        );
        s.Unique("rd_uq", "uq_rd_uq", column);
    }

    /// <summary>
    /// 親 {prefix}_parent(id) と子 {prefix}_child(id, pid〔, v〕) の外部キー fk_{prefix}（型は方言の整数型）
    /// </summary>
    private static void RowDependentParentChild(
        ExpectationSchema s,
        string dialect,
        string prefix,
        bool nullable,
        bool withValue = false,
        ForeignKeyReferentialAction onDelete = ForeignKeyReferentialAction.NoAction
    )
    {
        var type = RealCrossDialectScenarios.IntType(dialect);
        s.Table($"{prefix}_parent", ExpectationSchema.Pk("id", type));
        var child = s.Table(
            $"{prefix}_child",
            ExpectationSchema.Pk("id", type),
            nullable
                ? ExpectationSchema.Nullable("pid", type)
                : ExpectationSchema.NotNull("pid", type)
        );

        if (withValue)
        {
            child.Columns.Add(ExpectationSchema.NotNull("v", type));
        }

        s.Fk($"{prefix}_parent", $"{prefix}_child", $"fk_{prefix}", onDelete, ("id", "pid"));
    }

    /// <summary>表 rd_cs の列 x を参照する計算列 calc を足す文（総当たりの方言の部品と同じ形）</summary>
    private static string RowDependentComputedColumnSql(string dialect) =>
        dialect switch
        {
            DialectNames.SqlServer => "ALTER TABLE [rd_cs] ADD [calc] AS ([x] + 1);",
            DialectNames.PostgreSql =>
                "ALTER TABLE \"rd_cs\" ADD COLUMN \"calc\" integer GENERATED ALWAYS AS (\"x\" + 1) STORED;",
            DialectNames.MySql =>
                "ALTER TABLE `rd_cs` ADD COLUMN `calc` int GENERATED ALWAYS AS (`x` + 1) VIRTUAL;",
            DialectNames.Oracle =>
                "ALTER TABLE \"rd_cs\" ADD (\"calc\" NUMBER(10) GENERATED ALWAYS AS (\"x\" + 1) VIRTUAL);",
            DialectNames.Sqlite =>
                "ALTER TABLE \"rd_cs\" ADD COLUMN \"calc\" INTEGER GENERATED ALWAYS AS (\"x\" + 1) VIRTUAL;",
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, null),
        };

    /// <summary>
    /// 列が空でないと通らない列の変更（宣言 <see cref="SyncDialectCapabilities.PrecisionDecreaseRequiresEmptyColumn"/>＝Oracle の ORA-01440）の
    /// 前後で外して付け直す制約の名指し（途中の状態の外し直しの経路・主キーの変更の経路）
    /// </summary>
    /// <remarks>
    /// 行のある列の精度を下げる。値は新しい型に収まるので、宣言が偽の方言は名指しせずに通る。宣言が真の方言（Oracle）は、外して付け直す
    /// 外部キーと主キーの変更の解除を名指しし、列の変更が断られて名指しした制約だけが外れたまま残る（実 DB の表明 1b の受け入れは、警告が
    /// 名指しした制約だけ）。
    /// </remarks>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealCrossDialectEmptyColumnChangeRows() =>
        [
            Row(
                    "plan-empty-column-change-intermediate-rebuild-names-fk",
                    "[Integration] 外部キーの両端の小数の精度を下げる：列が空でないと精度を下げられない方言だけ、外して付け直す外部キーを名指しする",
                    AllDialects
                )
                .LiveWith(w =>
                    RealCrossDialectScenarios.DecimalParentChild(w.Live, w.Capability, 12)
                )
                .TargetWith(w =>
                    RealCrossDialectScenarios.DecimalParentChild(w.Target, w.Capability, 10)
                )
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .Expect(e => ExpectEmptyColumnChangeWarnings(e, "ec_child", "ec_fk"))
                .OnRealDatabase(r =>
                    RealCrossDialectScenarios
                        .SetupPerDialect(
                            r,
                            d =>
                                RealCrossDialectScenarios.Insert(d, "ec_parent", ["id"], "1.50")
                                + "\n"
                                + RealCrossDialectScenarios.Insert(
                                    d,
                                    "ec_child",
                                    ["id", "parent_id"],
                                    "1, 1.50"
                                )
                        )
                        .Expect(e => ExpectEmptyColumnChangeOutcome(e))
                ),
            Row(
                    "plan-empty-column-change-pk-change-names-primary-key-and-fk",
                    "[Integration] 親の主キーを並べ替え、外部キーの両端の整数の幅を下げる：列が空でないと精度を下げられない方言だけ、主キーと外して付け直す外部キーを名指しする",
                    AllDialects
                )
                .LiveWith(w =>
                    RealCrossDialectScenarios.CompositeParentChild(
                        w.Live,
                        w.Capability,
                        wideA: true,
                        reordered: false
                    )
                )
                .TargetWith(w =>
                    RealCrossDialectScenarios.CompositeParentChild(
                        w.Target,
                        w.Capability,
                        wideA: false,
                        reordered: true
                    )
                )
                .Select(items =>
                    RealScenarios.SelectKinds(
                        items,
                        SchemaDiffKind.AlterColumn,
                        SchemaDiffKind.AlterPrimaryKey
                    )
                )
                .Expect(e =>
                    ExpectEmptyColumnChangeWarnings(
                        e,
                        "ep_child",
                        "ep_fk",
                        primaryKeyTable: "ep_parent"
                    )
                )
                .OnRealDatabase(r =>
                    RealCrossDialectScenarios
                        .SetupPerDialect(
                            r,
                            d =>
                                RealCrossDialectScenarios.Insert(d, "ep_parent", ["a", "b"], "1, 1")
                                + "\n"
                                + RealCrossDialectScenarios.Insert(
                                    d,
                                    "ep_child",
                                    ["id", "pa", "pb"],
                                    "1, 1, 1"
                                )
                        )
                        .Expect(e => ExpectEmptyColumnChangeOutcome(e))
                ),
        ];

    /// <summary>
    /// 宣言が真の方言（Oracle）だけ、外して付け直す外部キー（と主キー）を列が空でないと通らない変更の警告で名指しする（再構築の方言は
    /// 文を逐次に流さないので名指ししない）
    /// </summary>
    private static void ExpectEmptyColumnChangeWarnings(
        ExpectationBuilder e,
        string childTable,
        string foreignKey,
        string? primaryKeyTable = null
    )
    {
        foreach (var dialect in AllDialects)
        {
            var capabilities = SyncPlanExpectationCapabilities.Get(dialect);
            var declared = capabilities.PrecisionDecreaseRequiresEmptyColumn;
            e.For(
                dialect,
                x =>
                {
                    if (!declared)
                    {
                        x.Warnings.Absent("ConstraintRebuildAroundEmptyColumnChange*");

                        // 精度を下げる変更は値が入らないと断られうる文でもあるので、ほかの暗黙コミットの方言は既存の行の警告で名指しする
                        if (capabilities.TransactionalDdl)
                        {
                            x.Warnings.Absent("ConstraintRebuildAroundRowDependentChange*");
                        }
                        else
                        {
                            x.Warnings.Contains(
                                $"ConstraintRebuildAroundRowDependentChange({childTable}): {foreignKey} (*"
                            );
                        }

                        return;
                    }

                    // 同じ文が列が空でないと通らない変更にも当たるときは、その警告だけで名指しする（精度を下げる列の変更＝「旧 → 新」
                    // の文を、既存の行の警告に重ねない。主キーの付与のような別の文は既存の行の警告に出てよい）
                    x.Warnings.Absent("ConstraintRebuildAroundRowDependentChange*→*");
                    x.Warnings.Contains(
                        $"ConstraintRebuildAroundEmptyColumnChange({childTable}): {foreignKey} (*"
                    );

                    if (primaryKeyTable is not null)
                    {
                        x.Warnings.Contains(
                            $"ConstraintRebuildAroundEmptyColumnChange({primaryKeyTable}): PRIMARY KEY (*"
                        );
                    }
                }
            );
        }
    }

    /// <summary>宣言が真の方言（Oracle）は行のある列の精度を下げられずに失敗し、ほかの方言は値が収まるので通って差分が消える</summary>
    private static void ExpectEmptyColumnChangeOutcome(ExpectationBuilder e)
    {
        foreach (var dialect in AllDialects)
        {
            var declared = SyncPlanExpectationCapabilities
                .Get(dialect)
                .PrecisionDecreaseRequiresEmptyColumn;
            e.For(
                dialect,
                x =>
                {
                    if (declared)
                    {
                        x.Outcome.Exact("failed: *ORA-01440*");
                        return;
                    }

                    x.Outcome.Exact("committed");
                    x.AfterDiff.Empty();
                }
            );
        }
    }

    /// <summary>
    /// 外部キーの両端の整数の幅の違い（宣言 <see cref="SyncDialectCapabilities.ForeignKeyAllowsIntegerWidthDifference"/> が真の方言の固定。
    /// 偽の方言＝SQL Server・MySQL は、同じ形が警告して失敗する行 <c>realdb-fk-type-mismatch-one-end-warns-and-fails</c>・
    /// <c>realdb-mysql-fk-type-mismatch-one-end-warns-and-keeps-fk</c> が固定する）
    /// </summary>
    /// <remarks>
    /// 子の列だけを 64 ビット整数へ変えると、両端の幅が違う外部キーになる。宣言が真の方言では食い違いとみなさず、名指しせずに
    /// 同期し、DB が受け入れる（外部キーを張ったまま列を変える PostgreSQL・Oracle と、表を作り直す SQLite）。宣言が DB の事実と
    /// 食い違えば、警告のない失敗（表明 1）で赤になる。
    /// </remarks>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealCrossDialectIntegerWidthRows() =>
        [
            Row(
                    "realdb-fk-integer-width-difference-is-accepted-without-warning",
                    "[Integration] 外部キーの子の列だけを幅の広い整数へ変えると、整数の幅の違いを許す方言では名指しせずに同期でき、DB が受け入れる",
                    DialectNames.PostgreSql,
                    DialectNames.Oracle,
                    DialectNames.Sqlite
                )
                .LiveWith(w => RealCrossDialectScenarios.IntegerParentChild(w.Live, w.Capability))
                .TargetWith(w =>
                {
                    RealCrossDialectScenarios.IntegerParentChild(w.Target, w.Capability);
                    w.Target.Col("iw_child", "parent_id").DataType =
                        RealCrossDialectScenarios.BigIntType(w.Capability);
                })
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.AlterColumn))
                .OnRealDatabase(r =>
                    r.Setup(
                            "INSERT INTO \"iw_parent\" (\"id\") VALUES (1);\n"
                                + "INSERT INTO \"iw_child\" (\"id\", \"parent_id\") VALUES (1, 1);"
                        )
                        .Expect(e =>
                        {
                            e.Warnings.Absent("ForeignKeyColumnTypeMismatch*");
                            e.Outcome.Exact("committed");
                            e.After.Count("fk *", 1);
                            e.AfterDiff.Empty();
                        })
                ),
        ];

    /// <summary>NOT NULL の子の列への SET NULL の外部キー（作れる方言で、親の削除が NOT NULL の違反で失敗する）</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealCrossDialectSetNullRows() =>
        [
            Row(
                    "realdb-set-null-not-null-child-ddl-applies-delete-fails",
                    "[Integration] NOT NULL の子の列への SET NULL の外部キーは作れるが、親の削除は失敗する（PostgreSQL 23502・Oracle ORA-01407・SQLite 19）",
                    DialectNames.PostgreSql,
                    DialectNames.Oracle,
                    DialectNames.Sqlite
                )
                .LiveWith(w => RealCrossDialectScenarios.SetNullOnNotNull(w.Live, w.Capability))
                .TargetWith(w => RealCrossDialectScenarios.SetNullOnNotNull(w.Target, w.Capability))
                .Select(items => RealScenarios.SelectKinds(items))
                .OnRealDatabase(r =>
                    r.Setup(
                            "INSERT INTO \"sn_parent\" (\"id\") VALUES (1);\n"
                                + "INSERT INTO \"sn_child\" (\"id\", \"parent_id\") VALUES (1, 1);"
                        )
                        .Query("delete-parent", "DELETE FROM \"sn_parent\" WHERE \"id\" = 1")
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.Before.Single(
                                "fk sn_child * (parent_id) -> sn_parent(id) del=SetNull *"
                            );
                            e.For(
                                DialectNames.PostgreSql,
                                x =>
                                    x.Probe("delete-parent")
                                        .Exact("error PostgresException: 23502: *")
                            );
                            e.For(
                                DialectNames.Oracle,
                                x =>
                                    x.Probe("delete-parent")
                                        .Exact("error OracleException: ORA-01407: *")
                            );
                            e.For(
                                DialectNames.Sqlite,
                                x =>
                                    x.Probe("delete-parent")
                                        .Exact("error SqliteException: SQLite Error 19: *")
                            );
                        })
                ),
        ];

    /// <summary>主キー・一意制約の構成列の削除（前提にする変更を選ばない／選ぶ）</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealCrossDialectDropMemberColumnRows() =>
        [
            Row(
                    "realdb-drop-member-column-drops-only-keeps-pk-and-drops-unique",
                    "[Integration] 同期: 主キーの列の削除は外し、一意制約の構成列の削除は一意制約ごと外して成功する",
                    AllDialects
                )
                .Live(RealCrossDialectScenarios.DropMemberLive)
                .Target(RealCrossDialectScenarios.DropMemberTarget)
                .Select(items => RealScenarios.SelectKinds(items, SchemaDiffKind.DropColumn))
                .OnRealDatabase(r =>
                    RealCrossDialectScenarios
                        .SetupPerDialect(
                            r,
                            d =>
                                RealCrossDialectScenarios.Insert(
                                    d,
                                    "t",
                                    ["a", "b", "c", "d"],
                                    "1, 1, 1, 1"
                                )
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.Warnings.Single(
                                "SelectedItemRequiresUnselectedItem(*)*",
                                "SelectedItemRequiresUnselectedItem(*): b (PRIMARY KEY)"
                            );
                            e.Warnings.Single(
                                "UniqueConstraintDroppedWithColumn(*)*",
                                "UniqueConstraintDroppedWithColumn(*): *(c, d)"
                            );
                            e.After.Exact(
                                "table t",
                                "col t.a *",
                                "col t.b *",
                                "col t.c *",
                                "pk t (a,b)"
                            );
                        })
                ),
            Row(
                    "realdb-drop-member-column-with-pk-change-drops-pk-column",
                    "[Integration] 同期: その列を外す主キーの変更も選べば、主キーの列を削除して成功する",
                    AllDialects
                )
                .Live(RealCrossDialectScenarios.DropMemberLive)
                .Target(RealCrossDialectScenarios.DropMemberTarget)
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected =
                            item.Kind == SchemaDiffKind.AlterPrimaryKey
                            || item is { Kind: SchemaDiffKind.DropColumn, ColumnName: "b" };
                    }
                })
                .OnRealDatabase(r =>
                    RealCrossDialectScenarios
                        .SetupPerDialect(
                            r,
                            d =>
                                RealCrossDialectScenarios.Insert(
                                    d,
                                    "t",
                                    ["a", "b", "c", "d"],
                                    "1, 1, 1, 1"
                                )
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");

                            // 新しい主キー (a) は live のキー (a, b) の列集合を含まないので、暗黙コミットの方言は行の重複で付与が
                            // 断られうる主キーを名指しする
                            foreach (var dialect in AllDialects)
                            {
                                if (SyncPlanExpectationCapabilities.Get(dialect).TransactionalDdl)
                                {
                                    e.For(dialect, x => x.Warnings.Empty());
                                }
                                else
                                {
                                    e.For(
                                        dialect,
                                        x =>
                                            x.Warnings.Exact(
                                                "ConstraintRebuildAroundRowDependentChange(t): PRIMARY KEY (PRIMARY KEY t (a))"
                                            )
                                    );
                                }
                            }

                            e.After.Exact(
                                "table t",
                                "col t.a *",
                                "col t.c *",
                                "col t.d *",
                                "pk t (a)",
                                "uq t *"
                            );
                        })
                ),
        ];

    /// <summary>図で列の無い表の追加（5 方言とも外して名指しし、ほかの表の追加は実行する）</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealCrossDialectTableWithoutColumnsRows() =>
        [
            Row(
                    "realdb-add-table-without-columns-is-excluded-and-other-tables-are-created",
                    "[Integration] 同期: 図で列の無い表の追加は外して名指しし、ほかの表の追加は実行して成功する（PostgreSQL も列の無い表を作らない）",
                    AllDialects
                )
                .LiveWith(w =>
                    TableWithoutColumnsLive(w.Live, RealCrossDialectScenarios.IntType(w.Capability))
                )
                .TargetWith(w =>
                    TableWithoutColumnsTarget(
                        w.Target,
                        RealCrossDialectScenarios.IntType(w.Capability)
                    )
                )
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Outcome.Exact("committed");
                        e.After.Unordered(
                            "table a",
                            "col a.id *",
                            "pk a (id)",
                            "table orders",
                            "col orders.id *",
                            "pk orders (id)"
                        );
                        e.AfterDiff.Single("AddTable empty *");

                        // 説明を持つ方言では、外した表の追加を前提にする表の説明も外して名指しする（SQLite は説明を持たない）
                        foreach (var dialect in SequentialDropMemberColumnDialects)
                        {
                            e.For(
                                dialect,
                                x =>
                                    x.Warnings.Unordered(
                                        "TableWithoutColumns(empty)",
                                        "SelectedItemRequiresUnselectedItem(empty): COMMENT (*)"
                                    )
                            );
                        }

                        e.For(
                            DialectNames.Sqlite,
                            x => x.Warnings.Exact("TableWithoutColumns(empty)")
                        );
                    })
                ),
        ];

    /// <summary>表の名前の変更と一意制約の名前の段（5 方言）</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RealCrossDialectNameRows() =>
        [
            Row(
                    "realdb-rename-table-keeping-unique-name-drops-old-table-first",
                    "[Integration] 同期: 表の名前を変えて一意制約の名前を引き継いでも、古い表を先に消して成功する",
                    AllDialects
                )
                .LiveWith(w => RealCrossDialectScenarios.Items(w.Live, "old_items", w.Capability))
                .TargetWith(w => RealCrossDialectScenarios.Items(w.Target, "items", w.Capability))
                .Select(RealCrossDialectScenarios.SelectSelectable)
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Outcome.Exact("committed");
                        e.After.Count("table *", 1);
                        e.After.Single("table items");
                        e.AfterDiff.Absent("* able=1 *");
                    })
                ),
            Row(
                    "realdb-uq-name-step-change-named-and-add-unnamed-on-old-columns",
                    "[Integration] 同期: 名前を保って一意制約の列を変え、元の列に名前の無い一意制約を足しても成功する",
                    AllDialects
                )
                .LiveWith(w =>
                    RealCrossDialectScenarios.UniqueStep(w.Live, w.Capability, ("UQ_x", "a"))
                )
                .TargetWith(w =>
                    RealCrossDialectScenarios.UniqueStep(
                        w.Target,
                        w.Capability,
                        ("UQ_x", "b"),
                        (null, "a")
                    )
                )
                .Select(RealCrossDialectScenarios.SelectSelectable)
                .OnRealDatabase(r =>
                    RealCrossDialectScenarios
                        .SetupPerDialect(
                            r,
                            d =>
                                RealCrossDialectScenarios.Insert(
                                    d,
                                    "t",
                                    ["id", "a", "b"],
                                    "1, 1, 1"
                                )
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");
                            e.After.Count("uq *", 2);
                            e.After.Single("uq t * (a)", "uq t * (b)");
                            e.AfterDiff.Absent("* able=1 *");
                        })
                ),
            Row(
                    "realdb-uq-name-step-change-named-and-add-unnamed-on-old-columns-default-selection",
                    "[Integration] 同期: 名前を保って一意制約の列を変え、元の列に名前の無い一意制約を足した図を既定の選択のまま流しても、同じ列に一意制約が重ならない",
                    AllDialects
                )
                .LiveWith(w =>
                    RealCrossDialectScenarios.UniqueStep(w.Live, w.Capability, ("UQ_x", "a"))
                )
                .TargetWith(w =>
                    RealCrossDialectScenarios.UniqueStep(
                        w.Target,
                        w.Capability,
                        ("UQ_x", "b"),
                        (null, "a")
                    )
                )
                .OnRealDatabase(r =>
                    RealCrossDialectScenarios
                        .SetupPerDialect(
                            r,
                            d =>
                                RealCrossDialectScenarios.Insert(
                                    d,
                                    "t",
                                    ["id", "a", "b"],
                                    "1, 1, 1"
                                )
                        )
                        .Expect(e =>
                        {
                            e.Outcome.Exact("committed");

                            foreach (var dialect in AllDialects)
                            {
                                // SQLite は取込が一意制約の名前を持たない（sqlite_autoindex）ので名前の段で組まず、元の列 (a) の
                                // 制約は列集合の段で満たされ、(b) の追加だけが既定で選ばれて通る
                                e.For(
                                    dialect,
                                    x =>
                                    {
                                        if (dialect == DialectNames.Sqlite)
                                        {
                                            x.Warnings.Absent(
                                                "SelectedItemRequiresUnselectedItem*"
                                            );
                                            x.After.Single("uq t * (a)", "uq t * (b)");
                                            return;
                                        }

                                        x.Warnings.Single(
                                            "SelectedItemRequiresUnselectedItem(t): * (UQ_x)"
                                        );
                                        x.After.Count("uq *", 1);
                                        x.After.Single("uq t * (a)");
                                    }
                                );
                            }
                        })
                ),
        ];

    /// <summary>空白だけの説明と、説明の BMP の外の文字（5 方言）</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealCrossDialectDescriptionRows() =>
        [
            BlankDescriptionRoundTripRow("blank-target", "  ", " \t "),
            BlankDescriptionRoundTripRow("empty-target", string.Empty, string.Empty),
            Row(
                    "realdb-blank-description-sync-removes-descriptions",
                    "[Integration] 説明のある DB を空白だけの説明の図へ同期すると、説明が消えて差分が出ない",
                    AllDialects
                )
                .LiveWith(w =>
                    RealCrossDialectScenarios.Described(
                        w.Live,
                        w.Capability,
                        "table note",
                        "column note"
                    )
                )
                .TargetWith(w =>
                    RealCrossDialectScenarios.Described(w.Target, w.Capability, "  ", " \t ")
                )
                .Select(RealCrossDialectScenarios.SelectSelectable)
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Outcome.Exact("committed");
                        e.After.Absent("desc *");
                        e.AfterDiff.Absent("* able=1 *");
                    })
                ),
            SupplementaryDdlRoundTripRow(withEmoji: true),
            SupplementaryDdlRoundTripRow(withEmoji: false),
            SupplementarySyncAddingRow(withEmoji: true),
            SupplementarySyncAddingRow(withEmoji: false),
        ];

    /// <summary>空白だけの説明の図の DDL を当てて取り込み、目標の説明（空白だけ・空）の図との差分に選べる項目が無い</summary>
    private static ScenarioRow<SyncPlanExpectationInput> BlankDescriptionRoundTripRow(
        string shape,
        string tableDescription,
        string columnDescription
    ) =>
        Row(
                "realdb-blank-description-ddl-round-trip-no-diff-" + shape,
                "[Integration] 空白だけの説明の図は、DDL を適用して取り込み直しても差分が出ない",
                AllDialects
            )
            .LiveWith(w => RealCrossDialectScenarios.Described(w.Live, w.Capability, "  ", " \t "))
            .TargetWith(w =>
                RealCrossDialectScenarios.Described(
                    w.Target,
                    w.Capability,
                    tableDescription,
                    columnDescription
                )
            )
            .Select(items => RealScenarios.SelectKinds(items))
            .OnRealDatabase(r =>
                r.Expect(e =>
                {
                    e.Diff.Absent("* able=1 *");
                    e.Outcome.Exact("committed");
                })
            );

    /// <summary>
    /// 説明の図の DDL を当てて取り込むと、説明は方言が保存する形で、元の図との差分に選べる項目が無い
    /// </summary>
    private static ScenarioRow<SyncPlanExpectationInput> SupplementaryDdlRoundTripRow(
        bool withEmoji
    )
    {
        var description = RealCrossDialectScenarios.SupplementaryDescription(withEmoji);
        return Row(
                "realdb-description-supplementary-ddl-round-trip-no-diff-"
                    + (withEmoji ? "emoji" : "bmp"),
                "[Integration] 説明の BMP の外の文字: DDL を適用して取り込み直しても差分が出ず、注記は宣言どおり",
                AllDialects
            )
            .LiveWith(w =>
                RealCrossDialectScenarios.Described(w.Live, w.Capability, description, description)
            )
            .TargetWith(w =>
                RealCrossDialectScenarios.Described(
                    w.Target,
                    w.Capability,
                    description,
                    description
                )
            )
            .Select(items => RealScenarios.SelectKinds(items))
            .OnRealDatabase(r =>
                r.Expect(e =>
                {
                    e.Diff.Absent("* able=1 *");
                    e.Outcome.Exact("committed");

                    foreach (var dialect in AllDialects)
                    {
                        e.For(
                            dialect,
                            x =>
                                RealCrossDialectScenarios.ExpectStoredDescriptions(
                                    x.Before,
                                    dialect,
                                    description
                                )
                        );
                    }
                })
            );
    }

    /// <summary>
    /// 説明の無い DB へ説明を足す同期は、保存できない方言でだけ警告し、取り込み直した説明は保存する形で、差分に選べる項目が無い
    /// </summary>
    private static ScenarioRow<SyncPlanExpectationInput> SupplementarySyncAddingRow(bool withEmoji)
    {
        var description = RealCrossDialectScenarios.SupplementaryDescription(withEmoji);
        return Row(
                "realdb-description-supplementary-sync-adding-warns-no-diff-"
                    + (withEmoji ? "emoji" : "bmp"),
                "[Integration] 説明の BMP の外の文字: 説明を足す同期は宣言どおりに警告し、直後に偽の差分が出ない",
                AllDialects
            )
            .LiveWith(w =>
                RealCrossDialectScenarios.Described(
                    w.Live,
                    w.Capability,
                    string.Empty,
                    string.Empty
                )
            )
            .TargetWith(w =>
                RealCrossDialectScenarios.Described(
                    w.Target,
                    w.Capability,
                    description,
                    description
                )
            )
            .Select(RealCrossDialectScenarios.SelectSelectable)
            .OnRealDatabase(r =>
                r.Expect(e =>
                {
                    e.Outcome.Exact("committed");
                    e.AfterDiff.Absent("* able=1 *");

                    foreach (var dialect in AllDialects)
                    {
                        var capabilities = SyncPlanExpectationCapabilities.Get(dialect);
                        var folds =
                            withEmoji
                            && capabilities.SupportsDescriptions
                            && !capabilities
                                .IdentifierLengthLimit
                                .RepresentsSupplementaryCharacters;
                        e.For(
                            dialect,
                            x =>
                            {
                                if (folds)
                                {
                                    x.Warnings.Exact(
                                        "DescriptionCharactersReplaced(*): t",
                                        "DescriptionCharactersReplaced(*): t.v"
                                    );
                                }
                                else
                                {
                                    x.Warnings.Absent("DescriptionCharactersReplaced*");
                                }

                                RealCrossDialectScenarios.ExpectStoredDescriptions(
                                    x.After,
                                    dialect,
                                    description
                                );
                            }
                        );
                    }
                })
            );
    }

    /// <summary>既定の制約名が上限を超える図（上限のある 4 方言）と、PostgreSQL の旧版が切り詰めた外部キー名の吸収</summary>
    private static IEnumerable<
        ScenarioRow<SyncPlanExpectationInput>
    > RealCrossDialectLongDefaultNameRows() =>
        [
            Row(
                    "realdb-long-default-names-apply-and-round-trip-no-diff",
                    "[Integration] 既定名が上限を超える図の DDL は適用でき、制約名は上限以内で、取り込み直すと差分が出ない",
                    DialectNames.SqlServer,
                    DialectNames.PostgreSql,
                    DialectNames.MySql,
                    DialectNames.Oracle
                )
                .LiveWith(w => RealCrossDialectScenarios.LongNames(w.Live, w.Capability))
                .TargetWith(w => RealCrossDialectScenarios.LongNames(w.Target, w.Capability))
                .Select(items => RealScenarios.SelectKinds(items))
                .OnRealDatabase(r =>
                    r.Expect(e =>
                    {
                        e.Diff.Empty();
                        e.Outcome.Exact("committed");
                        e.Before.Count("fk *", 2);

                        foreach (
                            var dialect in new[]
                            {
                                DialectNames.SqlServer,
                                DialectNames.PostgreSql,
                                DialectNames.MySql,
                                DialectNames.Oracle,
                            }
                        )
                        {
                            var limit = SyncPlanExpectationCapabilities
                                .Get(dialect)
                                .IdentifierLengthLimit;
                            var diagram = LongNameDiagrams.Build(limit, dialect);
                            var child = diagram.Entities[1].TableName;
                            var names = ConstraintNames
                                .ResolveForeignKeyNames(
                                    diagram.Entities,
                                    diagram.Relationships,
                                    limit
                                )
                                .Values.ToList();
                            e.For(
                                dialect,
                                x =>
                                {
                                    foreach (var name in names)
                                    {
                                        x.Before.Single($"fk {child} {name} *");
                                    }
                                }
                            );
                        }
                    })
                ),
            Row(
                    "realdb-long-default-pg-legacy-truncated-fk-name-is-absorbed",
                    "[Integration] PostgreSQL: 旧版の DDL で作った長い既定名の外部キー（PG が切り詰めた名前）は、取り込み直しても差分が出ない",
                    DialectNames.PostgreSql
                )
                .TargetWith(w =>
                    RealCrossDialectScenarios.Adopt(
                        w.Target,
                        LongNameDiagrams.BuildSingleForeignKey(40, DialectNames.PostgreSql)
                    )
                )
                .Select(items => RealScenarios.SelectKinds(items))
                .OnRealDatabase(r =>
                    r.LiveSql(RealCrossDialectScenarios.LegacyPostgreSqlLongNameSql)
                        .Expect(e =>
                        {
                            var limit = SyncPlanExpectationCapabilities
                                .Get(DialectNames.PostgreSql)
                                .IdentifierLengthLimit;
                            var (parent, child) = RealCrossDialectScenarios.LegacyTables;
                            var legacyName = "FK_" + child + "_" + parent;
                            e.Before.Count("fk *", 1);
                            e.Before.Single($"fk {child} {limit.Truncate(legacyName)} *");
                            e.Before.Absent(
                                $"fk * {ConstraintNames.ForeignKey(child, parent, limit)} *"
                            );
                            e.Diff.Empty();
                            e.Outcome.Exact("committed");
                        })
                ),
        ];
}

/// <summary>方言をまたぐ実 DB の段の行の図・SQL・選び方の部品</summary>
internal static class RealCrossDialectScenarios
{
    /// <summary>
    /// 方言の整数型の綴り（総当たりの方言の部品 <see cref="SyncPlanFuzzDialect.TypeName"/> の <see cref="FuzzTypeKind.Int"/> と同じ＝取り込み直した綴り）
    /// </summary>
    /// <summary>方言の 64 ビット整数の型（Oracle は小数部の無い NUMBER の桁）</summary>
    public static string BigIntType(string dialect) =>
        dialect switch
        {
            DialectNames.SqlServer => "bigint",
            DialectNames.PostgreSql => "bigint",
            DialectNames.MySql => "bigint",
            DialectNames.Oracle => "NUMBER(19)",
            DialectNames.Sqlite => "BIGINT",
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, null),
        };

    /// <summary>方言の固定小数点数の型（スケール 2）</summary>
    public static string DecimalType(string dialect, int precision) =>
        dialect switch
        {
            DialectNames.SqlServer or DialectNames.MySql => $"decimal({precision},2)",
            DialectNames.PostgreSql => $"numeric({precision},2)",
            DialectNames.Oracle => $"NUMBER({precision},2)",
            DialectNames.Sqlite => $"DECIMAL({precision},2)",
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, null),
        };

    /// <summary>親 ec_parent(id) と、それを参照する子 ec_child(parent_id)（外部キー ec_fk・型は方言の固定小数点数）</summary>
    public static void DecimalParentChild(ExpectationSchema s, string dialect, int precision)
    {
        var type = DecimalType(dialect, precision);
        s.Table("ec_parent", ExpectationSchema.Pk("id", type));
        s.Table(
            "ec_child",
            ExpectationSchema.Pk("id", IntType(dialect)),
            ExpectationSchema.NotNull("parent_id", type)
        );
        s.Fk("ec_parent", "ec_child", "ec_fk", ("id", "parent_id"));
    }

    /// <summary>
    /// 複合主キー ep_parent(a, b) と、それを参照する子 ep_child(pa, pb)（外部キー ep_fk）。<paramref name="wideA"/> なら a と pa を
    /// 64 ビット整数、偽なら 32 ビット整数にする（b と pb は 32 ビット整数）。<paramref name="reordered"/> なら主キーを (b, a) の順にする
    /// </summary>
    public static void CompositeParentChild(
        ExpectationSchema s,
        string dialect,
        bool wideA,
        bool reordered
    )
    {
        var a = wideA ? BigIntType(dialect) : IntType(dialect);
        var b = IntType(dialect);
        s.Table("ep_parent", ExpectationSchema.Pk("a", a), ExpectationSchema.Pk("b", b));
        s.Table(
            "ep_child",
            ExpectationSchema.Pk("id", b),
            ExpectationSchema.NotNull("pa", a),
            ExpectationSchema.NotNull("pb", b)
        );
        s.Fk("ep_parent", "ep_child", "ep_fk", ("a", "pa"), ("b", "pb"));

        if (reordered)
        {
            s.PrimaryKeyOrder("ep_parent", "b", "a");
        }
    }

    /// <summary>親 iw_parent(id) と、それを参照する子 iw_child(parent_id)（外部キー iw_fk・型は方言の 32 ビット整数）</summary>
    public static void IntegerParentChild(ExpectationSchema s, string dialect)
    {
        var type = IntType(dialect);
        s.Table("iw_parent", ExpectationSchema.Pk("id", type));
        s.Table(
            "iw_child",
            ExpectationSchema.Pk("id", type),
            ExpectationSchema.NotNull("parent_id", type)
        );
        s.Fk("iw_parent", "iw_child", "iw_fk", ("id", "parent_id"));
    }

    public static string IntType(string dialect) =>
        dialect switch
        {
            DialectNames.SqlServer => "int",
            DialectNames.PostgreSql => "integer",
            DialectNames.MySql => "int",
            DialectNames.Oracle => "NUMBER(10)",
            DialectNames.Sqlite => "INTEGER",
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, null),
        };

    /// <summary>方言の識別子の引用（総当たりの方言の部品 <see cref="SyncPlanFuzzDialect.Quote"/> と同じ）</summary>
    public static string Quote(string dialect, string identifier) =>
        dialect switch
        {
            DialectNames.SqlServer => $"[{identifier.Replace("]", "]]")}]",
            DialectNames.MySql => $"`{identifier.Replace("`", "``")}`",
            _ => $"\"{identifier.Replace("\"", "\"\"")}\"",
        };

    /// <summary>識別子を方言で引用した 1 行の INSERT</summary>
    public static string Insert(string dialect, string table, string[] columns, string values) =>
        $"INSERT INTO {Quote(dialect, table)} ({string.Join(", ", columns.Select(c => Quote(dialect, c)))}) VALUES ({values});";

    /// <summary>方言ごとに同じ名前の問い合わせを足す（5 方言・面は <c>probe:{名前}</c>）</summary>
    public static RealStage QueryPerDialect(RealStage stage, string name, Func<string, string> sql)
    {
        foreach (var dialect in SyncPlanExpectationCapabilities.AllDialects)
        {
            stage.Query(name, sql(dialect), dialect);
        }

        return stage;
    }

    /// <summary>方言ごとに同じ形の DDL の後の文を足す（5 方言）</summary>
    public static RealStage SetupPerDialect(RealStage stage, Func<string, string> sql)
    {
        foreach (var dialect in SyncPlanExpectationCapabilities.AllDialects)
        {
            stage.Setup(sql(dialect), dialect);
        }

        return stage;
    }

    /// <summary>選べる項目だけを選ぶ（選べない案内の項目は選ばない）</summary>
    public static void SelectSelectable(IReadOnlyList<SchemaDiffItem> items)
    {
        foreach (var item in items)
        {
            item.IsSelected = item.IsSelectable;
        }
    }

    /// <summary>
    /// 親 sn_parent(id) ← 子 sn_child(id, parent_id NOT NULL) の ON DELETE SET NULL の外部キー sn_fk（型は方言の整数型）
    /// </summary>
    public static void SetNullOnNotNull(ExpectationSchema s, string dialect)
    {
        var type = IntType(dialect);
        s.Table("sn_parent", ExpectationSchema.Pk("id", type));
        s.Table(
            "sn_child",
            ExpectationSchema.Pk("id", type),
            ExpectationSchema.NotNull("parent_id", type)
        );
        s.Fk(
            "sn_parent",
            "sn_child",
            "sn_fk",
            ForeignKeyReferentialAction.SetNull,
            ("id", "parent_id")
        );
    }

    /// <summary>DB 側の表 t(a PK, b PK, c, d, UNIQUE UQ_t_cd (c, d))（型は INT・c と d は NULL 許容）</summary>
    public static void DropMemberLive(ExpectationSchema s)
    {
        s.Tbl(
            "t",
            ("a", "INT", true),
            ("b", "INT", true),
            ("c", "INT", false),
            ("d", "INT", false)
        );
        s.Unique("t", "UQ_t_cd", "c", "d");
    }

    /// <summary>目標の表 t(a PK, c)（DB 側と独立に組む）</summary>
    public static void DropMemberTarget(ExpectationSchema s) =>
        s.Tbl("t", ("a", "INT", true), ("c", "INT", false));

    /// <summary>表 (id PK, code NOT NULL, UNIQUE UQ_items_code (code))（型は方言の整数型）</summary>
    public static void Items(ExpectationSchema s, string table, string dialect)
    {
        var type = IntType(dialect);
        s.Table(table, ExpectationSchema.Pk("id", type), ExpectationSchema.NotNull("code", type));
        s.Unique(table, "UQ_items_code", "code");
    }

    /// <summary>表 t (id PK, a NOT NULL, b NOT NULL) に一意制約を付けたもの（名前・列の組。型は方言の整数型）</summary>
    public static void UniqueStep(
        ExpectationSchema s,
        string dialect,
        params (string? Name, string Column)[] uniques
    )
    {
        var type = IntType(dialect);
        s.Table(
            "t",
            ExpectationSchema.Pk("id", type),
            ExpectationSchema.NotNull("a", type),
            ExpectationSchema.NotNull("b", type)
        );

        foreach (var (name, column) in uniques)
        {
            s.Unique("t", name, column);
        }
    }

    /// <summary>表 t (id PK, v NULL 許容) に表と列 v の説明を付けたもの（型は方言の整数型）</summary>
    public static void Described(
        ExpectationSchema s,
        string dialect,
        string tableDescription,
        string columnDescription
    )
    {
        var type = IntType(dialect);
        var v = ExpectationSchema.Nullable("v", type);
        v.Description = columnDescription;
        s.Table("t", ExpectationSchema.Pk("id", type), v).Description = tableDescription;
    }

    /// <summary>説明の文字（絵文字のサロゲート対を含むもの／BMP の中だけの対照）</summary>
    public static string SupplementaryDescription(bool withEmoji) =>
        withEmoji ? "a\U0001F600b\U0001F600\U0001F600c" : "a\u00E9b\u3042c";

    /// <summary>
    /// 取り込んだ表 t と列 v の説明が、方言が保存する形（<see cref="IdentifierLengthLimit.StoredText"/>）と一致する（説明の機構の無い方言では説明の行が無い）
    /// </summary>
    public static void ExpectStoredDescriptions(
        FacetExpectations facet,
        string dialect,
        string description
    )
    {
        var capabilities = SyncPlanExpectationCapabilities.Get(dialect);

        if (!capabilities.SupportsDescriptions)
        {
            facet.Absent("desc *");
            return;
        }

        var stored = capabilities.IdentifierLengthLimit.StoredText(description);
        facet.Count("desc *", 2);
        facet.Single($"desc t \"{stored}\"", $"desc t.v \"{stored}\"");
    }

    /// <summary>表の名前が方言の上限ちょうどの親子と、名前の無い外部キー 2 本・名前の無い一意制約の図</summary>
    public static void LongNames(ExpectationSchema s, string dialect) =>
        Adopt(
            s,
            LongNameDiagrams.Build(
                SyncPlanExpectationCapabilities.Get(dialect).IdentifierLengthLimit,
                dialect
            )
        );

    /// <summary>図の表とリレーションをそのまま自分の中身にする</summary>
    public static void Adopt(ExpectationSchema s, ErDiagram diagram) =>
        s.Adopt(diagram.Entities, diagram.Relationships);

    /// <summary>旧版の DDL を再現する表（親・子とも 40 文字）</summary>
    public static (string Parent, string Child) LegacyTables =>
        (new string('p', 40), new string('c', 40));

    /// <summary>
    /// 旧版の QuickER が書いた DDL と同じ形（外部キーの既定名を縮めずに書く＝PostgreSQL が 63 バイトで切り詰めて保存する）
    /// </summary>
    public static string LegacyPostgreSqlLongNameSql
    {
        get
        {
            var (parent, child) = LegacyTables;
            var legacyName = "FK_" + child + "_" + parent;
            return $"CREATE TABLE {PgIdentifier.Quote(parent)} (id integer NOT NULL, CONSTRAINT \"PK_{parent}\" PRIMARY KEY (id));\n"
                + $"CREATE TABLE {PgIdentifier.Quote(child)} (id integer NOT NULL, parent_id integer NOT NULL, CONSTRAINT \"PK_{child}\" PRIMARY KEY (id));\n"
                + $"ALTER TABLE {PgIdentifier.Quote(child)} ADD CONSTRAINT \"{legacyName}\" FOREIGN KEY (parent_id) REFERENCES {PgIdentifier.Quote(parent)} (id);";
        }
    }
}
