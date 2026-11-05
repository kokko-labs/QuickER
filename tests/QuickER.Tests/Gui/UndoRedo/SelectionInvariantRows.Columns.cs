using AwesomeAssertions;
using QuickER.Model;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

internal static partial class SelectionInvariantRows
{
    /// <summary>列の選択の外れ方（一覧の Reset・並べ替え・テーブルの削除・図の置換）と、図の置換・新規作成</summary>
    private static IEnumerable<SelectionRow> ColumnRows()
    {
        yield return Row(
                "selection-columns-reset-clears-column-selection-only-when-column-leaves-diagram",
                "列の一覧が Reset されても、選択中の列が図に居るテーブルのものなら選択は解けない"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step(
                "2 つ目のテーブルの 2 列目を選ぶ",
                context =>
                {
                    context.Vm.SelectedEntity = context.Vm.Entities[1];
                    var selected = context.Vm.SelectedEntity.Columns[1];
                    context.Remember("selected", selected);
                    context.Vm.SelectedColumn = selected;
                }
            )
            .Step(
                "別のテーブルの列の一覧を Reset する",
                (MainViewModel vm) => vm.Entities[0].Columns.Clear()
            )
            .Expect(
                "選択中の列は図に居るので選択は解けない",
                context =>
                    context
                        .Vm.SelectedColumn.Should()
                        .BeSameAs(context.Recall<ColumnViewModel>("selected"))
            )
            .Step(
                "選択中の列を持つテーブル自身の列の一覧を Reset する",
                (MainViewModel vm) => vm.Entities[1].Columns.Clear()
            )
            .Expect(
                "列は図から居なくなるので選択は解ける",
                (MainViewModel vm) => vm.SelectedColumn.Should().BeNull()
            )
            .Covers(SelectionPointer.SelectedColumn, SelectionDetach.Reset);

        yield return Row(
                "selection-move-column-keeps-column-selection",
                "列の並べ替えでは、選択中の列の選択が解けない"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step(
                "2 つ目のテーブルの 2 列目を選ぶ",
                context =>
                {
                    context.Vm.SelectedEntity = context.Vm.Entities[1];
                    var selected = context.Vm.SelectedEntity.Columns[1];
                    context.Remember("selected", selected);
                    context.Vm.SelectedColumn = selected;
                }
            )
            .Step(
                "列を先頭へ並べ替える",
                (MainViewModel vm) => vm.SelectedEntity!.Columns.Move(1, 0)
            )
            .Expect(
                "並べ替えは列が外れる操作ではない",
                context =>
                    context
                        .Vm.SelectedColumn.Should()
                        .BeSameAs(context.Recall<ColumnViewModel>("selected"))
            )
            .Covers(SelectionPointer.SelectedColumn, SelectionDetach.Reset);

        yield return Row(
                "selection-remove-entity-with-column-selected-clears-column-selection",
                "列を選択したままテーブルを削除・図を置換すると、列の選択も解ける"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step(
                "2 つ目のテーブルの 2 列目を選ぶ",
                (MainViewModel vm) =>
                {
                    vm.SelectedEntity = vm.Entities[1];
                    vm.SelectedColumn = vm.SelectedEntity.Columns[1];
                }
            )
            .Step(
                "選択中のテーブルを削除する",
                (MainViewModel vm) => vm.RemoveSelectedEntityCommand.Execute(null)
            )
            .Expect(
                "削除したテーブルの列を選択したままにしない",
                (MainViewModel vm) => vm.SelectedColumn.Should().BeNull()
            )
            .Step("Undo", (MainViewModel vm) => vm.UndoCommand.Execute(null))
            .Step(
                "2 つ目のテーブルの 2 列目を選び直す",
                (MainViewModel vm) =>
                {
                    vm.SelectedEntity = vm.Entities[1];
                    vm.SelectedColumn = vm.SelectedEntity.Columns[1];
                }
            )
            .Step(
                "空の図へ置換する",
                (MainViewModel vm) => vm.ReplaceDiagramFromModule(new ErDiagram())
            )
            .Expect(
                "図を置換したあとも、外れた列を選択したままにしない",
                (MainViewModel vm) => vm.SelectedColumn.Should().BeNull()
            )
            .Covers(
                SelectionPointer.SelectedColumn,
                SelectionDetach.Delete,
                SelectionDetach.Replace
            );

        yield return Row(
                "selection-replace-and-new-keep-invariants",
                "図の置換・新規作成のあとも、検索結果と選択は図に居る要素だけを指す"
            )
            .Diagram(SelectionInvariantTests.Build)
            .Step(
                "「Ord」で検索し、2 つ目のテーブルを選ぶ",
                (MainViewModel vm) =>
                {
                    vm.SearchQuery = "Ord";
                    vm.SelectedEntity = vm.Entities[1];
                }
            )
            .Step(
                "別の図（空）へ置換する",
                (MainViewModel vm) => vm.ReplaceDiagramFromModule(new ErDiagram())
            )
            .Step("置換後に再検索する", (MainViewModel vm) => vm.SearchQuery = "Ord")
            .Covers(SelectionPointer.SearchResults, SelectionDetach.Replace)
            .Covers(SelectionPointer.SelectedEntity, SelectionDetach.Replace);
    }
}
