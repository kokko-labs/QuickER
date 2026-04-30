using ERDesigner.Models;
using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.ViewModels;

/// <summary>
/// <see cref="MainViewModel"/> のクリック・選択・リレーション作成ロジックのテスト。
/// （WPF UI スレッドに依存しないロジックのみ）
/// </summary>
public class MainViewModelTests
{
    [Fact(DisplayName = "AddEntityCommand 実行でエンティティが 1 件増える")]
    public void AddEntity_AddsOne()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);

        vm.Entities.Should().HaveCount(1);
        vm.UndoRedo.CanUndo.Should().BeTrue();
        vm.SelectedEntity.Should().Be(vm.Entities[0]);
    }

    [Fact(DisplayName = "Undo で追加が取り消され、Redo で復活する")]
    public void Add_Then_Undo_Then_Redo()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        var added = vm.Entities[0];

        vm.UndoCommand.Execute(null);
        vm.Entities.Should().BeEmpty();

        vm.RedoCommand.Execute(null);
        vm.Entities.Should().Contain(added);
    }

    [Fact(DisplayName = "OnEntityClicked: 単一選択になる")]
    public void OnEntityClicked_SetsSingleSelection()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        var first = vm.Entities[0];
        var second = vm.Entities[1];

        vm.OnEntityClicked(first);
        first.IsSelected.Should().BeTrue();
        second.IsSelected.Should().BeFalse();
        vm.SelectedEntity.Should().Be(first);
    }

    [Fact(DisplayName = "リレーション作成モードで2つのエンティティを順にクリックすると追加される")]
    public void RelationshipMode_CreatesRelationship()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        var a = vm.Entities[0];
        var b = vm.Entities[1];

        vm.StartAddOneToManyCommand.Execute(null);
        vm.IsRelationshipMode.Should().BeTrue();

        vm.OnEntityClicked(a);
        vm.OnEntityClicked(b);

        vm.IsRelationshipMode.Should().BeFalse();
        vm.Relationships.Should().HaveCount(1);
        vm.Relationships[0].Source.Should().Be(a);
        vm.Relationships[0].Target.Should().Be(b);
        vm.Relationships[0].Type.Should().Be(RelationshipType.OneToMany);
    }

    [Fact(DisplayName = "エンティティ移動でリレーションの端点が追従する")]
    public void Relationship_FollowsEntityMove()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        var a = vm.Entities[0];
        var b = vm.Entities[1];

        vm.StartAddOneToOneCommand.Execute(null);
        vm.OnEntityClicked(a);
        vm.OnEntityClicked(b);

        var rel = vm.Relationships[0];
        var oldX1 = rel.X1;

        a.X = a.X + 200;

        rel.X1.Should().Be(oldX1 + 200);
    }
}
