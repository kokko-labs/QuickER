using AwesomeAssertions;
using QuickER.Model;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// リレーション作成モードの始点が図から外れたときに、作成モードが解けることを検証するテストクラス。
/// </summary>
/// <remarks>
/// 解けないと、図に無い（削除済み・置換前の）VM を始点にリレーションを作れてしまう
/// （線が追従せず、削除済みなら宙に浮いた参照を保存する）。
/// </remarks>
public sealed class MainViewModelRelationshipModeTests
{
    /// <summary>2 テーブルの図を持ち、1 対多の作成モードで 1 つ目を始点に選んだ VM を返す</summary>
    private static MainViewModel CreateWithPendingSource()
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

        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.PendingRelationshipSource.Should().BeSameAs(vm.Entities[0]);
        return vm;
    }

    /// <summary>始点のエンティティを削除すると作成モードが解けることを検証する</summary>
    [Fact(DisplayName = "始点を削除するとリレーション作成モードが解ける")]
    public void RemovingPendingSource_CancelsRelationshipMode()
    {
        var vm = CreateWithPendingSource();

        vm.SelectedEntity = vm.Entities[0];
        vm.RemoveSelectedEntityCommand.Execute(null);

        vm.IsRelationshipMode.Should().BeFalse();
        vm.PendingRelationshipSource.Should().BeNull();
    }

    /// <summary>始点のエンティティの追加を Undo すると作成モードが解けることを検証する</summary>
    [Fact(DisplayName = "始点の追加を取り消すとリレーション作成モードが解ける")]
    public void UndoingPendingSourceAddition_CancelsRelationshipMode()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);

        vm.UndoCommand.Execute(null);

        vm.Entities.Should().BeEmpty();
        vm.IsRelationshipMode.Should().BeFalse();
        vm.PendingRelationshipSource.Should().BeNull();
    }

    /// <summary>図を丸ごと置き換えると（開く・取込・外部変更の自動再読込の共通経路）作成モードが解けることを検証する</summary>
    [Fact(DisplayName = "図を置換するとリレーション作成モードが解ける")]
    public void ReplacingDiagram_CancelsRelationshipMode()
    {
        var vm = CreateWithPendingSource();

        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                TargetDbms = "sqlserver",
                Entities = { new Entity { TableName = "C" } },
            }
        );

        vm.IsRelationshipMode.Should().BeFalse();
        vm.PendingRelationshipSource.Should().BeNull();
    }

    /// <summary>始点でないエンティティを削除しても作成モードは解けないことを検証する</summary>
    [Fact(DisplayName = "始点でないテーブルの削除ではリレーション作成モードは解けない")]
    public void RemovingOtherEntity_KeepsRelationshipMode()
    {
        var vm = CreateWithPendingSource();
        var source = vm.Entities[0];

        vm.SelectedEntity = vm.Entities[1];
        vm.RemoveSelectedEntityCommand.Execute(null);

        vm.IsRelationshipMode.Should().BeTrue();
        vm.PendingRelationshipSource.Should().BeSameAs(source);
    }
}
