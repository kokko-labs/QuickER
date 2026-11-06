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
            DialectNames.SqlServer => 13,
            DialectNames.PostgreSql => 15,
            DialectNames.MySql => 13,
            DialectNames.Oracle => 14,
            DialectNames.Sqlite => 13,
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
                            e.Warnings.Empty();
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
