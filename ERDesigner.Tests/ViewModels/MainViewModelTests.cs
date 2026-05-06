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

    [Fact(DisplayName = "リレーション作成時に既定の参照先列と外部キー列が設定される")]
    public void RelationshipMode_SetsDefaultColumns()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        vm.Entities[0].Columns[0].Name = "ParentKey";
        vm.Entities[1].Columns.Add(new ColumnViewModel(new Column { Name = "ParentId", DataType = "int" }));

        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);

        vm.Relationships[0].SourceColumnId.Should().Be(vm.Entities[0].Columns[0].Id);
        vm.Relationships[0].TargetColumnId.Should().Be(vm.Entities[1].Columns[1].Id);
    }

    [Fact(DisplayName = "参照先 PK と同名の列があれば既定 FK に選ばれ、FK チェックも入る")]
    public void RelationshipMode_PrefersSameNamedColumnAndChecksFk()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        vm.Entities[0].Columns[0].Name = "CustomerId";
        vm.Entities[1].Columns[0].Name = "Id";
        vm.Entities[1].Columns.Add(new ColumnViewModel(new Column { Name = "CustomerId", DataType = "int" }));

        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);

        vm.Relationships[0].TargetColumnId.Should().Be(vm.Entities[1].Columns[1].Id);
        vm.Entities[1].Columns[1].IsForeignKey.Should().BeTrue();
    }

    [Fact(DisplayName = "リレーション列は PK/FK チェックを編集できない")]
    public void RelationshipColumns_AreLocked()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        vm.Entities[0].Columns[0].Name = "ParentKey";
        vm.Entities[1].Columns.Add(new ColumnViewModel(new Column { Name = "ID", DataType = "int" }));

        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);

        var fkColumn = vm.Entities[1].Columns.First(c => c.Id == vm.Relationships[0].TargetColumnId);

        vm.Entities[0].Columns[0].IsPrimaryKeyEditable.Should().BeFalse();
        fkColumn.IsForeignKeyEditable.Should().BeFalse();
    }

    [Fact(DisplayName = "リレーション削除で自動設定された FK チェックが外れる")]
    public void RemoveRelationship_UnchecksManagedForeignKey()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        vm.Entities[0].Columns[0].Name = "ParentKey";
        vm.Entities[1].Columns.Add(new ColumnViewModel(new Column { Name = "ID", DataType = "int" }));

        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);
        var relationship = vm.Relationships[0];
        var fkColumn = vm.Entities[1].Columns.First(c => c.Id == relationship.TargetColumnId);

        fkColumn.IsForeignKey.Should().BeTrue();
        vm.OnRelationshipClicked(relationship);
        vm.DeleteSelectedCommand.Execute(null);

        vm.Relationships.Should().BeEmpty();
        fkColumn.IsForeignKey.Should().BeFalse();
        fkColumn.IsForeignKeyEditable.Should().BeTrue();
    }

    [Fact(DisplayName = "DeleteSelected はリレーション選択中ならリレーションを削除する")]
    public void DeleteSelected_RemovesSelectedRelationship()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);
        vm.OnRelationshipClicked(vm.Relationships[0]);

        vm.DeleteSelectedCommand.Execute(null);

        vm.Relationships.Should().BeEmpty();
    }

    [Fact(DisplayName = "エンティティ移動でリレーションの端点が追従する")]
    public void Relationship_FollowsEntityMove()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        var a = vm.Entities[0];
        var b = vm.Entities[1];

        // 幾何条件を安定させるため、同じ Y 軸上に十分離して配置する。
        a.X = 0;
        a.Y = 0;
        b.X = 400;
        b.Y = 0;

        vm.StartAddOneToOneCommand.Execute(null);
        vm.OnEntityClicked(a);
        vm.OnEntityClicked(b);

        var rel = vm.Relationships[0];
        var oldX1 = rel.X1;
        var oldX2 = rel.X2;

        a.X = a.X + 100;

        rel.X1.Should().Be(oldX1 + 100);
        rel.X2.Should().Be(oldX2);
    }

    [Fact(DisplayName = "エンティティ名変更は Undo/Redo できる")]
    public void EntityPropertyChange_CanUndoRedo()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        var entity = vm.Entities[0];

        entity.TableName = "Customer";

        vm.UndoCommand.Execute(null);
        entity.TableName.Should().Be("NewTable");

        vm.RedoCommand.Execute(null);
        entity.TableName.Should().Be("Customer");
    }

    [Fact(DisplayName = "リレーション種別変更は Undo/Redo できる")]
    public void RelationshipPropertyChange_CanUndoRedo()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        vm.StartAddOneToOneCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);
        var relationship = vm.Relationships[0];

        relationship.Type = RelationshipType.OneToMany;

        vm.UndoCommand.Execute(null);
        relationship.Type.Should().Be(RelationshipType.OneToOne);

        vm.RedoCommand.Execute(null);
        relationship.Type.Should().Be(RelationshipType.OneToMany);
    }

    [Fact(DisplayName = "新規で図をクリアした後は Undo/Redo 履歴もリセットされる")]
    public void NewDiagram_ClearsUndoRedoHistory()
    {
        var vm = new MainViewModel();
        vm.IsConfirmationEnabled = false;
        vm.AddEntityCommand.Execute(null);

        vm.NewDiagramCommand.Execute(null);

        vm.UndoRedo.CanUndo.Should().BeFalse();
        vm.UndoRedo.CanRedo.Should().BeFalse();
    }

    [Fact(DisplayName = "説明表示状態は自動保存から復元される")]
    public void Initialize_RestoresShowDescriptionsState()
    {
        var vm = new MainViewModel();
        vm.ShowColumnDescriptionsInDiagram = true;
        vm.AutoSave();

        var restored = new MainViewModel();
        restored.Initialize();

        restored.ShowColumnDescriptionsInDiagram.Should().BeTrue();
    }
}
