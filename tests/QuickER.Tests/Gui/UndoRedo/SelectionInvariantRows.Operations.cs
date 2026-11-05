using AwesomeAssertions;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

internal static partial class SelectionInvariantRows
{
    /// <summary>複製・貼り付け・列とリレーションの追加・削除系の往復（図から外れる・戻る操作）</summary>
    private static IEnumerable<SelectionRow> OperationRows()
    {
        yield return Row(
                "selection-paste-and-duplicate-undo-clears-selection",
                "貼り付け・複製を Undo すると、選択が外れたテーブルを指したままにならない"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step(
                "2 つ目のテーブルを選ぶ",
                (MainViewModel vm) => vm.SelectedEntity = vm.Entities[1]
            )
            .RoundTrip(
                "複製",
                (MainViewModel vm) => vm.DuplicateSelectedEntityCommand.Execute(null)
            )
            .Step(
                "2 つ目のテーブルを選んでコピーする",
                (MainViewModel vm) =>
                {
                    vm.SelectedEntity = vm.Entities[1];
                    vm.CopySelectedEntityCommand.Execute(null);
                }
            )
            .RoundTrip("貼り付け", (MainViewModel vm) => vm.PasteCopiedEntityCommand.Execute(null))
            .Step("Undo", (MainViewModel vm) => vm.UndoCommand.Execute(null))
            .Expect(
                "貼り付けを Undo したあとの選択は解除",
                (MainViewModel vm) => vm.SelectedEntity.Should().BeNull()
            )
            .Covers(SelectionPointer.SelectedEntity, SelectionDetach.Undo);

        yield return Row(
                "selection-add-column-undo-clears-column-selection",
                "列の追加を Undo すると、選択が外れた列を指したままにならない"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step(
                "1 つ目のテーブルを選ぶ",
                (MainViewModel vm) => vm.SelectedEntity = vm.Entities[0]
            )
            .Step("列を追加する", (MainViewModel vm) => vm.AddColumnCommand.Execute(null))
            .Expect(
                "追加した列は選択される",
                (MainViewModel vm) => vm.SelectedColumn.Should().NotBeNull()
            )
            .Step("Undo", (MainViewModel vm) => vm.UndoCommand.Execute(null))
            .Expect(
                "Undo で消えた列を選択したままにしない",
                (MainViewModel vm) => vm.SelectedColumn.Should().BeNull()
            )
            .Covers(SelectionPointer.SelectedColumn, SelectionDetach.Undo);

        yield return Row(
                "selection-add-relationship-undo-clears-relationship-selection",
                "リレーションの追加を Undo すると、選択が外れたリレーションを指したままにならない"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step(
                "1 対多の作成モードで 2 つ目から 1 つ目へ結ぶ",
                (MainViewModel vm) =>
                {
                    vm.StartAddOneToManyCommand.Execute(null);
                    vm.OnEntityClicked(vm.Entities[1]);
                    vm.OnEntityClicked(vm.Entities[0]);
                }
            )
            .Expect(
                "リレーションが 1 本増える",
                (MainViewModel vm) => vm.Relationships.Should().HaveCount(2)
            )
            .Step(
                "追加したリレーションを選択する",
                context =>
                {
                    var added = context.Vm.Relationships[^1];
                    context.Remember("added", added);
                    context.Vm.SelectedRelationship = added;
                }
            )
            .Step("Undo", (MainViewModel vm) => vm.UndoCommand.Execute(null))
            .Expect(
                "Undo で消えたリレーションを選択したままにしない",
                context =>
                {
                    context
                        .Vm.Relationships.Should()
                        .NotContain(context.Recall<RelationshipViewModel>("added"));
                    context.Vm.SelectedRelationship.Should().BeNull();
                }
            )
            .Step("Redo", (MainViewModel vm) => vm.RedoCommand.Execute(null))
            .Covers(SelectionPointer.SelectedRelationship, SelectionDetach.Undo);

        yield return Row(
                "selection-remove-operations-round-trip",
                "削除系の操作と Undo / Redo の往復でも、選択は図に居る要素だけを指す"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step(
                "2 つ目のテーブルを選ぶ",
                (MainViewModel vm) => vm.SelectedEntity = vm.Entities[1]
            )
            .RoundTrip(
                "テーブルの削除",
                (MainViewModel vm) => vm.RemoveSelectedEntityCommand.Execute(null)
            )
            .Step(
                "先頭のリレーションを選ぶ",
                (MainViewModel vm) => vm.SelectedRelationship = vm.Relationships[0]
            )
            .RoundTrip(
                "リレーションの削除",
                (MainViewModel vm) => vm.RemoveSelectedRelationshipCommand.Execute(null)
            )
            .Step(
                "2 つ目のテーブルの 2 列目を選ぶ",
                (MainViewModel vm) =>
                {
                    vm.SelectedEntity = vm.Entities[1];
                    vm.SelectedColumn = vm.SelectedEntity.Columns[1];
                }
            )
            .RoundTrip(
                "列の削除",
                (MainViewModel vm) => vm.RemoveSelectedColumnCommand.Execute(null)
            )
            .Covers(SelectionPointer.SelectedEntity, SelectionDetach.Delete, SelectionDetach.Undo)
            .Covers(
                SelectionPointer.SelectedRelationship,
                SelectionDetach.Delete,
                SelectionDetach.Undo
            )
            .Covers(SelectionPointer.SelectedColumn, SelectionDetach.Delete, SelectionDetach.Undo);

        yield return Row(
                "selection-pending-relationship-source-undo-of-add-cancels-mode",
                "作成中のリレーションの始点にしたテーブルの追加を Undo すると、作成モードが解ける"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step(
                "テーブルを追加し、1 対多の作成モードでそれを始点に選ぶ",
                (MainViewModel vm) =>
                {
                    vm.AddEntityCommand.Execute(null);
                    var added = vm.SelectedEntity!;
                    vm.StartAddOneToManyCommand.Execute(null);
                    vm.OnEntityClicked(added);
                }
            )
            .Step("Undo", (MainViewModel vm) => vm.UndoCommand.Execute(null))
            .Covers(SelectionPointer.PendingRelationshipSource, SelectionDetach.Undo);

        yield return Row(
                "selection-single-remove-undo-restores-primary-selection",
                "単独のテーブルの削除の Undo で、選択の表示と主選択がそろって戻る"
            )
            .Diagram(EntityLifecycleUndoTests.Build)
            .Step(
                "先頭のテーブルをクリックして選ぶ",
                context =>
                {
                    var first = context.Vm.Entities[0];
                    context.Remember("first", first);
                    context.Vm.OnEntityClicked(first);
                }
            )
            .Step(
                "選択中のテーブルを削除する",
                (MainViewModel vm) => vm.RemoveSelectedEntityCommand.Execute(null)
            )
            .Step("Undo", (MainViewModel vm) => vm.UndoCommand.Execute(null))
            .Expect(
                "単独の削除の Undo で主選択を戻す",
                context =>
                {
                    var first = context.Recall<EntityViewModel>("first");
                    context.Vm.Entities.Should().Contain(first);
                    first.IsSelected.Should().BeTrue();
                    context.Vm.SelectedEntity.Should().BeSameAs(first);
                    context.Vm.RemoveSelectedEntityCommand.CanExecute(null).Should().BeTrue();
                }
            )
            .Covers(SelectionPointer.SelectedEntity, SelectionDetach.Delete, SelectionDetach.Undo);

        yield return Row(
                "selection-group-remove-undo-restores-primary-selection",
                "複数のテーブルの削除の Undo で、選択の表示と主選択がそろって戻る"
            )
            .Diagram(EntityLifecycleUndoTests.Build)
            .Step(
                "全テーブルを選択する",
                (MainViewModel vm) => vm.SelectAllEntitiesCommand.Execute(null)
            )
            .Step(
                "選択中のテーブルを削除する",
                (MainViewModel vm) => vm.RemoveSelectedEntityCommand.Execute(null)
            )
            .Step("Undo", (MainViewModel vm) => vm.UndoCommand.Execute(null))
            .Expect(
                "全テーブルが選択の表示つきで戻り、主選択は図に居る",
                (MainViewModel vm) =>
                {
                    vm.Entities.Should().HaveCount(2).And.OnlyContain(entity => entity.IsSelected);
                    vm.SelectedEntity.Should().NotBeNull();
                    vm.Entities.Should().Contain(vm.SelectedEntity!);
                }
            )
            .Covers(SelectionPointer.SelectedEntity, SelectionDetach.Delete, SelectionDetach.Undo);

        yield return Row(
                "selection-add-and-duplicate-undo-clears-primary-selection",
                "テーブルの追加・複製の Undo で、そのテーブルを主選択に残さない"
            )
            .Diagram(EntityLifecycleUndoTests.Build)
            .Step("テーブルを追加する", (MainViewModel vm) => vm.AddEntityCommand.Execute(null))
            .Step("Undo", (MainViewModel vm) => vm.UndoCommand.Execute(null))
            .Expect(
                "追加の Undo で主選択は解除",
                (MainViewModel vm) => vm.SelectedEntity.Should().BeNull()
            )
            .Step(
                "先頭のテーブルをクリックして複製する",
                (MainViewModel vm) =>
                {
                    vm.OnEntityClicked(vm.Entities[0]);
                    vm.DuplicateSelectedEntityCommand.Execute(null);
                }
            )
            .Step("Undo", (MainViewModel vm) => vm.UndoCommand.Execute(null))
            .Expect(
                "複製の Undo で主選択は解除",
                (MainViewModel vm) => vm.SelectedEntity.Should().BeNull()
            )
            .Covers(SelectionPointer.SelectedEntity, SelectionDetach.Undo);
    }
}
