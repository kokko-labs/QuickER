using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using S = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationSchema;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 差分と計画の期待の行（同じテーブルの組に名前の無いリレーションが 2 本ある図で、差分が DDL と同じ一意な外部キー名を使う：
/// 既定名 FK_{子}_{親} だけでは 2 本が同じ名前になるので、2 本目に子の列名を足す。外部キーを作らない名前の無い多対多は名前を取らない）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    /// <summary>名前の無い 1 本目に DDL が付ける名前</summary>
    private const string UnnamedBillName = "FK_orders_customers";

    /// <summary>名前の無い 2 本目に DDL が付ける名前</summary>
    private const string UnnamedShipName = "FK_orders_customers_ship_customer_id";

    /// <summary>外部キーの名前を保存する 4 方言</summary>
    private static string[] PersistedForeignKeyNameDialects =>
        [DialectNames.SqlServer, DialectNames.PostgreSql, DialectNames.MySql, DialectNames.Oracle];

    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> UnnamedForeignKeyRows() =>
        [
            Row(
                    "diff-unnamed-fk-same-pair-twice-matches-live-named-by-ddl",
                    "名前の無い同じ組の外部キー 2 本の図は、DDL が付けた名前の live と外部キーの差分を出さない（名前を保存する 4 方言）",
                    PersistedForeignKeyNameDialects
                )
                // DB 側は DDL が付けた名前をそのまま持ち帰った図（取込の結果に当たる）
                .Live(s => UnnamedPairSchema(s, UnnamedBillName, UnnamedShipName))
                .Target(s => UnnamedPairSchema(s, null, null))
                .Expect(e =>
                    e.Diff.Absent("AddForeignKey *", "DropForeignKey *", "AlterForeignKey *")
                ),
            Row(
                    "diff-unnamed-fk-second-referential-action-change-replaces-under-same-name",
                    "名前の無い 2 本目の参照アクションを変えると、同じ名前で外して付け直す外部キー変更になる（名前を保存する 4 方言）",
                    PersistedForeignKeyNameDialects
                )
                .Live(s => UnnamedPairSchema(s, UnnamedBillName, UnnamedShipName))
                .Target(s =>
                    UnnamedPairSchema(s, null, null).OnDelete = ForeignKeyReferentialAction.Cascade
                )
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected = true;
                    }
                })
                // 外すのも付けるのも 2 本目の名前（既定名で付け直すと 1 本目と同じ名前になる）
                .Expect(e =>
                    e.Diff.Exact("AlterForeignKey *")
                        .Items.Count("DropForeignKey > *", 1)
                        .Items.Single($"DropForeignKey > * fk={UnnamedShipName} sel=*")
                        .Items.Count("AddForeignKey > *", 1)
                        .Items.Single($"AddForeignKey > * fk={UnnamedShipName} sel=*")
                ),
            Row(
                    "diff-unnamed-fk-many-to-many-first-matches-live-with-default-name",
                    "名前の無い多対多が先に並ぶ図は、既定名で外部キーを作った live と外部キーの差分を出さない（名前を保存する 4 方言）",
                    PersistedForeignKeyNameDialects
                )
                .Live(s => UnnamedPairSchema(s, UnnamedBillName, UnnamedShipName))
                .Target(s =>
                {
                    UnnamedPairSchema(s, null, null);

                    // DDL はこの関係に外部キーを作らないので、既定名を取らせてはいけない
                    s.Relationships.Insert(
                        0,
                        new Relationship
                        {
                            SourceEntityId = s["customers"].Id,
                            TargetEntityId = s["orders"].Id,
                            Type = RelationshipType.ManyToMany,
                        }
                    );
                })
                .Expect(e =>
                    e.Diff.Absent("AddForeignKey *", "DropForeignKey *", "AlterForeignKey *")
                ),
        ];

    /// <summary>
    /// customers(id) と、customers を 2 回参照する orders(id, bill_customer_id, ship_customer_id) の図を足す（外部キーの名前は指定した
    /// もの）。2 本目の外部キー（ship_customer_id）を返す
    /// </summary>
    private static Relationship UnnamedPairSchema(S s, string? billName, string? shipName)
    {
        s.Table("customers", S.Pk("id", "int"));
        s.Table(
            "orders",
            S.Pk("id", "int"),
            S.NotNull("bill_customer_id", "int"),
            S.NotNull("ship_customer_id", "int")
        );
        s.Fk("customers", "orders", billName, ("id", "bill_customer_id"));
        return s.Fk("customers", "orders", shipName, ("id", "ship_customer_id"));
    }
}
