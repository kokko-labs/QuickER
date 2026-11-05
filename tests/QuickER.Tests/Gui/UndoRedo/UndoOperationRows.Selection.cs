using System.Windows;
using AwesomeAssertions;

namespace QuickER.Tests.Gui.UndoRedo;

internal static partial class UndoOperationRows
{
    /// <summary>
    /// 選択・減光：表示だけの状態の変化は履歴を積まず、未保存にもせず、図も変えない
    /// （MainViewModelHighlightTests・MainViewModelSelectionTests の図）
    /// </summary>
    private static IEnumerable<UndoRow> SelectionRows()
    {
        yield return Row(
                "selection-highlight-change-pushes-nothing",
                "履歴汚染なし: 選択・減光変化で CanUndo が偽のまま"
            )
            .Diagram(HighlightDiagram)
            .Operate(
                UndoEntry.Screen,
                "A を選び、リレーションの選択を外し、A の選択も外す",
                vm =>
                {
                    vm.SelectedEntity = vm.Entities[0];
                    vm.SelectedRelationship = null;
                    vm.SelectedEntity = null;
                }
            )
            .NoHistory()
            .Check(
                UndoStage.Operated,
                "減光は選択から導く表示状態で、選択を外せば戻る",
                vm => vm.Relationships[0].IsDimmed.Should().BeFalse()
            );

        yield return Row(
                "selection-gestures-push-nothing",
                "履歴汚染なし: 選択操作で CanUndo が偽のまま"
            )
            .Diagram(SelectionDiagram)
            .Operate(
                UndoEntry.Screen,
                "トグル選択 2 回・ラバーバンド・全選択・全解除",
                vm =>
                {
                    vm.ToggleEntitySelection(vm.Entities[0]);
                    vm.ToggleEntitySelection(vm.Entities[1]);
                    vm.ApplyRubberBandSelection(new Rect(0, 0, 50, 50), additive: false);
                    vm.SelectAllEntitiesCommand.Execute(null);
                    vm.ClearSelectionCommand.Execute(null);
                }
            )
            .NoHistory();
    }
}
