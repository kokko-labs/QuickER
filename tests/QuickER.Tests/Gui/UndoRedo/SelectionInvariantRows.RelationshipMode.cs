using AwesomeAssertions;
using QuickER.Model;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

internal static partial class SelectionInvariantRows
{
    /// <summary>作成中のリレーションの始点が図から外れたとき（削除・Undo・置換・パネルを畳む）に作成モードが解ける</summary>
    /// <remarks>
    /// 解けないと、図に無い（削除済み・置換前の）VM を始点にリレーションを作れてしまう。
    /// ツールボックスを畳む行：作成モードの表示と「キャンセル」ボタンはツールボックス内にしかなく、Esc でも解除されない。
    /// 畳んだまま残すと、画面のどこにも現れないモードだけが生き続ける。
    /// </remarks>
    private static IEnumerable<SelectionRow> RelationshipModeRows()
    {
        yield return PendingSourceRow(
                "relationship-mode-remove-source-cancels-mode",
                "始点を削除するとリレーション作成モードが解ける"
            )
            .Step(
                "始点のテーブルを選んで削除する",
                (MainViewModel vm) =>
                {
                    vm.SelectedEntity = vm.Entities[0];
                    vm.RemoveSelectedEntityCommand.Execute(null);
                }
            )
            .Expect(
                "作成モードが解け、始点は無い",
                (MainViewModel vm) =>
                {
                    vm.IsRelationshipMode.Should().BeFalse();
                    vm.PendingRelationshipSource.Should().BeNull();
                }
            )
            .Covers(SelectionPointer.PendingRelationshipSource, SelectionDetach.Delete);

        yield return Row(
                "relationship-mode-undo-source-addition-cancels-mode",
                "始点の追加を取り消すとリレーション作成モードが解ける"
            )
            .Diagram(() => new MainViewModel())
            .Step(
                "テーブルを追加し、1 対多の作成モードでそれを始点に選ぶ",
                (MainViewModel vm) =>
                {
                    vm.AddEntityCommand.Execute(null);
                    vm.StartAddOneToManyCommand.Execute(null);
                    vm.OnEntityClicked(vm.Entities[0]);
                }
            )
            .Step("Undo", (MainViewModel vm) => vm.UndoCommand.Execute(null))
            .Expect(
                "テーブルが消え、作成モードが解け、始点は無い",
                (MainViewModel vm) =>
                {
                    vm.Entities.Should().BeEmpty();
                    vm.IsRelationshipMode.Should().BeFalse();
                    vm.PendingRelationshipSource.Should().BeNull();
                }
            )
            .Covers(SelectionPointer.PendingRelationshipSource, SelectionDetach.Undo);

        yield return PendingSourceRow(
                "relationship-mode-replace-diagram-cancels-mode",
                "図を置換するとリレーション作成モードが解ける"
            )
            .Step(
                "図を丸ごと置き換える（開く・取込・外部変更の自動再読込の共通経路）",
                (MainViewModel vm) =>
                    vm.ReplaceDiagramFromModule(
                        new ErDiagram
                        {
                            TargetDbms = "sqlserver",
                            Entities = { new Entity { TableName = "C" } },
                        }
                    )
            )
            .Expect(
                "作成モードが解け、始点は無い",
                (MainViewModel vm) =>
                {
                    vm.IsRelationshipMode.Should().BeFalse();
                    vm.PendingRelationshipSource.Should().BeNull();
                }
            )
            .Covers(SelectionPointer.PendingRelationshipSource, SelectionDetach.Replace);

        yield return PendingSourceRow(
                "relationship-mode-remove-other-entity-keeps-mode",
                "始点でないテーブルの削除ではリレーション作成モードは解けない"
            )
            .Step(
                "始点でないテーブルを選んで削除する",
                (MainViewModel vm) =>
                {
                    vm.SelectedEntity = vm.Entities[1];
                    vm.RemoveSelectedEntityCommand.Execute(null);
                }
            )
            .Expect(
                "作成モードは解けず、始点はそのまま",
                context =>
                {
                    context.Vm.IsRelationshipMode.Should().BeTrue();
                    context
                        .Vm.PendingRelationshipSource.Should()
                        .BeSameAs(context.Recall<EntityViewModel>("source"));
                }
            )
            .Covers(SelectionPointer.PendingRelationshipSource, SelectionDetach.Delete);

        yield return Row(
                "relationship-mode-hide-toolbox-cancels-mode",
                "ツールボックスを畳むとリレーション作成モードは中断される"
            )
            .Diagram(() => new MainViewModel())
            .Step(
                "テーブルを 2 つ追加し、1 対多の作成モードで 1 つ目を始点に選ぶ",
                (MainViewModel vm) =>
                {
                    vm.AddEntityCommand.Execute(null);
                    vm.AddEntityCommand.Execute(null);
                    vm.StartAddOneToManyCommand.Execute(null);
                    vm.OnEntityClicked(vm.Entities[0]);
                }
            )
            .Expect(
                "作成モードで始点がある",
                (MainViewModel vm) =>
                {
                    vm.IsRelationshipMode.Should().BeTrue();
                    vm.PendingRelationshipSource.Should().NotBeNull();
                }
            )
            .Step("ツールボックスを畳む", (MainViewModel vm) => vm.IsToolboxVisible = false)
            .Expect(
                "作成モードが解け、始点は無く、リレーションは作られない",
                (MainViewModel vm) =>
                {
                    vm.IsRelationshipMode.Should().BeFalse();
                    vm.PendingRelationshipSource.Should().BeNull();
                    vm.Relationships.Should().BeEmpty();
                }
            )
            .Covers(SelectionPointer.PendingRelationshipSource, SelectionDetach.CollapsePanel);
    }

    /// <summary>A・B の 2 テーブルの図で、1 対多の作成モードにして A を始点に選んだところまでの行</summary>
    private static SelectionRow PendingSourceRow(string id, string description) =>
        Row(id, description)
            .Diagram(() =>
            {
                var vm = new MainViewModel();
                vm.ReplaceDiagramFromModule(
                    new ErDiagram
                    {
                        TargetDbms = "sqlserver",
                        Entities =
                        {
                            new Entity { TableName = "A" },
                            new Entity { TableName = "B" },
                        },
                    }
                );

                return vm;
            })
            .Step(
                "1 対多の作成モードで 1 つ目のテーブルを始点に選ぶ",
                context =>
                {
                    context.Vm.StartAddOneToManyCommand.Execute(null);
                    context.Vm.OnEntityClicked(context.Vm.Entities[0]);
                    context.Remember("source", context.Vm.Entities[0]);
                }
            )
            .Expect(
                "始点に選んだテーブルが始点になる",
                context =>
                    context.Vm.PendingRelationshipSource.Should().BeSameAs(context.Vm.Entities[0])
            );
}
