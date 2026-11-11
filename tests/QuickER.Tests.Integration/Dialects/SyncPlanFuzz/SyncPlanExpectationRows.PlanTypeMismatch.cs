using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;
using I = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationItems;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 計画の段の期待の行（外部キーの両端の型の食い違い：外し直さずに警告する・選んだ外部キーの作り直しと追加を外す・
/// 方言の型の正規化と文字列の長さだけの違い・暗黙コミットの方言で必ず断られる文を先頭へ）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    /// <summary>主キーの変更で付け直す外部キーの片側の長さだけを変える形（形, 方言, 両端の型, 子の新しい型, 外し直すか）</summary>
    private static (
        string Shape,
        string Dialect,
        string KeyType,
        string NewChildType,
        bool Rebuilds
    )[] StringLengthOnlyForeignKeyShapes =>
        [
            ("sqlserver", DialectNames.SqlServer, "varchar(10)", "varchar(20)", false),
            ("postgresql", DialectNames.PostgreSql, "varchar(10)", "varchar(20)", true),
            ("mysql", DialectNames.MySql, "varchar(10)", "varchar(20)", true),
            ("oracle-char", DialectNames.Oracle, "VARCHAR2(10 CHAR)", "VARCHAR2(20 CHAR)", true),
            ("oracle-byte", DialectNames.Oracle, "VARCHAR2(10 CHAR)", "VARCHAR2(20 BYTE)", false),
            ("sqlite", DialectNames.Sqlite, "VARCHAR(10)", "VARCHAR(20)", true),
        ];

    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanTypeMismatchRows() =>
        [
            .. new[] { ("child", "orders", "customer_id"), ("parent", "customer", "id") }.Select(
                side =>
                    (ScenarioRow<SyncPlanExpectationInput>)
                        Row(
                                $"plan-type-mismatch-one-side-warns-and-does-not-inject-{side.Item1}",
                                "型の食い違い: 片側だけの型変更では外部キーを外し直さず警告する",
                                DialectNames.MySql
                            )
                            .Live(s => ExpectationScenarios.CustomerOrders(s))
                            .Items(_ =>
                                [
                                    new SchemaDiffItem
                                    {
                                        Kind = SchemaDiffKind.AlterColumn,
                                        TableName = side.Item2,
                                        ColumnName = side.Item3,
                                        Column = S.Nullable(side.Item3, "BIGINT"),
                                        IsSelected = true,
                                    },
                                ]
                            )
                            .Expect(e =>
                                e.Sections.Exact("AlterColumn: *")
                                    .Warnings.Count("ForeignKeyColumnTypeMismatch*", 1)
                                    .Warnings.Single(
                                        "ForeignKeyColumnTypeMismatch(orders): FK_orders_customer"
                                    )
                            )
            ),
            Row(
                    "plan-type-mismatch-equivalent-notation-uses-dialect-normalization",
                    "型の食い違い: 綴りだけ違う同じ型は方言の正規化を通して一致とみなす",
                    DialectNames.SqlServer,
                    C.FkRebuild
                )
                .Live(s => ExpectationScenarios.CustomerOrders(s))
                // 両端の型は SQL Server の numeric(18,0) と numeric(18)（綴りだけ違う同じ型）
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "orders",
                            ColumnName = "customer_id",
                            Column = S.Nullable("customer_id", "numeric(18,0)"),
                            IsSelected = true,
                        },
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "customer",
                            ColumnName = "id",
                            Column = S.Nullable("id", "numeric(18)"),
                            IsSelected = true,
                        },
                    ]
                )
                // 正規化を通さない能力では食い違いとして警告する（対照）
                .Expect(e =>
                    e.For(
                            DialectNames.SqlServer,
                            x =>
                                x.Warnings.Empty()
                                    .Sections.Contains("DropForeignKey: *", "AddForeignKey: *")
                        )
                        .For(C.FkRebuild, x => x.Warnings.Count("ForeignKeyColumnTypeMismatch*", 1))
                ),
            Row(
                    "plan-type-mismatch-string-length-one-side-rebuilds-where-allowed",
                    "型の食い違い: 文字列型の長さの違いを許す方言では、片側の長さだけの変更でも外し直す",
                    DialectNames.MySql,
                    C.MySqlLike
                )
                .Live(s =>
                {
                    ExpectationScenarios.CustomerOrders(s);
                    s.Col("customer", "id").DataType = "varchar(50)";
                    s.Col("orders", "customer_id").DataType = "varchar(50)";
                })
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "orders",
                            ColumnName = "customer_id",
                            Column = S.Nullable("customer_id", "varchar(100)"),
                            IsSelected = true,
                        },
                    ]
                )
                // 長さの違いを許さない方言では従来どおり食い違いとして警告する（対照）
                .Expect(e =>
                    e.For(
                            DialectNames.MySql,
                            x =>
                                x.Warnings.Empty()
                                    .Sections.Exact(
                                        "DropForeignKey: *",
                                        "AlterColumn: *",
                                        "AddForeignKey: *"
                                    )
                        )
                        .For(C.MySqlLike, x => x.Warnings.Count("ForeignKeyColumnTypeMismatch*", 1))
                ),
            .. StringLengthOnlyForeignKeyShapes.Select(shape =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"plan-type-mismatch-pk-change-string-length-only-fk-{shape.Shape}",
                            "型の食い違い: 長さだけ違う外部キーの付け直しは SQL Server 以外の方言で許す（単位語の違いは食い違い）",
                            shape.Dialect
                        )
                        .Live(s =>
                        {
                            ExpectationScenarios.CustomerOrders(s);
                            s.Col("customer", "id").DataType = shape.KeyType;
                            s.Col("orders", "customer_id").DataType = shape.KeyType;
                            s["customer"].Columns.Add(S.NotNull("code", "INT"));
                            // 主キーを外しても外部キーの参照先が候補キーのまま残るよう、id に一意制約を置く
                            s.Unique("customer", "UQ_customer_id", "id");
                        })
                        .Items(w =>
                        {
                            var target = w.Live["customer"].Clone(preserveId: true);
                            target.Columns[0].IsPrimaryKey = false;
                            target.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;
                            return
                            [
                                I.AlterPk("customer", target),
                                new SchemaDiffItem
                                {
                                    Kind = SchemaDiffKind.AlterColumn,
                                    TableName = "orders",
                                    ColumnName = "customer_id",
                                    Column = S.Nullable("customer_id", shape.NewChildType),
                                    IsSelected = true,
                                },
                            ];
                        })
                        .Expect(e =>
                            StringLengthOnlyForeignKeyExpectation(e, shape.Dialect, shape.Rebuilds)
                        )
            ),
            .. new[] { false, true }.Select(childChangedToo =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"plan-type-mismatch-selected-fk-alter-{(childChangedToo ? "child-changed-too-is-kept" : "is-excluded-as-a-whole")}",
                            "型の食い違い: 選んだ外部キーの作り直しも、同期後に両端の型が食い違うなら削除ごと外して警告する",
                            AllDialects
                        )
                        // 別の表の外部キー FK_invoice_customer（子の列も同じ型へ変えるので同期後に型が揃う）。列の変更で
                        // 外部キーを外し直す方言（SQL Server・MySQL）ではふだん暗黙に外して付け直すが、食い違う外部キーがあれば
                        // 列の変更が必ず失敗するので外さない。食い違わせる行は整数と小数（整数の幅の違いは、宣言で許す方言では
                        // 食い違いでない）、子の列も変える行は整数の幅（列の変更で外し直さない方言は外部キーを張ったまま両端を
                        // 1 列ずつ変えるので、途中の両端も並べられる型にする）
                        .Live(CustomerOrdersAndInvoice)
                        .TargetWith(w =>
                        {
                            var newType = childChangedToo ? "BIGINT" : "DECIMAL(12,2)";
                            w.Target.Entities.AddRange(
                                w.Live.Entities.Select(e => e.Clone(preserveId: true))
                            );
                            w.Target["customer"].Columns[0].DataType = newType;
                            w.Target["invoice"].Columns[1].DataType = newType;

                            if (childChangedToo)
                            {
                                w.Target.Col("orders", "customer_id").DataType = newType;
                            }

                            var rel = w.Live.Relationships[0];
                            w.Target.Relationships.Add(
                                new Relationship
                                {
                                    SourceEntityId = rel.SourceEntityId,
                                    TargetEntityId = rel.TargetEntityId,
                                    ColumnPairs = [.. rel.ColumnPairs.Select(p => p.Clone())],
                                    ConstraintName = rel.ConstraintName,
                                    OnDelete = ForeignKeyReferentialAction.Cascade,
                                }
                            );
                            w.Target.Relationships.Add(w.Live.Relationships[1]);
                        })
                        .Select(SelectAll)
                        .Expect(e => SelectedForeignKeyAlterMismatchExpectation(e, childChangedToo))
            ),
            Row(
                    "plan-type-mismatch-selected-fk-add-is-excluded",
                    "型の食い違い: 選んだ外部キーの追加も、同期後に両端の型が食い違うなら外して警告する",
                    AllDialects
                )
                .Live(s =>
                {
                    s.Tbl("customer", ("id", "INT", true));
                    s.Tbl("orders", ("id", "INT", true), ("customer_id", "INT", false));
                })
                .TargetWith(w =>
                {
                    w.Target.Entities.AddRange(
                        w.Live.Entities.Select(e => e.Clone(preserveId: true))
                    );
                    w.Target["customer"].Columns[0].DataType = "DECIMAL(12,2)";
                    w.Target.Fk("customer", "orders", "FK_orders_customer", ("id", "customer_id"));
                })
                .Select(SelectAll)
                .Expect(e =>
                {
                    foreach (var dialect in AllDialects)
                    {
                        e.For(
                            dialect,
                            x =>
                            {
                                if (C.Get(dialect).ColumnReorder == ColumnReorderMode.Rebuild)
                                {
                                    // テーブル再構築の方言（SQLite）は追加をそのまま再構築へ畳む
                                    x.Warnings.Absent("ForeignKeyColumnTypeMismatch*")
                                        .Rebuilds.Contains("*");
                                    return;
                                }

                                x.Warnings.Count("ForeignKeyColumnTypeMismatch*", 1)
                                    .Warnings.Single(
                                        "ForeignKeyColumnTypeMismatch(orders): FK_orders_customer"
                                    )
                                    .Sections.Absent("AddForeignKey: *")
                                    .Sections.Contains("AlterColumn: *");
                            }
                        );
                    }
                }),
            Row(
                    "plan-type-mismatch-live-types-only-fk-alter-is-kept",
                    "型の食い違い: 構成列の型を変えない外部キーの作り直しは、live の型が食い違って見えても外さない",
                    DialectNames.PostgreSql
                )
                // PostgreSQL は int の列から bigint の列への外部キーを作れる（参照アクションだけを変える正当な作り直し）
                .Live(s =>
                {
                    ExpectationScenarios.CustomerOrders(s);
                    s.Col("customer", "id").DataType = "BIGINT";
                })
                .TargetWith(w =>
                {
                    w.Target.Entities.AddRange(
                        w.Live.Entities.Select(e => e.Clone(preserveId: true))
                    );
                    var rel = w.Live.Relationships[0];
                    w.Target.Relationships.Add(
                        new Relationship
                        {
                            SourceEntityId = rel.SourceEntityId,
                            TargetEntityId = rel.TargetEntityId,
                            ColumnPairs = [.. rel.ColumnPairs.Select(p => p.Clone())],
                            ConstraintName = rel.ConstraintName,
                            OnDelete = ForeignKeyReferentialAction.Cascade,
                        }
                    );
                })
                .Select(SelectAll)
                .Expect(e =>
                    e.Warnings.Empty().Sections.Exact("DropForeignKey: *", "AddForeignKey: *")
                ),
            Row(
                    "plan-type-mismatch-both-sides-same-type-rebuilds-without-warning",
                    "型の食い違い: 両端を同じ型へ変えれば外し直して警告しない",
                    DialectNames.MySql
                )
                .Live(s => ExpectationScenarios.CustomerOrders(s))
                // 大文字小文字の違いは同じ型
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "orders",
                            ColumnName = "customer_id",
                            Column = S.Nullable("customer_id", "bigint"),
                            IsSelected = true,
                        },
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "customer",
                            ColumnName = "id",
                            Column = S.Nullable("id", "BIGINT"),
                            IsSelected = true,
                        },
                    ]
                )
                .Expect(e =>
                    e.Sections.Exact("DropForeignKey: *", "AlterColumn: *", "AddForeignKey: *")
                        .Warnings.Empty()
                ),
            Row(
                    "plan-type-mismatch-both-sides-kind-change-rebuilds-and-warns-where-declared",
                    "型の食い違い: 両端を同じ別の種類へ変える外部キーは、どの逐次 DDL の方言でも外して付け直し、列が空でないと型の種類を変えられない方言だけ警告する",
                    AllDialects
                )
                // 外部キーを張ったまま片側だけ変えた途中の状態（int と varchar）は、どの方言でも並べられない
                .Live(s => ExpectationScenarios.CustomerOrders(s))
                .TargetWith(w =>
                {
                    w.Target.Entities.AddRange(
                        w.Live.Entities.Select(e => e.Clone(preserveId: true))
                    );
                    w.Target.Col("customer", "id").DataType = "VARCHAR(10)";
                    w.Target.Col("orders", "customer_id").DataType = "VARCHAR(10)";
                    w.Target.Relationships.Add(w.Live.Relationships[0]);
                })
                .Select(SelectAll)
                .Expect(e =>
                {
                    foreach (var dialect in AllDialects)
                    {
                        var capabilities = C.Get(dialect);
                        e.For(
                            dialect,
                            x =>
                            {
                                x.Warnings.Absent("ForeignKeyColumnTypeMismatch*");

                                if (capabilities.ColumnReorder == ColumnReorderMode.Rebuild)
                                {
                                    x.Warnings.Absent("ConstraintRebuildAroundEmptyColumnChange*")
                                        .Rebuilds.Contains("*");
                                    return;
                                }

                                // SQL Server は主キーの列の変更で主キーも外して付け直す（Contains で外部キーの文だけを見る）
                                x.Sections.Contains(
                                    "DropForeignKey: DropForeignKey orders fk=FK_orders_customer",
                                    "AddForeignKey: AddForeignKey orders.customer_id fk=FK_orders_customer"
                                );

                                if (capabilities.TypeKindChangeRequiresEmptyColumn)
                                {
                                    x.Warnings.Single(
                                        "ConstraintRebuildAroundEmptyColumnChange(orders): FK_orders_customer (*"
                                    );
                                }
                                else
                                {
                                    x.Warnings.Absent("ConstraintRebuildAroundEmptyColumnChange*");
                                }
                            }
                        );
                    }
                }),
            Row(
                    "plan-type-mismatch-same-column-mismatched-sibling-drops-neither",
                    "型の食い違い: 同じ列の別の外部キーが食い違うなら、食い違わない外部キーも外さない",
                    C.MySqlLike
                )
                .Live(s => ExpectationScenarios.TwoForeignKeysOnSameColumn(s, true))
                .Items(_ =>
                    [AlterToBigint("orders", "customer_id"), AlterToBigint("customer", "id")]
                )
                .Expect(e =>
                    e.Sections.Exact("AlterColumn: *")
                        .Warnings.Count("ForeignKeyColumnTypeMismatch*", 1)
                        .Warnings.Single("ForeignKeyColumnTypeMismatch(*): FK_b")
                ),
            Row(
                    "plan-type-mismatch-any-mismatch-rebuilds-no-fk",
                    "型の食い違い: 食い違う外部キーがあれば、別の表の外部キーも外し直さない",
                    C.MySqlLike
                )
                // 残した外部キー（FK_a）が参照する親の列 customer.id の変更も失敗するので、同じ親の列を参照する items の
                // 外部キー（FK_c）も外さない
                .Live(s =>
                {
                    ExpectationScenarios.TwoForeignKeysOnSameColumn(s, true);
                    s.Tbl("items", ("id", "INT", true), ("customer_ref", "INT", false));
                    s.Fk("customer", "items", "FK_c", ("id", "customer_ref"));
                })
                .Items(_ =>
                    [
                        AlterToBigint("orders", "customer_id"),
                        AlterToBigint("customer", "id"),
                        AlterToBigint("items", "customer_ref"),
                    ]
                )
                .Expect(e =>
                    e.Sections.Exact("AlterColumn: *")
                        .Warnings.Count("ForeignKeyColumnTypeMismatch*", 1)
                        .Warnings.Single("ForeignKeyColumnTypeMismatch(*): FK_b")
                ),
            // 複合外部キーの 1 組が DB の受け入れている型の違い（子 INT → 親 DECIMAL(12,2)）を持ち、別の組の両端を同じ型へ変える。
            // 型の決まらない組は数えない（DB が今受け入れている）ので、外し直して警告しない（P5-2 の試運転の 2 回目の V1。
            // 外部キー単位で数えると偽の型の食い違いになり、外し直さずに列の変更・主キーの解除で失敗した）。
            // 列の変更で外し直す方言の形（MySQL に近い宣言＝実 DB はこの外部キーを作れないので網だけ）
            Row(
                    "plan-type-mismatch-composite-undecided-pair-is-not-counted-alter-column",
                    "型の食い違い: 複合外部キーの型の決まらない組の違いは数えず、ほかの組の両端を同じ型へ変えれば外し直す",
                    C.MySqlLike
                )
                .Live(CompositeForeignKeyWithAcceptedMismatch)
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "parent",
                            ColumnName = "id",
                            Column = S.Pk("id", "BIGINT"),
                            IsSelected = true,
                        },
                        AlterToBigint("child", "parent_id"),
                    ]
                )
                .Expect(e =>
                    e.Warnings.Absent("ForeignKeyColumnTypeMismatch*")
                        .Sections.Exact(
                            "DropForeignKey: *",
                            "AlterColumn: #0 AlterColumn parent.id; #1 AlterColumn child.parent_id",
                            "AddForeignKey: *"
                        )
                ),
            // 同じ形で親の主キーを並べ替える（主キーの変更で付け直す経路・PostgreSQL と Oracle は子 INT → 親の小数の外部キーを作れる）
            Row(
                    "plan-type-mismatch-composite-undecided-pair-is-not-counted-pk-change",
                    "型の食い違い: 主キーの変更で付け直す複合外部キーも、型の決まらない組の違いを数えずに外し直す",
                    DialectNames.PostgreSql,
                    DialectNames.Oracle
                )
                .Live(CompositeForeignKeyWithAcceptedMismatch)
                .TargetWith(w =>
                {
                    w.Target.Entities.AddRange(
                        w.Live.Entities.Select(e => e.Clone(preserveId: true))
                    );
                    w.Target.Col("parent", "id").DataType = "BIGINT";
                    w.Target.Col("child", "parent_id").DataType = "BIGINT";
                    w.Target.PrimaryKeyOrder("parent", "code", "id");
                    w.Target.Relationships.AddRange(w.Live.Relationships);
                })
                .Select(SelectAll)
                .Expect(e =>
                    e.Warnings.Absent("ForeignKeyColumnTypeMismatch*")
                        .Sections.Contains("DropForeignKey: *", "AddForeignKey: *")
                ),
            .. new[] { false, true }.Select(sibling =>
                (ScenarioRow<SyncPlanExpectationInput>)
                    Row(
                            $"plan-type-mismatch-pk-change-rebuilt-fk-warns-without-rebuild-{(sibling ? "with-sibling" : "alone")}",
                            "型の食い違い: 主キーの変更で付け直す外部キーも、型が食い違うなら外さずに警告する（暗黙コミットの方言は主キーの解除を先頭へ）",
                            DialectNames.SqlServer,
                            DialectNames.PostgreSql,
                            DialectNames.MySql,
                            DialectNames.Oracle
                        )
                        .Live(s =>
                        {
                            if (sibling)
                            {
                                CustomerOrdersAndInvoice(s);
                            }
                            else
                            {
                                ExpectationScenarios.CustomerOrders(s);
                            }

                            s["customer"].Columns.Add(S.NotNull("code", "INT"));
                        })
                        // 主キーを code へ付け替え、id に一意制約を足して候補キーを保つ（兄弟の外部キーは付け直せる）。
                        // orders の子の列だけ型を変える
                        .TargetWith(w =>
                        {
                            w.Target.Entities.AddRange(
                                w.Live.Entities.Select(e => e.Clone(preserveId: true))
                            );
                            w.Target.Col("customer", "id").IsPrimaryKey = false;
                            w.Target.Col("customer", "code").IsPrimaryKey = true;
                            w.Target.Unique("customer", "UQ_customer_id", "id");
                            w.Target.Col("orders", "customer_id").DataType = "VARCHAR(10)";
                            w.Target.Relationships.AddRange(w.Live.Relationships);
                        })
                        .Select(SelectAll)
                        .Expect(e => MismatchedRebuiltForeignKeyExpectation(e, sibling))
            ),
        ];

    /// <summary>差分の項目をすべて選ぶ</summary>
    private static void SelectAll(IReadOnlyList<SchemaDiffItem> items)
    {
        foreach (var item in items)
        {
            item.IsSelected = true;
        }
    }

    /// <summary>列の型を BIGINT（NULL 許容）へ変える列の変更の項目</summary>
    private static SchemaDiffItem AlterToBigint(string table, string column) =>
        new()
        {
            Kind = SchemaDiffKind.AlterColumn,
            TableName = table,
            ColumnName = column,
            Column = S.Nullable(column, "BIGINT"),
            IsSelected = true,
        };

    /// <summary>
    /// customer(id) ← orders(customer_id) に、customer を参照する別の表 invoice(id, customer_id) と外部キー FK_invoice_customer を足す
    /// </summary>
    /// <summary>
    /// 複合外部キー parent(id, code) ← child(parent_id, order_no)。組 (code, order_no) は親 DECIMAL(12,2)・子 INT の型の違い
    /// （PostgreSQL・Oracle が受け入れる）
    /// </summary>
    private static void CompositeForeignKeyWithAcceptedMismatch(ExpectationSchema s)
    {
        s.Tbl("parent", ("id", "INT", true), ("code", "DECIMAL(12,2)", true));
        s.Tbl(
            "child",
            ("id", "INT", true),
            ("parent_id", "INT", false),
            ("order_no", "INT", false)
        );
        s.Fk("parent", "child", "FK_child_parent", ("id", "parent_id"), ("code", "order_no"));
    }

    private static void CustomerOrdersAndInvoice(ExpectationSchema s)
    {
        ExpectationScenarios.CustomerOrders(s);
        s.Tbl("invoice", ("id", "INT", true), ("customer_id", "INT", false));
        s.Fk("customer", "invoice", "FK_invoice_customer", ("id", "customer_id"));
    }

    /// <summary>長さだけ違う外部キーの付け直しの期待（方言の能力の宣言で分ける）</summary>
    private static void StringLengthOnlyForeignKeyExpectation(
        ExpectationBuilder e,
        string dialect,
        bool rebuilds
    )
    {
        if (rebuilds && C.Get(dialect).ColumnReorder == ColumnReorderMode.Rebuild)
        {
            // テーブル再構築の方言（SQLite）は、外部キーを子表の作り直しで外して付け直す
            e.Warnings.Empty().Rebuilds.Contains("*");
        }
        else if (rebuilds && C.Get(dialect).TransactionalDdl)
        {
            e.Warnings.Empty().Sections.Contains("DropForeignKey: *", "AddForeignKey: *");
        }
        else if (rebuilds)
        {
            // 暗黙コミットの方言は、主キーを live のキーで一意と証明できない列（code）へ移す付与が行で断られうるので、その前で外す
            // 外部キーと主キーを名指しする（長さだけの違いは食い違いとしない）
            e.Warnings.Exact(
                    "ConstraintRebuildAroundRowDependentChange(orders): FK_orders_customer (PRIMARY KEY customer (code))",
                    "ConstraintRebuildAroundRowDependentChange(customer): PRIMARY KEY (PRIMARY KEY customer (code))"
                )
                .Sections.Contains("DropForeignKey: *", "AddForeignKey: *");
        }
        else
        {
            e.Sections.Absent("DropForeignKey: *")
                .Warnings.Count("ForeignKeyColumnTypeMismatch*", 1);
        }
    }

    /// <summary>選んだ外部キーの作り直しが同期後に両端の型の食い違いになるときの期待（方言の能力の宣言で分ける）</summary>
    private static void SelectedForeignKeyAlterMismatchExpectation(
        ExpectationBuilder e,
        bool childChangedToo
    )
    {
        foreach (var dialect in AllDialects)
        {
            var capabilities = C.Get(dialect);
            e.For(
                dialect,
                x =>
                {
                    if (capabilities.ColumnReorder == ColumnReorderMode.Rebuild)
                    {
                        // テーブル再構築の方言（SQLite）は外部キーの型を確かめず、作り直しを再構築へ畳む
                        x.Warnings.Absent("ForeignKeyColumnTypeMismatch*").Rebuilds.Contains("*");
                        return;
                    }

                    if (childChangedToo)
                    {
                        // 子の列も同じ型へ変えれば食い違わないので、作り直しを残して警告しない
                        x.Warnings.Absent("ForeignKeyColumnTypeMismatch*")
                            .Sections.Count("AddForeignKey: *", 1)
                            .Items.Contains(
                                "AddForeignKey > * fk=FK_orders_customer sel=* del=Cascade *"
                            );
                        return;
                    }

                    x.Warnings.Count("ForeignKeyColumnTypeMismatch*", 1)
                        .Warnings.Single("ForeignKeyColumnTypeMismatch(orders): FK_orders_customer")
                        .Items.Contains("AlterColumn > AlterColumn customer.id *")
                        .Items.Absent("* fk=FK_orders_customer *");

                    if (capabilities.AlterColumnRequiresForeignKeyRebuild)
                    {
                        // 列の変更で外し直す方言は、食い違う外部キーがあればどの外部キーも外し直さない（doomed）
                        x.Sections.Absent("DropForeignKey: *", "AddForeignKey: *");
                    }
                    else
                    {
                        // 外し直さない方言は、両端を同じ型へ変える FK_invoice_customer を、片側だけ変えた途中の状態（整数と小数）が
                        // 宣言で並べられないので外して付け直す（宣言は保守側＝DB が受け入れる組でも付け直す）
                        x.Sections.Contains(
                            "DropForeignKey: DropForeignKey invoice fk=FK_invoice_customer",
                            "AddForeignKey: AddForeignKey invoice.customer_id fk=FK_invoice_customer"
                        );
                    }

                    // DDL が暗黙コミットされる方言（MySQL・Oracle）では、食い違う外部キーに阻まれて必ず断られる列の変更を
                    // 計画の先頭へ移し、何も変えずに失敗させる（外部キーを外す文より前）。トランザクションの方言は並びを変えない
                    if (!capabilities.TransactionalDdl)
                    {
                        x.Sections.Exact(
                            capabilities.AlterColumnRequiresForeignKeyRebuild
                                ? ["AlterColumn: AlterColumn customer.id", "*"]
                                :
                                [
                                    "AlterColumn: AlterColumn customer.id",
                                    "DropForeignKey: *",
                                    "AlterColumn: *",
                                    "AddForeignKey: *",
                                ]
                        );
                    }
                    else
                    {
                        x.Sections.Count("AlterColumn: *", 1);
                    }
                }
            );
        }
    }

    /// <summary>主キーの変更で付け直す外部キーの両端の型が食い違うときの期待（方言の能力の宣言で分ける）</summary>
    /// <param name="e">期待</param>
    /// <param name="sibling">同じ主キーを参照する兄弟の外部キー（型を変えない）を持つか</param>
    private static void MismatchedRebuiltForeignKeyExpectation(ExpectationBuilder e, bool sibling)
    {
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
            var capabilities = C.Get(dialect);
            e.For(
                dialect,
                x =>
                {
                    x.Warnings.Count("ForeignKeyColumnTypeMismatch*", 1)
                        .Warnings.Single("ForeignKeyColumnTypeMismatch(*): FK_orders_customer")
                        .Items.Absent("* > DropForeignKey orders *", "* > DropForeignKey orders.*");

                    // 兄弟の外部キーを外し直すのは、列の変更で外部キーを外し直さない方言（PostgreSQL・Oracle）だけ
                    var siblingRebuilt =
                        sibling && !capabilities.AlterColumnRequiresForeignKeyRebuild;

                    if (capabilities.TransactionalDdl)
                    {
                        // 巻き戻る方言は固定順のまま（兄弟の外部キーの解除が主キーの解除より前）
                        x.Sections.Exact([
                            .. siblingRebuilt ? new[] { "DropForeignKey: *" } : [],
                            "AlterPrimaryKey/Drop: *",
                            "AlterColumn: *",
                            "AlterPrimaryKey/Add: *",
                            "AddUniqueConstraint: *",
                            .. siblingRebuilt ? new[] { "AddForeignKey: *" } : [],
                        ]);
                        return;
                    }

                    // 暗黙コミットの方言：食い違う外部キーに阻まれる主キーの解除を、兄弟の外部キーを外すより前（計画の先頭）へ移す
                    x.Sections.Exact([
                        "AlterPrimaryKey/Drop: AlterPrimaryKey customer",
                        .. siblingRebuilt ? new[] { "DropForeignKey: *" } : [],
                        "AlterColumn: *",
                        "AlterPrimaryKey/Add: *",
                        "AddUniqueConstraint: *",
                        .. siblingRebuilt ? new[] { "AddForeignKey: *" } : [],
                    ]);
                }
            );
        }
    }
}
