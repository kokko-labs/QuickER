using AwesomeAssertions;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>
/// 選択・検索結果・作成中のリレーションの始点が、図に居る要素だけを指すことの表明（選択の不変条件の正本）
/// </summary>
/// <remarks>
/// 不変条件: <c>SelectedEntity</c> は <c>Entities</c> の要素か null・<c>SelectedRelationship</c> は <c>Relationships</c> の要素か null・
/// <c>SelectedColumn</c> は <c>SelectedEntity</c> の列か null・<c>SearchResults</c> の各エンティティは <c>Entities</c> の要素・
/// 作成中の始点は <c>Entities</c> の要素か null。選択の不変条件の表（<see cref="SelectionInvariantTests"/>）と
/// 操作の往復の表（<see cref="UndoOperationTests"/>）の各段が使う。
/// </remarks>
internal static class SelectionInvariant
{
    /// <summary>不変条件を表明する。<paramref name="step"/> は失敗時にどの操作の直後かを示す</summary>
    public static void Assert(MainViewModel vm, string step)
    {
        if (vm.SelectedEntity is not null)
        {
            vm.Entities.Should()
                .Contain(vm.SelectedEntity, "{0}: 選択中のエンティティは図に居る", step);
        }

        if (vm.SelectedRelationship is not null)
        {
            vm.Relationships.Should()
                .Contain(vm.SelectedRelationship, "{0}: 選択中のリレーションは図に居る", step);
        }

        if (vm.SelectedColumn is not null)
        {
            (vm.SelectedEntity?.Columns)
                .Should()
                .NotBeNull(step + ": 列を選択しているならエンティティも選択している");
            vm.SelectedEntity!.Columns.Should()
                .Contain(vm.SelectedColumn, "{0}: 選択中の列は選択中のエンティティの列", step);
        }

        foreach (var result in vm.SearchResults)
        {
            vm.Entities.Should()
                .Contain(result.Entity, "{0}: 検索結果は図に居るエンティティだけ", step);
        }

        if (vm.PendingRelationshipSource is not null)
        {
            vm.Entities.Should()
                .Contain(vm.PendingRelationshipSource, "{0}: 作成中の始点は図に居る", step);
        }
    }
}
