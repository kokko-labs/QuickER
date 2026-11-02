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
/// 計画の段の期待の行（並べ替え：参照する列の並びを問う方言で、外部キーの追加・付け直し・作り直しを同期後のキーの並びで出す）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanArrangeRows() =>
        [
            // 参照される複合主キーの並びを入れ替える変更と一緒に選べば新しい並び（旧主キーを参照する外部キーの付け直しも
            // 新しい並び）。問わない方言では図の並びのまま。どれも警告しない
            Row(
                    "plan-arrange-add-fk-follows-post-sync-pk-order-with-key-change",
                    "並べ替え: 外部キーの追加は、同期後の主キーの並びで出す（主キーの並びを変えれば新しい並び）",
                    DialectNames.MySql,
                    DialectNames.MySql,
                    C.NonTransactionalUnordered
                )
                .Live(PairReferenceWithNewColumns)
                .Items(w =>
                {
                    var reordered = ReorderedPair(w);
                    return [I.AlterPk("pair", reordered), AddForeignKeyToPair(w, reordered)];
                })
                .Expect(e =>
                {
                    e.Warnings.Empty();

                    foreach (var ordered in new[] { DialectNames.MySql, DialectNames.MySql })
                    {
                        e.For(
                            ordered,
                            x =>
                                AddedForeignKeyPairs(
                                    AddedForeignKeyPairs(x, "FK_ref", "b>rb,a>ra"),
                                    "FK_ref_new",
                                    "b>xb,a>xa"
                                )
                        );
                    }

                    e.For(
                        C.NonTransactionalUnordered,
                        x =>
                            AddedForeignKeyPairs(
                                AddedForeignKeyPairs(x, "FK_ref", "a>ra,b>rb"),
                                "FK_ref_new",
                                "b>xb,a>xa"
                            )
                    );
                }),
            // 主キーの変更を選ばなければ live の並び
            Row(
                    "plan-arrange-add-fk-follows-live-pk-order-without-key-change",
                    "並べ替え: 外部キーの追加は、同期後の主キーの並びで出す（主キーの並びを変えれば新しい並び）",
                    DialectNames.MySql,
                    DialectNames.MySql
                )
                .Live(PairReferenceWithNewColumns)
                .Items(w => [AddForeignKeyToPair(w, ReorderedPair(w))])
                .Expect(e => AddedForeignKeyPairs(e.Warnings.Empty(), "FK_ref_new", "a>xa,b>xb")),
            Row(
                    "plan-arrange-add-fk-to-live-unique-in-other-order",
                    "並べ替え: 並びを問う方言では、一意制約と違う並びで参照する外部キーの追加を一意制約の並びで出す",
                    DialectNames.SqlServer,
                    C.FkRebuildUnorderedKey
                )
                .Live(s =>
                {
                    ExpectationScenarios.CustomerOrdersWithCodeAndKind(s);
                    s.Unique("customer", "UQ_customer_code_kind", "code", "kind");
                })
                .Items(w => [AddForeignKeyKindThenCode(w)])
                // 問わない方言では図の並びのまま
                .Expect(e =>
                    e.Warnings.Empty()
                        .For(
                            DialectNames.SqlServer,
                            x =>
                                AddedForeignKeyPairs(
                                    x,
                                    "FK_new",
                                    "code>customer_code,kind>customer_kind"
                                )
                        )
                        .For(
                            C.FkRebuildUnorderedKey,
                            x =>
                                AddedForeignKeyPairs(
                                    x,
                                    "FK_new",
                                    "kind>customer_kind,code>customer_code"
                                )
                        )
                ),
            // 同じ計画で追加する一意制約の並びにもそろえる
            Row(
                    "plan-arrange-add-fk-to-new-unique-in-other-order",
                    "並べ替え: 並びを問う方言では、一意制約と違う並びで参照する外部キーの追加を一意制約の並びで出す",
                    DialectNames.SqlServer
                )
                .Live(ExpectationScenarios.CustomerOrdersWithCodeAndKind)
                .Items(w =>
                    [
                        AddForeignKeyKindThenCode(w),
                        I.Unique(
                            SchemaDiffKind.AddUniqueConstraint,
                            "customer",
                            null,
                            ["code", "kind"]
                        ),
                    ]
                )
                .Expect(e =>
                    AddedForeignKeyPairs(
                        e.Warnings.Empty(),
                        "FK_new",
                        "code>customer_code,kind>customer_kind"
                    )
                ),
            // DDL の ForeignKeyColumnOrder.ArrangeToReferencedKey と同じく主キーを先に試す（一意制約を先に試すと、同期と DDL で
            // 同じ外部キーの並びが食い違う）
            Row(
                    "plan-arrange-add-fk-pk-and-unique-on-same-columns-follows-pk",
                    "並べ替え: 主キーと同じ列の集合の一意制約があっても、外部キーの追加は主キーの並びで出す",
                    DialectNames.MySql
                )
                .Live(s =>
                {
                    PairReferenceWithNewColumns(s);
                    s.Unique("pair", "UQ_pair_b_a", "b", "a");
                })
                .Items(w => [AddForeignKeyToPair(w, w.Live["pair"])])
                .Expect(e => AddedForeignKeyPairs(e.Warnings.Empty(), "FK_ref_new", "a>xa,b>xb")),
            // MySQL で一意でない索引の列を参照する形。QuickER は一意でない索引を取り込まないので、並べ替える先のキーが無い
            Row(
                    "plan-arrange-add-fk-not-on-any-key-is-not-arranged-and-warns",
                    "並べ替え: 参照先がどのキーとも一致しない外部キーの追加は並べ替えず、従来どおり警告する",
                    DialectNames.MySql
                )
                .Live(ExpectationScenarios.CustomerOrdersWithCodeAndKind)
                .Items(w => [AddForeignKeyKindThenCode(w)])
                .Expect(e =>
                    AddedForeignKeyPairs(
                        e.Warnings.Exact("ForeignKeyNotOnCandidateKey(orders): FK_new"),
                        "FK_new",
                        "kind>customer_kind,code>customer_code"
                    )
                ),
            // 旧主キーに支えられた外部キーを外して付け直す項目を引き続き注入し（DependsOnOldPrimaryKey）、並びを問う方言
            // （SQL Server・MySQL）では付け直しを新しい主キーの並びで出す。並びを問わない方言は live の並びのまま付け直す
            Row(
                    "plan-arrange-reorder-referenced-composite-key-rebuilds-fk-in-new-order",
                    "並べ替え: 参照される複合主キーの並びだけを変えると、外部キーを外して新しい主キーの並びで付け直す",
                    DialectNames.MySql,
                    DialectNames.MySql,
                    C.NonTransactionalUnordered
                )
                .Live(s => ExpectationScenarios.PairReference(s))
                .Items(w => [I.AlterPk("pair", ReorderedPair(w))])
                .Expect(e =>
                {
                    e.Warnings.Empty()
                        .Sections.Exact(
                            "DropForeignKey: *",
                            "AlterPrimaryKey*: *",
                            "AlterPrimaryKey*: *",
                            "AddForeignKey: *"
                        )
                        .Items.Count("DropForeignKey > *", 1)
                        .Items.Single("DropForeignKey > * fk=FK_ref sel=*");

                    foreach (var ordered in new[] { DialectNames.MySql, DialectNames.MySql })
                    {
                        e.For(ordered, x => AddedForeignKeyPairs(x, "FK_ref", "b>rb,a>ra"));
                    }

                    e.For(
                        C.NonTransactionalUnordered,
                        x => AddedForeignKeyPairs(x, "FK_ref", "a>ra,b>rb")
                    );
                }),
            // 差分は列ペアを並びを見ずに比べるので、作り直しは並び以外の変更（ここでは参照アクション）で起きる
            Row(
                    "plan-arrange-fk-alter-in-other-order-is-arranged-to-key",
                    "並べ替え: 図の並びが参照先のキーと違う外部キーの作り直しは、キーの並びで付け直す",
                    DialectNames.MySql,
                    DialectNames.MySql,
                    C.NonTransactionalUnordered
                )
                .Live(s => ExpectationScenarios.PairReference(s))
                .TargetWith(w =>
                {
                    w.Target.Entities.AddRange(
                        w.Live.Entities.Select(e => e.Clone(preserveId: true))
                    );
                    var fk = w.Live.Relationships[0];
                    w.Target.Relationships.Add(
                        new Relationship
                        {
                            SourceEntityId = fk.SourceEntityId,
                            TargetEntityId = fk.TargetEntityId,
                            ColumnPairs = [.. fk.ColumnPairs.Select(p => p.Clone()).Reverse()],
                            ConstraintName = fk.ConstraintName,
                            OnDelete = ForeignKeyReferentialAction.Cascade,
                        }
                    );
                })
                .Select(SelectAll)
                // 問わない方言は図の並びのまま
                .Expect(e =>
                {
                    e.Diff.Count("AlterForeignKey *", 1).Warnings.Empty();

                    foreach (var ordered in new[] { DialectNames.MySql, DialectNames.MySql })
                    {
                        e.For(
                            ordered,
                            x =>
                                AddedForeignKeyPairs(
                                    x.Sections.Exact("DropForeignKey: *", "AddForeignKey: *"),
                                    "FK_ref",
                                    "a>ra,b>rb"
                                )
                        );
                    }

                    e.For(
                        C.NonTransactionalUnordered,
                        x => AddedForeignKeyPairs(x, "FK_ref", "b>rb,a>ra")
                    );
                }),
            // 索引の喪失による外し直し（CollectLostIndexes）は、一意制約の削除を選んだときだけ働く
            Row(
                    "plan-arrange-unique-in-other-order-is-no-diff-and-keeps-fk",
                    "並べ替え: 一意制約の列の並びだけの違いは作り直しにならず、MySQL でも外部キーを外し直さない",
                    DialectNames.MySql
                )
                .Live(s =>
                {
                    ExpectationScenarios.CustomerOrders(s);
                    s["orders"].Columns.Add(S.Nullable("note", "INT"));
                    s.Unique("orders", "UQ_orders_customer_note", "customer_id", "note");
                })
                // 目標の図は一意制約の列の並びだけを入れ替え、外部キーは DB 側のものをそのまま持つ
                .TargetWith(w =>
                {
                    w.Target.Entities.AddRange(
                        w.Live.Entities.Select(e => e.Clone(preserveId: true))
                    );
                    var unique = w.Target["orders"].UniqueConstraints[0];
                    unique.ColumnIds = [.. unique.ColumnIds.AsEnumerable().Reverse()];
                    w.Target.Relationships.Add(w.Live.Relationships[0]);
                })
                .Expect(e => e.Diff.Empty().Sections.Empty().Rebuilds.Empty().Reorders.Empty()),
        ];

    /// <summary>
    /// 期待：計画の外部キーの追加のセクションがちょうど 1 つで、その中の名前 <paramref name="name"/> の項目がちょうど 1 つ、
    /// その列ペア（親&gt;子の並び）が <paramref name="pairs"/>
    /// </summary>
    private static ExpectationBuilder AddedForeignKeyPairs(
        ExpectationBuilder e,
        string name,
        string pairs
    ) =>
        e
            .Sections.Count("AddForeignKey: *", 1)
            .Items.Count($"AddForeignKey > * fk={name} sel=*", 1)
            .Items.Single($"AddForeignKey > * fk={name} sel=* pairs=({pairs}) *");

    /// <summary>
    /// <see cref="ExpectationScenarios.PairReference"/> に、ref の NULL 許容の列 xb・xa（INT）を足す（外部キーの追加の子の列）
    /// </summary>
    private static void PairReferenceWithNewColumns(ExpectationSchema s)
    {
        ExpectationScenarios.PairReference(s);
        s["ref"].Columns.Add(S.Nullable("xb", "INT"));
        s["ref"].Columns.Add(S.Nullable("xa", "INT"));
    }

    /// <summary>DB 側の pair の複製（同じ Id）で、主キーの順序を (b, a) にしたもの</summary>
    private static Entity ReorderedPair(ExpectationWorld w)
    {
        var reordered = w.Live["pair"].Clone(preserveId: true);
        reordered.PrimaryKeyColumnIds = [reordered.Columns[1].Id, reordered.Columns[0].Id];
        return reordered;
    }

    /// <summary>pair を ref(xb, xa) が (b, a) の並びで参照する外部キー FK_ref_new の追加（親は <paramref name="parent"/>）</summary>
    private static SchemaDiffItem AddForeignKeyToPair(ExpectationWorld w, Entity parent) =>
        I.AddForeignKey(parent, w.Live["ref"], "FK_ref_new", new("b", "xb"), new("a", "xa"));

    /// <summary>customer を orders が (kind, code) の並びで参照する外部キー FK_new の追加</summary>
    private static SchemaDiffItem AddForeignKeyKindThenCode(ExpectationWorld w) =>
        I.AddForeignKey(
            w.Live["customer"],
            w.Live["orders"],
            "FK_new",
            new("kind", "customer_kind"),
            new("code", "customer_code")
        );
}
