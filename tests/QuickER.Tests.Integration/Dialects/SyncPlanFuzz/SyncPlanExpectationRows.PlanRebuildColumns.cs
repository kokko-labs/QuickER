using System;
using System.Collections.Generic;
using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;
using I = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationItems;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 計画の段の期待の行（テーブル再構築の列：失われる列の属性の警告・主キーの順序の張り直し・live の列を保つ合成・候補キーの喪失の警告）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    /// <summary>再構築の列の属性の警告（形, 説明, DB 側の表 others も持つか, 表ごとの CREATE 文, 期待する警告）</summary>
    private static (
        string Shape,
        string Description,
        bool WithOthers,
        (string Table, string Sql)[] CreateSql,
        string? Warning
    )[] RebuildAttributeWarnings =>
        [
            (
                "warns-dropped-column-attributes",
                "再構築: live DDL の列属性を警告する",
                false,
                [
                    (
                        "items",
                        "CREATE TABLE items (id INTEGER PRIMARY KEY AUTOINCREMENT, note TEXT DEFAULT 'x')"
                    ),
                ],
                "TableRebuildDropsColumnAttribute(items): AUTOINCREMENT, DEFAULT"
            ),
            (
                "no-warning-without-attributes",
                "再構築: 属性のない DDL では警告しない",
                false,
                [("items", "CREATE TABLE items (id INTEGER, note TEXT, PRIMARY KEY(id))")],
                null
            ),
            (
                "no-warning-for-unrelated-tables",
                "再構築: 対象外テーブルの属性は警告しない",
                true,
                [
                    ("items", "CREATE TABLE items (id INTEGER, note TEXT, PRIMARY KEY(id))"),
                    ("others", "CREATE TABLE others (id INTEGER PRIMARY KEY AUTOINCREMENT)"),
                ],
                null
            ),
            (
                "no-warning-without-live-ddl",
                "再構築: live DDL が無ければ警告しない",
                false,
                [],
                null
            ),
        ];

    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanRebuildColumnRows() =>
        [
            .. RebuildAttributeRows(),
            Row(
                    "plan-rebuild-attribute-no-warning-for-create-only",
                    "再構築: 新規テーブル作成では警告しない",
                    DialectNames.Sqlite
                )
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddTable,
                            TableName = "fresh",
                            Entity = S.Detached("fresh", S.Pk("id", "INT")),
                            IsSelected = true,
                        },
                    ]
                )
                .Context(_ => new SyncPlanContext
                {
                    TableCreateSql = new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase
                    )
                    {
                        ["fresh"] = "CREATE TABLE fresh (id INTEGER PRIMARY KEY AUTOINCREMENT)",
                    },
                })
                .Expect(e =>
                    e.Rebuilds.Count("* cols *", 1)
                        .Rebuilds.Single("fresh create-only")
                        .Warnings.Empty()
                ),
            Row(
                    "plan-rebuild-selected-alter-pk-remaps-pk-order",
                    "再構築: 選択された AlterPrimaryKey は主キー順序を target の実効順へ張り直す",
                    DialectNames.Sqlite
                )
                // live の主キー順は (a, b)、目標は (b, a)
                .Live(s => s.Add(ExpectationScenarios.PairTable(["a", "b"])))
                .Items(_ => [I.AlterPk("pair", ExpectationScenarios.PairTable(["b", "a"]))])
                // 順序の並びは合成後の定義の列を指す
                .Expect(e =>
                    e.Rebuilds.Count("* cols *", 1)
                        .Rebuilds.Single("pair pk (b,a)", "pair pkids (b,a)")
                ),
            Row(
                    "plan-rebuild-unselected-alter-pk-keeps-live-pk-order",
                    "再構築: AlterPrimaryKey が未選択なら主キー順序は live のまま",
                    DialectNames.Sqlite
                )
                // live の主キー順は (b, a)。目標の (a, b) は選択されていないので当たらない
                .Live(s => s.Add(ExpectationScenarios.PairTable(["b", "a"])))
                .Items(_ =>
                    [
                        I.AlterPk(
                            "pair",
                            ExpectationScenarios.PairTable(["a", "b"]),
                            selected: false
                        ),
                        // 再構築そのものは別の選択済みの差分（通常の列の型の変更）で起こす
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "pair",
                            ColumnName = "memo",
                            Column = S.Nullable("memo", "NVARCHAR(200)"),
                            IsSelected = true,
                        },
                    ]
                )
                .Expect(e => e.Rebuilds.Count("* cols *", 1).Rebuilds.Single("pair pk (b,a)")),
            Row(
                    "plan-rebuild-alter-column-keeps-live-column-identity",
                    "再構築: AlterColumn は live の列 ID を保ち一意制約と主キー順序を壊さない",
                    DialectNames.Sqlite
                )
                .Live(s =>
                {
                    s.Add(ExpectationScenarios.PairTable(["a", "b"]));
                    s.Unique("pair", null, "a");
                })
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "pair",
                            ColumnName = "a",
                            // 図側の列（別 Guid）。型・NULL 許容・計算列が変わる
                            Column = new()
                            {
                                Name = "a",
                                DataType = "BIGINT",
                                IsPrimaryKey = true,
                                IsNullable = false,
                                IsComputed = true,
                            },
                            IsSelected = true,
                        },
                    ]
                )
                // 列 a は live の列（同じ Id）のまま型と計算列だけが変わり、一意制約の構成列がその列を指す
                .Expect(e =>
                    e.Rebuilds.Count("* cols *", 1)
                        .Rebuilds.Count("pair uq *", 1)
                        .Rebuilds.Single(
                            "pair cols a:BIGINT notnull pk computed live, *",
                            "pair pk (a,b)",
                            "pair uq - (a)"
                        )
                ),
            Row(
                    "plan-rebuild-column-changes-without-alter-pk-keep-live-pk",
                    "再構築: AlterPrimaryKey 未選択なら AlterColumn・AddColumn は主キーを変えない",
                    DialectNames.Sqlite
                )
                .Live(s => s.Tbl("items", ("id", "INT", true), ("code", "TEXT", false)))
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "items",
                            ColumnName = "code",
                            Column = S.Pk("code", "VARCHAR(20)"),
                            IsSelected = true,
                        },
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddColumn,
                            TableName = "items",
                            ColumnName = "k2",
                            Column = S.Pk("k2", "TEXT"),
                            IsSelected = true,
                        },
                    ]
                )
                .Expect(e =>
                    e.Rebuilds.Count("* cols *", 1)
                        .Rebuilds.Single("items pk (id)", "items cols *, k2:TEXT notnull*")
                ),
            Row(
                    "plan-rebuild-add-column-with-alter-pk-joins-pk",
                    "再構築: AlterPrimaryKey を選べば追加列も主キーになる",
                    DialectNames.Sqlite
                )
                .Live(s => s.Tbl("items", ("id", "INT", true), ("note", "TEXT", false)))
                .Items(_ =>
                {
                    var target = S.Detached(
                        "items",
                        S.Pk("id", "INT"),
                        S.Nullable("note", "TEXT"),
                        S.Pk("k2", "TEXT")
                    );

                    return
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AddColumn,
                            TableName = "items",
                            ColumnName = "k2",
                            Column = target.Columns[2],
                            IsSelected = true,
                        },
                        I.AlterPk("items", target),
                    ];
                })
                .Expect(e => e.Rebuilds.Count("* cols *", 1).Rebuilds.Single("items pk (id,k2)")),
            Row(
                    "plan-rebuild-referenced-pk-move-warns-candidate-key-loss",
                    "再構築: 参照される主キーの付け替えで候補キーを失う外部キーを警告する",
                    DialectNames.Sqlite
                )
                .Live(s => CandidateKeyParentChild(s, uniqueOnId: false))
                .Items(_ => [MovePrimaryKeyToCode()])
                .Expect(e =>
                    e.Warnings.Count("ForeignKeyRebuildMayLoseCandidateKey(*", 1)
                        .Warnings.Single(
                            "ForeignKeyRebuildMayLoseCandidateKey(child): FK_child_parent"
                        )
                ),
            Row(
                    "plan-rebuild-referenced-pk-move-no-warning-with-unique",
                    "再構築: 被参照列に一意制約がある・子表を削除するなら候補キー喪失を警告しない",
                    DialectNames.Sqlite
                )
                .Live(s => CandidateKeyParentChild(s, uniqueOnId: true))
                .Items(_ => [MovePrimaryKeyToCode()])
                .Expect(e => e.Warnings.Empty()),
            // 元のテストは親に一意制約を足した後で同じ親を使って 2 回目の計画を作る（子表の削除の形も一意制約を持つ）
            Row(
                    "plan-rebuild-referenced-pk-move-no-warning-when-child-dropped",
                    "再構築: 被参照列に一意制約がある・子表を削除するなら候補キー喪失を警告しない",
                    DialectNames.Sqlite
                )
                .Live(s => CandidateKeyParentChild(s, uniqueOnId: true))
                .Items(w =>
                    [
                        MovePrimaryKeyToCode(),
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.DropTable,
                            TableName = "child",
                            Entity = w.Live["child"],
                            IsSelected = true,
                        },
                    ]
                )
                .Expect(e => e.Warnings.Empty()),
        ];

    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> RebuildAttributeRows()
    {
        foreach (
            var (shape, description, withOthers, createSql, warning) in RebuildAttributeWarnings
        )
        {
            yield return Row($"plan-rebuild-attribute-{shape}", description, DialectNames.Sqlite)
                .Live(s =>
                {
                    s.Tbl("items", ("id", "INT", true), ("note", "TEXT", false));

                    if (withOthers)
                    {
                        s.Tbl("others", ("id", "INT", true));
                    }
                })
                // note 列の型の変更（＝再構築の引き金）
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "items",
                            ColumnName = "note",
                            Column = S.Nullable("note", "VARCHAR(200)"),
                            OldColumn = S.Nullable("note", "TEXT"),
                            IsSelected = true,
                        },
                    ]
                )
                .Context(w =>
                {
                    if (createSql.Length == 0)
                    {
                        return w.LiveContext();
                    }

                    var sql = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var (table, text) in createSql)
                    {
                        sql[table] = text;
                    }

                    return new SyncPlanContext
                    {
                        LiveEntities = w.Live.Entities,
                        LiveRelationships = w.Live.Relationships,
                        TableCreateSql = sql,
                    };
                })
                .Expect(e =>
                {
                    e.Rebuilds.Count("* cols *", 1);

                    if (warning is null)
                    {
                        e.Warnings.Empty();
                    }
                    else
                    {
                        e.Warnings.Count("TableRebuildDropsColumnAttribute(*", 1)
                            .Warnings.Single(warning);
                    }
                });
        }
    }

    /// <summary>主キー id・自然キー code（NOT NULL の TEXT）の親と、id を参照する子（名前なしの外部キー）を足す</summary>
    private static void CandidateKeyParentChild(ExpectationSchema s, bool uniqueOnId)
    {
        s.Table("parent", S.Pk("id", "INT"), S.NotNull("code", "TEXT"));
        s.Tbl("child", ("id", "INT", true), ("parent_id", "INT", false));
        s.Fk("parent", "child", null, ("id", "parent_id"));

        if (uniqueOnId)
        {
            s.Unique("parent", null, "id");
        }
    }

    /// <summary>親の主キーを id から code へ付け替える主キーの変更（目標の親）</summary>
    private static SchemaDiffItem MovePrimaryKeyToCode() =>
        I.AlterPk("parent", S.Detached("parent", S.Nullable("id", "INT"), S.Pk("code", "TEXT")));
}
