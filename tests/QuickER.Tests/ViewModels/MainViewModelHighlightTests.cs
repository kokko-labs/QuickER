using FluentAssertions;
using QuickER.Model;
using QuickER.ViewModels;

namespace QuickER.Tests.ViewModels;

/// <summary>
/// <see cref="MainViewModel"/> の関連ハイライト（減光・強調）導出ロジックを検証するテストクラス
/// </summary>
/// <remarks>
/// 減光・強調は選択状態から再計算される純粋な表示状態であり、Undo 履歴・保存対象には含めない。
/// そのため選択・減光の変化で <see cref="QuickER.UndoRedo.UndoRedoManager.CanUndo"/> が真にならないことも併せて検証する。
/// </remarks>
public class MainViewModelHighlightTests
{
    /// <summary>PK 列を 1 つ持つエンティティ ViewModel を生成してキャンバスへ追加する</summary>
    private static EntityViewModel AddEntity(MainViewModel vm, string tableName)
    {
        var model = new Entity
        {
            TableName = tableName,
            Columns =
            {
                new Column
                {
                    Name = "ID",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };

        var entity = new EntityViewModel(model);
        vm.Entities.Add(entity);
        return entity;
    }

    /// <summary>2 つのエンティティを結ぶリレーション ViewModel を生成してキャンバスへ追加する</summary>
    private static RelationshipViewModel AddRelationship(
        MainViewModel vm,
        EntityViewModel source,
        EntityViewModel target
    )
    {
        var relationship = new RelationshipViewModel(
            new Relationship
            {
                SourceEntityId = source.Id,
                TargetEntityId = target.Id,
                Type = RelationshipType.OneToMany,
            },
            source,
            target
        );

        vm.Relationships.Add(relationship);
        return relationship;
    }

    /// <summary>エンティティ選択で接続要素は通常表示・無関係要素は減光されることを検証する</summary>
    [Fact(DisplayName = "エンティティ選択: 接続要素は非減光・接続線は強調・無関係は減光")]
    public void SelectEntity_DimsUnrelatedAndEmphasizesConnected()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A");
        var b = AddEntity(vm, "B");
        var c = AddEntity(vm, "C");
        var relAb = AddRelationship(vm, a, b);
        var relBc = AddRelationship(vm, b, c);

        vm.SelectedEntity = a;

        // 選択エンティティと接続相手は非減光、無関係エンティティは減光
        a.IsDimmed.Should().BeFalse();
        b.IsDimmed.Should().BeFalse();
        c.IsDimmed.Should().BeTrue();

        // 接続リレーションは強調・非減光、無関係リレーションは減光・非強調
        relAb.IsEmphasized.Should().BeTrue();
        relAb.IsDimmed.Should().BeFalse();
        relBc.IsEmphasized.Should().BeFalse();
        relBc.IsDimmed.Should().BeTrue();
    }

    /// <summary>選択解除で全要素の減光・強調が解除されることを検証する</summary>
    [Fact(DisplayName = "選択解除: 全要素の減光・強調が解除される")]
    public void ClearSelection_ResetsAllHighlights()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A");
        var b = AddEntity(vm, "B");
        var relAb = AddRelationship(vm, a, b);

        vm.SelectedEntity = a;
        vm.SelectedEntity = null;

        a.IsDimmed.Should().BeFalse();
        b.IsDimmed.Should().BeFalse();
        relAb.IsDimmed.Should().BeFalse();
        relAb.IsEmphasized.Should().BeFalse();
    }

    /// <summary>リレーション選択で両端エンティティは通常表示・他は減光されることを検証する</summary>
    [Fact(DisplayName = "リレーション選択: 両端は非減光・他は減光")]
    public void SelectRelationship_KeepsEndpointsAndDimsRest()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A");
        var b = AddEntity(vm, "B");
        var c = AddEntity(vm, "C");
        var relAb = AddRelationship(vm, a, b);
        var relBc = AddRelationship(vm, b, c);

        vm.SelectedRelationship = relAb;

        // 選択リレーションの両端は非減光、無関係エンティティは減光
        a.IsDimmed.Should().BeFalse();
        b.IsDimmed.Should().BeFalse();
        c.IsDimmed.Should().BeTrue();

        // 選択リレーション自体は減光されず（強調は選択の青に委ねる）、他リレーションは減光
        relAb.IsDimmed.Should().BeFalse();
        relAb.IsEmphasized.Should().BeFalse();
        relBc.IsDimmed.Should().BeTrue();
    }

    /// <summary>自己参照リレーションを持つエンティティの選択でループが強調されることを検証する</summary>
    [Fact(DisplayName = "自己参照エンティティ選択: 自己ループが強調される")]
    public void SelectEntityWithSelfLoop_EmphasizesLoop()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A");
        var self = AddRelationship(vm, a, a);

        vm.SelectedEntity = a;

        self.IsSelfRelationship.Should().BeTrue();
        self.IsEmphasized.Should().BeTrue();
        self.IsDimmed.Should().BeFalse();
        a.IsDimmed.Should().BeFalse();
    }

    /// <summary>リレーション削除後にコレクション変更トリガで減光状態が再計算されることを検証する</summary>
    [Fact(DisplayName = "リレーション削除: コレクション変更で減光が再計算される")]
    public void RemoveRelationship_RecalculatesHighlights()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A");
        var b = AddEntity(vm, "B");
        var c = AddEntity(vm, "C");
        var relAb = AddRelationship(vm, a, b);
        AddRelationship(vm, b, c);

        vm.SelectedEntity = a;

        // 選択時点では B は A に接続しているため非減光
        b.IsDimmed.Should().BeFalse();

        // A-B の接続を削除すると、B は A と無関係になり減光へ切り替わる
        vm.Relationships.Remove(relAb);

        b.IsDimmed.Should().BeTrue();
        c.IsDimmed.Should().BeTrue();
    }

    /// <summary>選択・減光の変化では Undo 履歴が汚染されない（CanUndo が偽のまま）ことを検証する</summary>
    [Fact(DisplayName = "履歴汚染なし: 選択・減光変化で CanUndo が偽のまま")]
    public void HighlightChanges_DoNotPolluteUndoHistory()
    {
        var vm = new MainViewModel();
        var a = AddEntity(vm, "A");
        var b = AddEntity(vm, "B");
        var relAb = AddRelationship(vm, a, b);

        vm.SelectedEntity = a;
        vm.SelectedRelationship = null;
        vm.SelectedEntity = null;

        // 減光・強調は導出状態であり Undo の対象外であること
        relAb.IsDimmed.Should().BeFalse();
        vm.UndoRedo.CanUndo.Should().BeFalse();
    }
}
