using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;
using I = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationItems;
using ProviderStrings = QuickER.Provider.Resources.Strings;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 計画の段の期待の行（一意制約：セクションの順序・候補キーの証明・構成列の変更に伴う暗黙の外し直し・外部キーの破壊の警告・再構築）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    /// <summary>
    /// 明示的に外す一意制約と暗黙の外し直しの照合（形, DB の一意制約の名前〔すべて code 列。空文字は名前なし〕,
    /// 明示的に外す名前, 同じ計画で足す図の一意制約の名前, code 列を消すか〔偽なら型を変える〕, 暗黙の削除, 暗黙の付け直し）
    /// </summary>
    private static (
        string Shape,
        string[] Live,
        string[] Drops,
        string[] Adds,
        bool DropColumn,
        string[] ImplicitDrops,
        string[] ImplicitReAdds
    )[] ExplicitUniqueDrops =>
        [
            ("single-dropped", ["UQ_customer_code"], ["UQ_customer_code"], [], false, [], []),
            ("twin-one-dropped", ["UQ1", "UQ2"], ["UQ1"], [], false, ["UQ2"], ["UQ2"]),
            ("twin-both-dropped", ["UQ1", "UQ2"], ["UQ1", "UQ2"], [], false, [], []),
            ("unnamed-dropped", [""], [""], [], false, [], []),
            ("unnamed-add-is-not-matched", ["UQ1"], [], [""], false, ["UQ1"], ["UQ1"]),
            (
                "synthesized-name-add-is-not-matched",
                ["UQ1"],
                [],
                ["UQ_customer_code"],
                false,
                ["UQ1"],
                ["UQ1"]
            ),
            ("twin-one-dropped-with-column-drop", ["UQ1", "UQ2"], ["UQ1"], [], true, ["UQ2"], []),
            (
                "twin-both-dropped-with-column-drop",
                ["UQ1", "UQ2"],
                ["UQ1", "UQ2"],
                [],
                true,
                [],
                []
            ),
        ];

    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanUniqueConstraintRows() =>
        [
            Row(
                    "plan-uq-sections-are-in-fixed-order",
                    "一意制約セクションは解除＝FK 解除直後・追加＝FK 追加直前に並ぶ",
                    C.Neutral
                )
                .Items(_ =>
                    [
                        I.Item(SchemaDiffKind.AddForeignKey),
                        I.Unique(SchemaDiffKind.AddUniqueConstraint, "T", null, ["code"]),
                        I.Item(SchemaDiffKind.AlterColumn),
                        I.Unique(SchemaDiffKind.DropUniqueConstraint, "T", "uq_old", ["memo"]),
                        I.Item(SchemaDiffKind.DropForeignKey),
                    ]
                )
                .NoContext()
                .Expect(e =>
                    e.Sections.Exact(
                        "DropForeignKey: *",
                        "DropUniqueConstraint: *",
                        "AlterColumn: *",
                        "AddUniqueConstraint: *",
                        "AddForeignKey: *"
                    )
                ),
            Row(
                    "plan-uq-candidate-key-kept-by-unique-no-warning",
                    "候補キー証明: 被参照列に UNIQUE が残るなら主キー付け替えでも警告しない",
                    DialectNames.SqlServer
                )
                // customer(id) は主キー兼被参照列。加えて id に一意制約が張られている
                .Live(s => CustomerOrdersWithUnique(s, "UQ_customer_id", "id"))
                // 主キーを code へ付け替える（被参照列 id は主キーから外れるが一意制約で候補キーのまま）
                .Items(_ => [MoveCustomerPrimaryKeyToCode()])
                // 依存する外部キーの自動の外し直しそのものは行う
                .Expect(e => e.Warnings.Empty().Sections.Contains("AddForeignKey: *")),
            Row(
                    "plan-uq-not-covering-referenced-column-warns",
                    "候補キー証明: UNIQUE が被参照列と一致しなければ警告する",
                    DialectNames.SqlServer
                )
                // 一意制約は (id, code) の複合＝id 単独の一意性は保証しない
                .Live(s => CustomerOrdersWithUnique(s, "UQ_customer_id_code", "id", "code"))
                .Items(_ => [MoveCustomerPrimaryKeyToCode()])
                .Expect(e => e.Warnings.Exact("ForeignKeyRebuildMayLoseCandidateKey(orders)*")),
            Row(
                    "plan-uq-dropped-in-same-sync-warns",
                    "候補キー証明: 根拠の UNIQUE を同時に削除するなら警告する",
                    DialectNames.SqlServer
                )
                .Live(s => CustomerOrdersWithUnique(s, "UQ_customer_id", "id"))
                .Items(_ =>
                    [
                        MoveCustomerPrimaryKeyToCode(),
                        I.Unique(
                            SchemaDiffKind.DropUniqueConstraint,
                            "customer",
                            "UQ_customer_id",
                            ["id"]
                        ),
                    ]
                )
                .Expect(e => e.Warnings.Contains("ForeignKeyRebuildMayLoseCandidateKey(*")),
            Row(
                    "plan-uq-unselected-add-is-not-relied-on",
                    "候補キー証明: 未選択の UNIQUE 追加はあてにしない",
                    DialectNames.SqlServer
                )
                .Live(s => CustomerOrdersWithUnique(s, null))
                .Items(_ =>
                    [
                        MoveCustomerPrimaryKeyToCode(),
                        I.Unique(
                            SchemaDiffKind.AddUniqueConstraint,
                            "customer",
                            null,
                            ["id"],
                            selected: false
                        ),
                    ]
                )
                .Expect(e => e.Warnings.Contains("ForeignKeyRebuildMayLoseCandidateKey(*")),
            Row(
                    "plan-uq-member-alter-injects-implicit-drop-and-readd",
                    "一意制約: 構成列の定義変更で自動 DROP → 再 ADD が注入される",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    s.Tbl("customer", ("id", "INT", true), ("code", "INT", false));
                    s.Unique("customer", "UQ_customer_code", "code");
                })
                .Items(_ =>
                    [
                        new SchemaDiffItem
                        {
                            Kind = SchemaDiffKind.AlterColumn,
                            TableName = "customer",
                            ColumnName = "code",
                            Column = S.Nullable("code", "BIGINT"),
                            IsSelected = true,
                        },
                    ]
                )
                .Expect(e =>
                    e.Sections.Exact(
                            "DropUniqueConstraint: *",
                            "AlterColumn: *",
                            "AddUniqueConstraint: *"
                        )
                        .Items.Count("DropUniqueConstraint > *", 1)
                        .Items.Count("AddUniqueConstraint > *", 1)
                        .Items.Single(
                            "DropUniqueConstraint > * uq=UQ_customer_code * uqcols=(code) desc=\""
                                + string.Format(
                                    ProviderStrings.Diff_AutoUniqueConstraintRebuild,
                                    "code"
                                )
                                + "\"",
                            "AddUniqueConstraint > * uqcols=(code) *"
                        )
                ),
            .. ExplicitUniqueDropRows(),
            Row(
                    "plan-uq-drop-on-referenced-column-warns-fk-break",
                    "一意制約: 被参照列の UNIQUE 削除は FK 破壊の警告を積む",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    ExpectationScenarios.CustomerOrders(s);
                    s.Unique("customer", "UQ_customer_id", "id");
                })
                .Items(_ =>
                    [
                        I.Unique(
                            SchemaDiffKind.DropUniqueConstraint,
                            "customer",
                            "UQ_customer_id",
                            ["id"]
                        ),
                    ]
                )
                // 警告は実行を止めない（削除そのものは計画に残る）
                .Expect(e =>
                    e.Warnings.Exact(
                            "UniqueConstraintDropMayBreakForeignKey(customer): FK_orders_customer"
                        )
                        .Sections.Exact("DropUniqueConstraint: *")
                ),
            Row(
                    "plan-uq-drop-on-unrelated-column-no-warning",
                    "一意制約: 被参照列と無関係な UNIQUE 削除は警告しない",
                    DialectNames.SqlServer
                )
                .Live(s => CustomerOrdersWithUnique(s, "UQ_customer_code", "code"))
                .Items(_ =>
                    [
                        I.Unique(
                            SchemaDiffKind.DropUniqueConstraint,
                            "customer",
                            "UQ_customer_code",
                            ["code"]
                        ),
                    ]
                )
                .Expect(e => e.Warnings.Empty()),
            Row(
                    "plan-uq-rebuild-changes-are-folded-into-synthesis",
                    "再構築方言: 一意制約の追加・削除は合成スキーマへ畳まれる",
                    DialectNames.Sqlite
                )
                .Live(s =>
                {
                    s.Tbl(
                        "product",
                        ("id", "INT", true),
                        ("sku", "TEXT", false),
                        ("legacy", "TEXT", false)
                    );
                    s.Unique("product", null, "legacy");
                })
                .Items(_ =>
                    [
                        I.Unique(SchemaDiffKind.DropUniqueConstraint, "product", null, ["legacy"]),
                        I.Unique(SchemaDiffKind.AddUniqueConstraint, "product", null, ["sku"]),
                    ]
                )
                // 一意制約の変更だけでも再構築が起き（セクションへは残らない）、legacy の制約が消えて sku の制約が加わる
                // （構成列は合成後の列で引ける）
                .Expect(e =>
                    e.Sections.Empty()
                        .Rebuilds.Count("* cols *", 1)
                        .Rebuilds.Count("product uq *", 1)
                        .Rebuilds.Single("product rebuild", "product uq * (sku)")
                ),
            // 同じ列集合の名前付きの一意制約が 2 つあれば、外すのは削除の項目と同じ名前の 1 つだけ（照合は名前と列集合）
            Row(
                    "plan-uq-rebuild-drops-only-same-named-twin",
                    "再構築方言: 一意制約の追加・削除は合成スキーマへ畳まれる",
                    DialectNames.Sqlite
                )
                .Live(s =>
                {
                    s.Tbl("product", ("id", "INT", true), ("sku", "TEXT", false));
                    s.Unique("product", "UQ1", "sku");
                    s.Unique("product", "UQ2", "sku");
                })
                .Items(_ =>
                    [I.Unique(SchemaDiffKind.DropUniqueConstraint, "product", "UQ1", ["sku"])]
                )
                .Expect(e =>
                    e.Rebuilds.Count("* cols *", 1)
                        .Rebuilds.Count("product uq *", 1)
                        .Rebuilds.Single("product uq UQ2 (sku)")
                ),
        ];

    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> ExplicitUniqueDropRows()
    {
        foreach (var shape in ExplicitUniqueDrops)
        {
            yield return Row(
                    $"plan-uq-explicit-drop-is-neither-duplicated-nor-readded-{shape.Shape}",
                    "一意制約: 明示 DROP したものは自動 DROP も再作成もしない（照合は名前と列集合）",
                    DialectNames.SqlServer
                )
                .Live(s =>
                {
                    s.Tbl("customer", ("id", "INT", true), ("code", "INT", false));

                    foreach (var name in shape.Live)
                    {
                        s.Unique("customer", name.Length == 0 ? null : name, "code");
                    }
                })
                .Items(_ =>
                    [
                        .. shape.Drops.Select(name =>
                            I.Unique(
                                SchemaDiffKind.DropUniqueConstraint,
                                "customer",
                                name.Length == 0 ? null : name,
                                ["code"]
                            )
                        ),
                        .. shape.Adds.Select(name =>
                            I.Unique(
                                SchemaDiffKind.AddUniqueConstraint,
                                "customer",
                                name.Length == 0 ? null : name,
                                ["code"]
                            )
                        ),
                        shape.DropColumn
                            ? new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.DropColumn,
                                TableName = "customer",
                                ColumnName = "code",
                                IsSelected = true,
                            }
                            : new SchemaDiffItem
                            {
                                Kind = SchemaDiffKind.AlterColumn,
                                TableName = "customer",
                                ColumnName = "code",
                                Column = S.Nullable("code", "BIGINT"),
                                IsSelected = true,
                            },
                    ]
                )
                // 計画が足した削除・付け直し（番号の付かない項目）は期待の名前だけ。明示的に外す項目はすべて削除のセクションに残る
                .Expect(e =>
                {
                    e.Items.Count(
                        "DropUniqueConstraint > DropUniqueConstraint *",
                        shape.ImplicitDrops.Length
                    );
                    e.Items.Count(
                        "AddUniqueConstraint > AddUniqueConstraint *",
                        shape.ImplicitReAdds.Length
                    );

                    foreach (var name in shape.ImplicitDrops)
                    {
                        e.Items.Single(
                            $"DropUniqueConstraint > DropUniqueConstraint customer uq={name} *"
                        );
                    }

                    foreach (var name in shape.ImplicitReAdds)
                    {
                        e.Items.Single(
                            $"AddUniqueConstraint > AddUniqueConstraint customer uq={name} *"
                        );
                    }

                    for (var i = 0; i < shape.Drops.Length; i++)
                    {
                        e.Items.Single($"DropUniqueConstraint > #{i} DropUniqueConstraint *");
                    }
                });
        }
    }

    /// <summary>
    /// customer(id) ← orders(customer_id) に、customer の NULL 許容の列 code と一意制約（<paramref name="name"/> が null なら
    /// 一意制約は足さない）を足す
    /// </summary>
    private static void CustomerOrdersWithUnique(
        ExpectationSchema s,
        string? name,
        params string[] columns
    )
    {
        ExpectationScenarios.CustomerOrders(s);
        s["customer"].Columns.Add(S.Nullable("code", "INT"));

        if (name is not null)
        {
            s.Unique("customer", name, columns);
        }
    }
}
