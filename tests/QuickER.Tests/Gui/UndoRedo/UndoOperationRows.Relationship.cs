using AwesomeAssertions;
using QuickER.Model;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

internal static partial class UndoOperationRows
{
    /// <summary>
    /// リレーションの削除・種別の変更・列ペアの編集と、列ペアを巻き込む列の削除：1 つの操作が履歴 1 件になり、外部キーの印が追従する
    /// （MainViewModelTests・RelationshipViewModelTests・MainViewModelRelationshipColumnPairTests の図）
    /// </summary>
    private static IEnumerable<UndoRow> RelationshipRows()
    {
        yield return Row(
                "relationship-remove-unchecks-managed-foreign-key",
                "リレーション削除で自動設定された FK チェックが外れる"
            )
            .Diagram(EmptyDiagram)
            .Prepare(
                "親の主キーを ParentKey、子に NewTableId を加えてリレーションを作り、そのリレーションをクリックする",
                vm =>
                {
                    vm.AddEntityCommand.Execute(null);
                    vm.AddEntityCommand.Execute(null);
                    vm.Entities[0].Columns[0].Name = "ParentKey";
                    vm.Entities[1]
                        .Columns.Add(
                            new ColumnViewModel(
                                new Column { Name = "NewTableId", DataType = "int" }
                            )
                        );
                    CreateOneToMany(vm);
                    var relationship = vm.Relationships[0];
                    var foreignKey = vm.Entities[1]
                        .Columns.First(column =>
                            column.Id == relationship.ColumnPairs[0].TargetColumnId
                        );
                    foreignKey.IsForeignKey.Should().BeTrue();
                    Remember(vm, "foreignKey", foreignKey);
                    vm.OnRelationshipClicked(relationship);
                }
            )
            .Operate(
                UndoEntry.Screen,
                "DeleteSelectedCommand（リレーション選択中）",
                vm => vm.DeleteSelectedCommand.Execute(null)
            )
            .Commands(typeof(RemoveRelationshipCommand))
            .Check(
                UndoStage.Operated,
                "リレーションが消え、子列の外部キーの印が外れて編集できる",
                vm =>
                {
                    vm.Relationships.Should().BeEmpty();
                    var foreignKey = Recall<ColumnViewModel>(vm, "foreignKey");
                    foreignKey.IsForeignKey.Should().BeFalse();
                    foreignKey.IsForeignKeyEditable.Should().BeTrue();
                }
            );

        yield return Row(
                "relationship-remove-selected-clears-selection",
                "MainViewModel.RemoveSelectedRelationship でリレーションが削除される"
            )
            .Diagram(EmptyDiagram)
            .Prepare(
                "テーブルを 2 つ足してリレーションを作り、クリックして選ぶ",
                vm =>
                {
                    vm.AddEntityCommand.Execute(null);
                    vm.AddEntityCommand.Execute(null);
                    CreateOneToMany(vm);
                    var relationship = vm.Relationships[0];
                    vm.OnRelationshipClicked(relationship);
                    vm.SelectedRelationship.Should().Be(relationship);
                    Remember(vm, "relationship", relationship);
                }
            )
            .Operate(
                UndoEntry.Screen,
                "RemoveSelectedRelationshipCommand",
                vm => vm.RemoveSelectedRelationshipCommand.Execute(null)
            )
            .Commands(typeof(RemoveRelationshipCommand))
            .Check(
                UndoStage.Operated,
                "リレーションが消え、選択も外れる",
                vm =>
                {
                    vm.Relationships.Should().BeEmpty();
                    vm.SelectedRelationship.Should().BeNull();
                }
            )
            .Check(
                UndoStage.Undone,
                "同じリレーションのインスタンスが戻る",
                vm =>
                    vm.Relationships.Should()
                        .Contain(Recall<RelationshipViewModel>(vm, "relationship"))
            );

        yield return Row(
                "relationship-target-column-change-moves-foreign-key-mark",
                "リレーションの FK 列変更でカラム定義側の FK 状態も追従する"
            )
            .Diagram(EmptyDiagram)
            .Prepare(
                "子に NewTableId と ParentCode を加えてリレーションを作る",
                vm =>
                {
                    vm.AddEntityCommand.Execute(null);
                    vm.AddEntityCommand.Execute(null);
                    vm.Entities[1]
                        .Columns.Add(
                            new ColumnViewModel(
                                new Column { Name = "NewTableId", DataType = "int" }
                            )
                        );
                    vm.Entities[1]
                        .Columns.Add(
                            new ColumnViewModel(
                                new Column { Name = "ParentCode", DataType = "int" }
                            )
                        );
                    CreateOneToMany(vm);
                    Remember(
                        vm,
                        "originalTarget",
                        vm.Entities[1]
                            .Columns.First(column =>
                                column.Id == vm.Relationships[0].ColumnPairs[0].TargetColumnId
                            )
                    );
                }
            )
            .Operate(
                UndoEntry.Screen,
                "列ペアの行の子列を ParentCode へ選び直す",
                vm =>
                    vm.Relationships[0].ColumnPairRows[0].SelectedTargetColumn = ChildColumn(
                        vm,
                        "ParentCode"
                    )
            )
            .Commands(typeof(ChangeRelationshipColumnPairsCommand))
            .Check(
                UndoStage.Operated,
                "旧子列は外部キーの印が外れて編集でき、新子列は印が付いて編集できない",
                vm =>
                {
                    var original = Recall<ColumnViewModel>(vm, "originalTarget");
                    var replacement = ChildColumn(vm, "ParentCode");
                    original.IsForeignKey.Should().BeFalse();
                    original.IsForeignKeyEditable.Should().BeTrue();
                    replacement.IsForeignKey.Should().BeTrue();
                    replacement.IsForeignKeyEditable.Should().BeFalse();
                }
            )
            .Check(
                UndoStage.Undone,
                "編集できるかどうかも元へ戻る",
                vm =>
                {
                    Recall<ColumnViewModel>(vm, "originalTarget")
                        .IsForeignKeyEditable.Should()
                        .BeFalse();
                    ChildColumn(vm, "ParentCode").IsForeignKeyEditable.Should().BeTrue();
                }
            );

        yield return Row(
                "relationship-type-change-one-to-one-to-one-to-many",
                "リレーション種別変更は Undo/Redo できる"
            )
            .Diagram(EmptyDiagram)
            .Prepare(
                "テーブルを 2 つ足して 1 対 1 のリレーションを作る",
                vm =>
                {
                    vm.AddEntityCommand.Execute(null);
                    vm.AddEntityCommand.Execute(null);
                    vm.StartAddOneToOneCommand.Execute(null);
                    vm.OnEntityClicked(vm.Entities[0]);
                    vm.OnEntityClicked(vm.Entities[1]);
                }
            )
            .Operate(
                UndoEntry.Tracker,
                "Type = OneToMany",
                vm => vm.Relationships[0].Type = RelationshipType.OneToMany
            )
            .Commands(typeof(SnapshotChangeCommand))
            .Check(
                UndoStage.Operated,
                "種別が 1 対多になる",
                vm => vm.Relationships[0].Type.Should().Be(RelationshipType.OneToMany)
            );

        yield return Row(
                "relationship-many-to-many-clears-single-pair-in-one-history",
                "多対多への変更で列選択クリアも 1 回の Undo/Redo で往復する"
            )
            .Diagram(EmptyDiagram)
            .Prepare(
                "子に NewTableId を加えて 1 対多のリレーションを作る（列ペア 1 組）",
                vm =>
                {
                    vm.AddEntityCommand.Execute(null);
                    vm.AddEntityCommand.Execute(null);
                    vm.Entities[1]
                        .Columns.Add(
                            new ColumnViewModel(
                                new Column { Name = "NewTableId", DataType = "int" }
                            )
                        );
                    CreateOneToMany(vm);
                    vm.Relationships[0].ColumnPairs.Should().ContainSingle();
                }
            )
            .Operate(
                UndoEntry.Tracker,
                "Type = ManyToMany",
                vm => vm.Relationships[0].Type = RelationshipType.ManyToMany
            )
            .Commands(typeof(SnapshotChangeCommand))
            .Check(
                UndoStage.Operated,
                "列ペアと列ペアの行が空になる",
                vm =>
                {
                    vm.Relationships[0].ColumnPairs.Should().BeEmpty();
                    vm.Relationships[0].ColumnPairRows.Should().BeEmpty();
                }
            )
            .Check(
                UndoStage.Undone,
                "列ペアの行も戻る",
                vm => vm.Relationships[0].ColumnPairRows.Should().ContainSingle()
            );

        yield return Row(
                "relationship-column-pair-replace-is-one-history",
                "列ペア行の確定・差し替え・削除はそれぞれ 1 回の Undo で戻る（差し替え）"
            )
            .Diagram(() => OrderLineShipmentDiagram("OrderId", "LineNo", "AltLineNo"))
            .Prepare(
                "複合主キーの親から子へリレーションを作り、履歴を空にする",
                vm =>
                {
                    CreateOneToMany(vm);
                    vm.UndoRedo.Clear();
                }
            )
            .Operate(
                UndoEntry.Screen,
                "2 行目の子列を AltLineNo へ選び直す",
                vm =>
                    vm.Relationships[0].ColumnPairRows[1].SelectedTargetColumn = ChildColumn(
                        vm,
                        "AltLineNo"
                    )
            )
            .Commands(typeof(ChangeRelationshipColumnPairsCommand))
            .Check(
                UndoStage.Operated,
                "2 行目の子列が差し替わる",
                vm =>
                    vm.Relationships[0]
                        .ColumnPairs[1]
                        .TargetColumnId.Should()
                        .Be(ChildColumn(vm, "AltLineNo").Id)
            );

        yield return Row(
                "relationship-column-pair-remove-row-is-one-history",
                "列ペア行の確定・差し替え・削除はそれぞれ 1 回の Undo で戻る（行の削除）"
            )
            .Diagram(() => OrderLineShipmentDiagram("OrderId", "LineNo", "AltLineNo"))
            .Prepare(
                "複合主キーの親から子へリレーションを作り、2 行目の子列を AltLineNo へ差し替えて Undo / Redo する",
                vm =>
                {
                    CreateOneToMany(vm);
                    vm.UndoRedo.Clear();
                    vm.Relationships[0].ColumnPairRows[1].SelectedTargetColumn = ChildColumn(
                        vm,
                        "AltLineNo"
                    );
                    vm.UndoRedo.Undo();
                    vm.UndoRedo.Redo();
                }
            )
            .Operate(
                UndoEntry.Screen,
                "RemoveRelationshipColumnPairCommand（2 行目）",
                vm =>
                    vm.RemoveRelationshipColumnPairCommand.Execute(
                        vm.Relationships[0].ColumnPairRows[1]
                    )
            )
            .Commands(typeof(ChangeRelationshipColumnPairsCommand))
            .Check(
                UndoStage.Operated,
                "単列の外部キーへ戻り、外した子列の外部キーの印が外れる",
                vm =>
                {
                    vm.Relationships[0].ColumnPairs.Should().ContainSingle();
                    ChildColumn(vm, "AltLineNo").IsForeignKey.Should().BeFalse();
                }
            );

        yield return Row(
                "relationship-many-to-many-clears-every-composite-pair",
                "多対多への変更は複合外部キーの全ペアを消し、1 回の Undo で全て戻る"
            )
            .Diagram(() => OrderLineShipmentDiagram("OrderId", "LineNo"))
            .Prepare(
                "複合主キーの親から子へリレーションを作り（列ペア 2 組）、履歴を空にする",
                vm =>
                {
                    CreateOneToMany(vm);
                    vm.Relationships[0].ColumnPairs.Should().HaveCount(2);
                    vm.UndoRedo.Clear();
                }
            )
            .Operate(
                UndoEntry.Tracker,
                "Type = ManyToMany",
                vm => vm.Relationships[0].Type = RelationshipType.ManyToMany
            )
            .Commands(typeof(SnapshotChangeCommand))
            .Check(
                UndoStage.Operated,
                "列ペアと列ペアの行が空になる",
                vm =>
                {
                    vm.Relationships[0].ColumnPairs.Should().BeEmpty();
                    vm.Relationships[0].ColumnPairRows.Should().BeEmpty();
                }
            )
            .Check(
                UndoStage.Undone,
                "列ペアの行も 2 行戻る",
                vm => vm.Relationships[0].ColumnPairRows.Should().HaveCount(2)
            );

        yield return Row(
                "relationship-remove-column-clears-every-composite-pair",
                "列削除は複合外部キーの全ペアを消し、1 回の Undo で全て戻る"
            )
            .Diagram(() => OrderLineShipmentDiagram("OrderId", "LineNo"))
            .Prepare(
                "複合主キーの親から子へリレーションを作り（列ペア 2 組）、子を選んで履歴を空にする",
                vm =>
                {
                    CreateOneToMany(vm);
                    vm.Relationships[0].ColumnPairs.Should().HaveCount(2);
                    vm.SelectedEntity = vm.Entities[1];
                    Remember(vm, "removed", ChildColumn(vm, "LineNo"));
                    vm.UndoRedo.Clear();
                }
            )
            .Operate(
                UndoEntry.Screen,
                "RemoveColumnCommand（子の LineNo）",
                vm => vm.RemoveColumnCommand.Execute(Recall<ColumnViewModel>(vm, "removed"))
            )
            .Commands(typeof(RemoveColumnCommand))
            .Check(
                UndoStage.Operated,
                "1 組だけ抜かずに全ペアを消し、線は残り、残った子列の外部キーの印も外れる",
                vm =>
                {
                    vm.Entities[1]
                        .Columns.Should()
                        .NotContain(Recall<ColumnViewModel>(vm, "removed"));
                    vm.Relationships[0].ColumnPairs.Should().BeEmpty();
                    vm.Relationships.Should().ContainSingle();
                    ChildColumn(vm, "OrderId").IsForeignKey.Should().BeFalse();
                }
            )
            .Check(
                UndoStage.Undone,
                "同じ列のインスタンスが戻る",
                vm =>
                    vm.Entities[1].Columns.Should().Contain(Recall<ColumnViewModel>(vm, "removed"))
            );
    }
}
