using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;
using C = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.SyncPlanExpectationCapabilities;
using I = QuickER.Tests.Integration.Dialects.SyncPlanFuzz.ExpectationItems;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 計画の段の期待の行（外部キーの作り直し＝AlterForeignKey の展開・主キーの変更の暗黙の外し直しとの重なり・再構築の方言）
/// </summary>
internal static partial class SyncPlanExpectationRows
{
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> PlanForeignKeyRows() =>
        [
            Row(
                    "plan-fk-alter-selected-expands-to-drop-and-add",
                    "外部キー変更: 選択すると旧定義の DROP と新定義の ADD へ展開される",
                    DialectNames.SqlServer
                )
                .Live(s => ExpectationScenarios.CustomerOrders(s))
                .Items(w => [SelectedForeignKeyAlter(w)])
                // 外す側は旧い定義（NO ACTION）・付ける側は新しい定義（CASCADE）
                .Expect(e =>
                    e.Sections.Count("DropForeignKey: *", 1)
                        .Sections.Count("AddForeignKey: *", 1)
                        .Items.Count("DropForeignKey > *", 1)
                        .Items.Single(
                            "DropForeignKey > * fk=FK_orders_customer sel=* del=NoAction *"
                        )
                        .Items.Count("AddForeignKey > *", 1)
                        .Items.Single("AddForeignKey > * del=Cascade *")
                ),
            Row(
                    "plan-fk-alter-unselected-produces-empty-plan",
                    "外部キー変更: 未選択なら計画は空",
                    DialectNames.SqlServer
                )
                .Live(s => ExpectationScenarios.CustomerOrders(s))
                .Items(w =>
                {
                    var alter = SelectedForeignKeyAlter(w);
                    alter.IsSelected = false;
                    return [alter];
                })
                .Expect(e => e.Sections.Empty().Rebuilds.Empty().Reorders.Empty()),
            Row(
                    "plan-fk-alter-with-referenced-pk-change-does-not-duplicate",
                    "外部キー変更: 主キー変更の暗黙の外し直しと重複せず旧定義を付け直さない",
                    DialectNames.SqlServer
                )
                .Live(s => ExpectationScenarios.CustomerOrders(s))
                .Items(w =>
                    [
                        SelectedForeignKeyAlter(w),
                        I.AlterPk("customer", w.Live["customer"].Clone(preserveId: false)),
                    ]
                )
                .Expect(e =>
                    e.Sections.Count("DropForeignKey: *", 1)
                        .Items.Count("DropForeignKey > *", 1)
                        .Sections.Count("AddForeignKey: *", 1)
                        .Items.Count("AddForeignKey > *", 1)
                        .Items.Single("AddForeignKey > * del=Cascade *")
                ),
            Row(
                    "plan-fk-alter-rebuild-dialect-replaces-fk",
                    "外部キー変更: 再構築の方言では子表の外部キーが新しい定義の 1 本だけになる",
                    DialectNames.Sqlite
                )
                .Live(s => ExpectationScenarios.CustomerOrders(s))
                .Items(w => [SelectedForeignKeyAlter(w)])
                .Expect(e =>
                    e.Rebuilds.Count("* cols *", 1)
                        .Rebuilds.Count("* fk *", 1)
                        .Rebuilds.Single("* fk * del=Cascade *")
                ),
            Row(
                    "plan-fk-alter-one-of-duplicated-rebuild-dialect-keeps-the-other",
                    "外部キー変更: 再構築の方言で同じ列ペアの 2 本のうち 1 本を変えても、もう 1 本は残る",
                    DialectNames.Sqlite
                )
                .Live(s =>
                {
                    var (a, b) = ExpectationScenarios.TwoForeignKeysOnSameColumn(s, false);
                    a.ConstraintName = null;
                    b.ConstraintName = null;
                    b.OnDelete = ForeignKeyReferentialAction.Cascade;
                })
                // 1 本目は ON UPDATE だけを CASCADE へ変える・2 本目はそのまま（名前は持たない）
                .TargetWith(w =>
                {
                    w.Target.Entities.AddRange(
                        w.Live.Entities.Select(e => e.Clone(preserveId: true))
                    );

                    foreach (
                        var (live, onUpdate) in new[]
                        {
                            (w.Live.Relationships[0], ForeignKeyReferentialAction.Cascade),
                            (w.Live.Relationships[1], ForeignKeyReferentialAction.NoAction),
                        }
                    )
                    {
                        w.Target.Relationships.Add(
                            new Relationship
                            {
                                SourceEntityId = live.SourceEntityId,
                                TargetEntityId = live.TargetEntityId,
                                ColumnPairs = [.. live.ColumnPairs.Select(p => p.Clone())],
                                OnDelete = live.OnDelete,
                                OnUpdate = onUpdate,
                            }
                        );
                    }
                })
                .Select(items =>
                {
                    foreach (var item in items)
                    {
                        item.IsSelected = item.Kind == SchemaDiffKind.AlterForeignKey;
                    }
                })
                .Expect(e =>
                    e.Diff.Count("AlterForeignKey *", 1)
                        .Rebuilds.Count("* cols *", 1)
                        .Rebuilds.Count("* fk *", 2)
                        .Rebuilds.Single(
                            "* fk * del=NoAction upd=Cascade",
                            "* fk * del=Cascade upd=NoAction"
                        )
                ),
        ];

    /// <summary>
    /// DB 側の図の外部キー（1 本目）の ON DELETE だけを CASCADE へ変えた目標の図と差分を取り、外部キー変更の項目を選んだ状態で返す
    /// （差分は行の能力で取る）
    /// </summary>
    private static SchemaDiffItem SelectedForeignKeyAlter(ExpectationWorld w)
    {
        var targetEntities = w.Live.Entities.Select(e => e.Clone(preserveId: true)).ToList();
        var liveRel = w.Live.Relationships[0];
        var targetRel = new Relationship
        {
            SourceEntityId = liveRel.SourceEntityId,
            TargetEntityId = liveRel.TargetEntityId,
            ColumnPairs = [.. liveRel.ColumnPairs.Select(p => p.Clone())],
            ConstraintName = liveRel.ConstraintName,
            OnDelete = ForeignKeyReferentialAction.Cascade,
        };

        var alter = new SchemaDiffService()
            .Compute(
                w.Live.Entities,
                w.Live.Relationships,
                targetEntities,
                [targetRel],
                w.Capabilities
            )
            .Items.Single(i => i.Kind == SchemaDiffKind.AlterForeignKey);
        alter.IsSelected = true;
        return alter;
    }
}
