using AwesomeAssertions;
using QuickER.Model;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

internal static partial class UndoOperationRows
{
    /// <summary>
    /// 削除の位置：テーブル・リレーション・列・一意制約・名前付きクエリの削除を Undo すると、元の位置・元の並びへ戻る
    /// （RemovalOrderRoundTripTests・EntityQueryRemovalCommandTests・CommandTests の図）
    /// </summary>
    private static IEnumerable<UndoRow> RemovalPositionRows()
    {
        yield return Row(
                "removal-order-remove-unique-constraint-restores-position",
                "一意制約の削除を Undo すると、元の位置へ戻る"
            )
            .Diagram(RemovalOrderDiagram)
            .Prepare("先頭のテーブルを選ぶ", vm => vm.SelectedEntity = vm.Entities[0])
            .Operate(
                UndoEntry.Screen,
                "RemoveUniqueConstraintCommand（先頭の一意制約）",
                vm =>
                    vm.RemoveUniqueConstraintCommand.Execute(
                        vm.SelectedEntity!.UniqueConstraints[0]
                    )
            )
            .Commands(typeof(RemoveUniqueConstraintCommand));

        yield return Row(
                "removal-order-remove-column-restores-column-and-constraint-positions",
                "列の削除（その列を含む一意制約の連動削除を含む）を Undo すると、列も制約も元の位置へ戻る"
            )
            .Diagram(RemovalOrderDiagram)
            .Prepare("先頭のテーブルを選ぶ", vm => vm.SelectedEntity = vm.Entities[0])
            .Operate(
                UndoEntry.Screen,
                "RemoveColumnCommand（Code）",
                vm => vm.RemoveColumnCommand.Execute(SelectedColumnNamed(vm, "Code"))
            )
            .Commands(typeof(RemoveColumnCommand));

        yield return Row(
                "removal-order-remove-entity-restores-entity-and-relationship-positions",
                "テーブルの削除（接続するリレーションの連動削除を含む）を Undo すると、テーブルもリレーションも元の位置へ戻る"
            )
            .Diagram(RemovalOrderDiagram)
            .Prepare("先頭のテーブルを選ぶ", vm => vm.SelectedEntity = vm.Entities[0])
            .Operate(
                UndoEntry.Screen,
                "RemoveSelectedEntityCommand",
                vm => vm.RemoveSelectedEntityCommand.Execute(null)
            )
            .Commands(typeof(RemoveEntityCommand));

        yield return Row(
                "removal-order-group-remove-entities-restores-positions",
                "選択した複数のテーブルの削除を Undo すると、テーブルもリレーションも元の位置へ戻る"
            )
            .Diagram(RemovalOrderDiagram)
            .Prepare("全テーブルを選ぶ", vm => vm.SelectAllEntitiesCommand.Execute(null))
            .Operate(
                UndoEntry.Screen,
                "DeleteSelectedCommand",
                vm => vm.DeleteSelectedCommand.Execute(null)
            )
            .Commands(typeof(GroupRemoveEntitiesCommand));

        yield return Row(
                "removal-order-remove-first-entity-restores-order",
                "RemoveEntityCommand: 先頭のテーブルを削除して Undo すると、テーブルもリレーションも元の位置へ戻る"
            )
            .Diagram(FourTablesDiagram)
            .Prepare("先頭のテーブル A を選ぶ", vm => vm.SelectedEntity = TableNamed(vm, "A"))
            .Operate(
                UndoEntry.Screen,
                "RemoveSelectedEntityCommand（A）",
                vm => vm.RemoveSelectedEntityCommand.Execute(null)
            )
            .Commands(typeof(RemoveEntityCommand))
            .Check(
                UndoStage.Operated,
                "A と、A につながるリレーションが消える",
                vm =>
                {
                    TableOrder(vm).Should().Equal("B", "C", "D");
                    RelationshipOrder(vm).Should().Equal("C->D");
                }
            );

        yield return Row(
                "removal-order-group-remove-reverse-selection-restores-order",
                "GroupRemoveEntitiesCommand: 離れた位置のテーブルをまとめて削除して Undo すると、元の位置へ戻る"
            )
            .Diagram(FourTablesDiagram)
            .Operate(
                UndoEntry.Screen,
                // 選択の順 D、B は図の順と逆。画面の選択は図の順に並ぶので、この順はコマンドへ直接渡す
                "UndoRedo.Execute(GroupRemoveEntitiesCommand(D, B))（コマンドを直接）",
                vm =>
                    vm.UndoRedo.Execute(
                        new GroupRemoveEntitiesCommand(
                            vm,
                            [TableNamed(vm, "D"), TableNamed(vm, "B")]
                        )
                    )
            )
            .Commands(typeof(GroupRemoveEntitiesCommand))
            .Check(
                UndoStage.Operated,
                "D と B と、それにつながるリレーションが消え、A->C が残る",
                vm =>
                {
                    TableOrder(vm).Should().Equal("A", "C");
                    RelationshipOrder(vm).Should().Equal("A->C");
                }
            );

        yield return Row(
                "removal-order-remove-entity-restores-queries",
                "RemoveEntityCommand: そのエンティティのクエリを取り除き、Undo で元の位置へ戻す"
            )
            .Diagram(QueryOwnersDiagram)
            .Prepare(
                "Order を選び、クエリの一覧を覚える",
                vm =>
                {
                    vm.SelectedEntity = TableNamed(vm, "Order");
                    Remember(vm, "queries", vm.Queries.ToList());
                }
            )
            .Operate(
                UndoEntry.Screen,
                "RemoveSelectedEntityCommand（Order）",
                vm => vm.RemoveSelectedEntityCommand.Execute(null)
            )
            .Commands(typeof(RemoveEntityCommand))
            .Check(
                UndoStage.Operated,
                "Order のクエリだけが外れる",
                vm => QueryNames(vm).Should().Equal("FindByName")
            )
            .Check(
                UndoStage.Undone,
                "同じクエリのインスタンスが元の位置へ戻る",
                vm =>
                    vm.Queries.Should()
                        .Equal(
                            Recall<List<QueryDefinition>>(vm, "queries"),
                            (actual, expected) => ReferenceEquals(actual, expected)
                        )
            )
            .Check(
                UndoStage.Redone,
                "Redo でまた Order のクエリだけが外れる",
                vm => QueryNames(vm).Should().Equal("FindByName")
            );

        yield return Row(
                "removal-order-group-remove-restores-queries",
                "GroupRemoveEntitiesCommand: 対象すべてのクエリを取り除き、Undo で元の位置へ戻す"
            )
            .Diagram(QueryOwnersDiagram)
            .Prepare(
                "全テーブル（Order、Customer の順）を選び、クエリの一覧を覚える",
                vm =>
                {
                    vm.SelectAllEntitiesCommand.Execute(null);
                    Remember(vm, "queries", vm.Queries.ToList());
                }
            )
            .Operate(
                UndoEntry.Screen,
                "RemoveSelectedEntityCommand（Order、Customer）",
                vm => vm.RemoveSelectedEntityCommand.Execute(null)
            )
            .Commands(typeof(GroupRemoveEntitiesCommand))
            .Check(UndoStage.Operated, "クエリがすべて外れる", vm => vm.Queries.Should().BeEmpty())
            .Check(
                UndoStage.Undone,
                "同じクエリのインスタンスが元の位置へ戻る",
                vm =>
                    vm.Queries.Should()
                        .Equal(
                            Recall<List<QueryDefinition>>(vm, "queries"),
                            (actual, expected) => ReferenceEquals(actual, expected)
                        )
            );

        yield return Row(
                "removal-order-remove-entity-also-removes-relationships",
                "RemoveEntityCommand: 関連リレーションも削除・復元される"
            )
            .Diagram(LinkedPairDiagram)
            .Prepare(
                "先頭のテーブルとリレーションを覚えて、先頭のテーブルを選ぶ",
                vm =>
                {
                    Remember(vm, "removed", vm.Entities[0]);
                    Remember(vm, "relationship", vm.Relationships[0]);
                    vm.SelectedEntity = vm.Entities[0];
                }
            )
            .Operate(
                UndoEntry.Screen,
                "RemoveSelectedEntityCommand（先頭のテーブル）",
                vm => vm.RemoveSelectedEntityCommand.Execute(null)
            )
            .Commands(typeof(RemoveEntityCommand))
            .Check(
                UndoStage.Operated,
                "テーブルとリレーションが消える",
                vm =>
                {
                    vm.Entities.Should().NotContain(Recall<EntityViewModel>(vm, "removed"));
                    vm.Relationships.Should()
                        .NotContain(Recall<RelationshipViewModel>(vm, "relationship"));
                }
            )
            .Check(
                UndoStage.Undone,
                "同じテーブルとリレーションのインスタンスが戻る",
                vm =>
                {
                    vm.Entities.Should().Contain(Recall<EntityViewModel>(vm, "removed"));
                    vm.Relationships.Should()
                        .Contain(Recall<RelationshipViewModel>(vm, "relationship"));
                }
            );
    }
}
