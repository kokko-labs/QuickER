using ERDesigner.Models;
using ERDesigner.Tests.TestDoubles;
using ERDesigner.UndoRedo;
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
        vm.Relationships[0].ConstraintName.Should().Be("FK_NewTable_NewTable");
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

    [Fact(DisplayName = "リレーション作成モードで同じエンティティを2回クリックすると自己参照リレーションが追加される")]
    public void RelationshipMode_CreatesSelfRelationship()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.Entities[0].Columns.Add(new ColumnViewModel(new Column { Name = "ParentId", DataType = "int" }));

        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[0]);

        vm.Relationships.Should().ContainSingle();
        vm.Relationships[0].Source.Should().Be(vm.Entities[0]);
        vm.Relationships[0].Target.Should().Be(vm.Entities[0]);
        vm.Relationships[0].TargetColumnId.Should().Be(vm.Entities[0].Columns[1].Id);
        vm.Relationships[0].ConstraintName.Should().Be("FK_NewTable_NewTable");
    }

    [Fact(DisplayName = "同じ始点と終点のリレーションは種別が違っても重複追加されない")]
    public void RelationshipMode_DoesNotCreateDuplicateRelationship()
    {
        var vm = new MainViewModel(new StubDialogService());
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);

        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);

        vm.StartAddOneToOneCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);

        vm.Relationships.Should().ContainSingle();
        vm.IsRelationshipMode.Should().BeFalse();
        vm.PendingRelationshipSource.Should().BeNull();
    }

    [Fact(DisplayName = "自己参照リレーションも重複追加されない")]
    public void RelationshipMode_DoesNotCreateDuplicateSelfRelationship()
    {
        var vm = new MainViewModel(new StubDialogService());
        vm.AddEntityCommand.Execute(null);
        vm.Entities[0].Columns.Add(new ColumnViewModel(new Column { Name = "ParentId", DataType = "int" }));

        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[0]);

        vm.StartAddOneToOneCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[0]);

        vm.Relationships.Should().ContainSingle();
        vm.Relationships[0].Source.Should().Be(vm.Entities[0]);
        vm.Relationships[0].Target.Should().Be(vm.Entities[0]);
    }

    [Fact(DisplayName = "リレーションの制約名と参照アクションは Undo/Redo できる")]
    public void RelationshipMetadata_CanUndoRedo()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        vm.Entities[1].Columns.Add(new ColumnViewModel(new Column { Name = "ParentId", DataType = "int" }));
        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);
        var relationship = vm.Relationships[0];

        relationship.ConstraintName = "FK_Test";
        relationship.OnDelete = ForeignKeyReferentialAction.Cascade;
        relationship.OnUpdate = ForeignKeyReferentialAction.SetDefault;

        vm.UndoCommand.Execute(null);
        relationship.OnUpdate.Should().Be(ForeignKeyReferentialAction.NoAction);

        vm.UndoCommand.Execute(null);
        relationship.OnDelete.Should().Be(ForeignKeyReferentialAction.NoAction);

        vm.UndoCommand.Execute(null);
        relationship.ConstraintName.Should().Be("FK_NewTable_NewTable");

        vm.RedoCommand.Execute(null);
        vm.RedoCommand.Execute(null);
        vm.RedoCommand.Execute(null);
        relationship.ConstraintName.Should().Be("FK_Test");
        relationship.OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
        relationship.OnUpdate.Should().Be(ForeignKeyReferentialAction.SetDefault);
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

    [Fact(DisplayName = "エンティティ見出し色変更は Undo/Redo できる")]
    public void EntityTitleBackgroundColorChange_CanUndoRedo()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        var entity = vm.Entities[0];

        entity.TitleBackgroundColor = "#E4F1C9";

        vm.UndoCommand.Execute(null);
        entity.TitleBackgroundColor.Should().Be(Entity.DefaultTitleBackgroundColor);

        vm.RedoCommand.Execute(null);
        entity.TitleBackgroundColor.Should().Be("#E4F1C9");
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

    [Fact(DisplayName = "多対多への変更で列選択クリアも 1 回の Undo/Redo で往復する")]
    public void RelationshipTypeChange_WithDependentColumnClear_CanUndoRedoAsSingleStep()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        vm.Entities[1].Columns.Add(new ColumnViewModel(new Column { Name = "ParentId", DataType = "int" }));
        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);
        var relationship = vm.Relationships[0];
        var originalSourceColumnId = relationship.SourceColumnId;
        var originalTargetColumnId = relationship.TargetColumnId;

        relationship.Type = RelationshipType.ManyToMany;

        relationship.SourceColumnId.Should().BeNull();
        relationship.TargetColumnId.Should().BeNull();

        vm.UndoCommand.Execute(null);
        relationship.Type.Should().Be(RelationshipType.OneToMany);
        relationship.SourceColumnId.Should().Be(originalSourceColumnId);
        relationship.TargetColumnId.Should().Be(originalTargetColumnId);

        vm.RedoCommand.Execute(null);
        relationship.Type.Should().Be(RelationshipType.ManyToMany);
        relationship.SourceColumnId.Should().BeNull();
        relationship.TargetColumnId.Should().BeNull();
    }

    [Fact(DisplayName = "リレーションの FK 列変更でカラム定義側の FK 状態も追従する")]
    public void RelationshipTargetColumnChange_UpdatesForeignKeyFlags()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        vm.Entities[1].Columns.Add(new ColumnViewModel(new Column { Name = "ParentId", DataType = "int" }));
        vm.Entities[1].Columns.Add(new ColumnViewModel(new Column { Name = "ParentCode", DataType = "int" }));
        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);
        var relationship = vm.Relationships[0];
        var originalTargetColumn = vm.Entities[1].Columns.First(column => column.Id == relationship.TargetColumnId);
        var newTargetColumn = vm.Entities[1].Columns.Single(column => column.Name == "ParentCode");

        relationship.TargetColumnId = newTargetColumn.Id;

        originalTargetColumn.IsForeignKey.Should().BeFalse();
        originalTargetColumn.IsForeignKeyEditable.Should().BeTrue();
        newTargetColumn.IsForeignKey.Should().BeTrue();
        newTargetColumn.IsForeignKeyEditable.Should().BeFalse();

        vm.UndoCommand.Execute(null);
        originalTargetColumn.IsForeignKey.Should().BeTrue();
        originalTargetColumn.IsForeignKeyEditable.Should().BeFalse();
        newTargetColumn.IsForeignKey.Should().BeFalse();
        newTargetColumn.IsForeignKeyEditable.Should().BeTrue();
    }

    [Fact(DisplayName = "カラム追加は Undo/Redo できる")]
    public void AddColumn_CanUndoRedo()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.SelectedEntity = vm.Entities[0];
        var initialCount = vm.SelectedEntity.Columns.Count;

        vm.AddColumnCommand.Execute(null);

        vm.SelectedEntity.Columns.Should().HaveCount(initialCount + 1);
        vm.SelectedColumn.Should().Be(vm.SelectedEntity.Columns.Last());

        vm.UndoCommand.Execute(null);
        vm.SelectedEntity.Columns.Should().HaveCount(initialCount);

        vm.RedoCommand.Execute(null);
        vm.SelectedEntity.Columns.Should().HaveCount(initialCount + 1);
    }

    [Fact(DisplayName = "選択エンティティはコピーして貼り付けできる")]
    public void CopyPasteSelectedEntity_AddsShiftedClone()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        var original = vm.Entities[0];
        original.TableName = "Customer";
        original.X = 120;
        original.Y = 200;
        original.Width = 280;
        original.Description = "顧客";
        original.Memo = "メモ";
        original.TitleBackgroundColor = "#E4F1C9";
        original.Columns.Add(
            new ColumnViewModel(
                new Column
                {
                    Name = "Code",
                    DataType = "nvarchar(50)",
                    IsNullable = true,
                    Description = "顧客コード",
                }
            )
        );
        vm.OnEntityClicked(original);

        vm.CopySelectedEntityCommand.Execute(null);
        vm.PasteCopiedEntityCommand.Execute(null);

        vm.Entities.Should().HaveCount(2);
        var pasted = vm.Entities[1];
        pasted.Should().NotBeSameAs(original);
        pasted.Id.Should().NotBe(original.Id);
        pasted.TableName.Should().Be("Customer_Copy");
        pasted.X.Should().Be(150);
        pasted.Y.Should().Be(230);
        pasted.Width.Should().Be(280);
        pasted.Description.Should().Be("顧客");
        pasted.Memo.Should().Be("メモ");
        pasted.TitleBackgroundColor.Should().Be("#E4F1C9");
        pasted.Columns.Should().HaveCount(2);
        pasted.Columns[0].Id.Should().NotBe(original.Columns[0].Id);
        pasted.Columns[1].Id.Should().NotBe(original.Columns[1].Id);
        pasted.Columns[1].Name.Should().Be("Code");
        pasted.Columns[1].Description.Should().Be("顧客コード");
        vm.SelectedEntity.Should().Be(pasted);
        pasted.IsSelected.Should().BeTrue();
        original.IsSelected.Should().BeFalse();
    }

    [Fact(DisplayName = "エンティティ貼り付けは連続実行で位置と名前がずれ、Undo/Redo できる")]
    public void PasteCopiedEntity_CanRepeatAndUndoRedo()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        var original = vm.Entities[0];
        original.TableName = "Order";
        original.X = 90;
        original.Y = 140;
        vm.OnEntityClicked(original);

        vm.CopySelectedEntityCommand.Execute(null);
        vm.PasteCopiedEntityCommand.Execute(null);
        vm.PasteCopiedEntityCommand.Execute(null);

        vm.Entities.Select(entity => entity.TableName).Should().Equal("Order", "Order_Copy", "Order_Copy2");
        vm.Entities[1].X.Should().Be(120);
        vm.Entities[1].Y.Should().Be(170);
        vm.Entities[2].X.Should().Be(150);
        vm.Entities[2].Y.Should().Be(200);

        vm.UndoCommand.Execute(null);
        vm.Entities.Select(entity => entity.TableName).Should().Equal("Order", "Order_Copy");

        vm.RedoCommand.Execute(null);
        vm.Entities.Select(entity => entity.TableName).Should().Equal("Order", "Order_Copy", "Order_Copy2");
    }

    [Fact(DisplayName = "選択カラムはコピーして直下へ貼り付けできる")]
    public void CopyPasteSelectedColumn_InsertsCloneBelowSelection()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.SelectedEntity = vm.Entities[0];
        vm.SelectedEntity.Columns[0].Name = "Id";
        vm.SelectedEntity.Columns.Add(
            new ColumnViewModel(
                new Column
                {
                    Name = "Code",
                    DataType = "nvarchar(50)",
                    IsNullable = true,
                    Description = "コード",
                }
            )
        );
        vm.SelectedEntity.Columns.Add(new ColumnViewModel(new Column { Name = "UpdatedAt", DataType = "datetime2" }));
        vm.SelectedColumn = vm.SelectedEntity.Columns[1];
        var original = vm.SelectedColumn;

        vm.CopySelectedColumnCommand.Execute(null);
        vm.PasteCopiedColumnCommand.Execute(null);

        vm.SelectedEntity.Columns.Select(column => column.Name).Should().Equal("Id", "Code", "Code", "UpdatedAt");
        vm.SelectedColumn.Should().Be(vm.SelectedEntity.Columns[2]);
        vm.SelectedColumn.Should().NotBeSameAs(original);
        vm.SelectedColumn!.Id.Should().NotBe(original.Id);
        vm.SelectedColumn.DataType.Should().Be("nvarchar(50)");
        vm.SelectedColumn.IsNullable.Should().BeTrue();
        vm.SelectedColumn.Description.Should().Be("コード");
    }

    [Fact(DisplayName = "カラム貼り付けは Undo/Redo できる")]
    public void PasteCopiedColumn_CanUndoRedo()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.SelectedEntity = vm.Entities[0];
        vm.SelectedEntity.Columns.Add(new ColumnViewModel(new Column { Name = "Code", DataType = "int" }));
        vm.SelectedColumn = vm.SelectedEntity.Columns[1];

        vm.CopySelectedColumnCommand.Execute(null);
        vm.PasteCopiedColumnCommand.Execute(null);

        vm.SelectedEntity.Columns.Select(column => column.Name).Should().Equal("ID", "Code", "Code");

        vm.UndoCommand.Execute(null);
        vm.SelectedEntity.Columns.Select(column => column.Name).Should().Equal("ID", "Code");

        vm.RedoCommand.Execute(null);
        vm.SelectedEntity.Columns.Select(column => column.Name).Should().Equal("ID", "Code", "Code");
    }

    [Fact(DisplayName = "カラム削除は Undo/Redo できる")]
    public void RemoveColumn_CanUndoRedo()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.SelectedEntity = vm.Entities[0];
        vm.SelectedEntity.Columns.Add(new ColumnViewModel(new Column { Name = "B", DataType = "int" }));
        var removed = vm.SelectedEntity.Columns[0];

        vm.RemoveColumnCommand.Execute(removed);

        vm.SelectedEntity.Columns.Select(c => c.Name).Should().Equal("B");

        vm.UndoCommand.Execute(null);
        vm.SelectedEntity.Columns.Select(c => c.Name).Should().Equal("ID", "B");

        vm.RedoCommand.Execute(null);
        vm.SelectedEntity.Columns.Select(c => c.Name).Should().Equal("B");
    }

    [Fact(DisplayName = "FK に設定済みのカラムを削除して Undo するとリレーションの FK 設定も復元される")]
    public void RemoveColumn_UsedAsFk_UndoRestoresRelationshipFk()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);

        // Entity[1] に FK 列を追加してリレーションを作成する
        vm.Entities[1].Columns.Add(new ColumnViewModel(new Column { Name = "ParentId", DataType = "int" }));
        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);

        var relationship = vm.Relationships[0];

        // FK 列として設定された TargetColumnId を記憶する
        var originalTargetColumnId = relationship.TargetColumnId;
        originalTargetColumnId.Should().NotBeNull();

        // FK カラムを削除する
        vm.SelectedEntity = vm.Entities[1];
        var columnToRemove = vm.Entities[1].Columns.First(c => c.Id == originalTargetColumnId);
        vm.RemoveColumnCommand.Execute(columnToRemove);

        // 削除後: FK 設定がクリアされている
        relationship.TargetColumnId.Should().BeNull();

        // Undo: カラムが復元され、リレーションの FK 設定も復元される
        vm.UndoCommand.Execute(null);
        vm.Entities[1].Columns.Should().Contain(c => c.Id == originalTargetColumnId);
        relationship.TargetColumnId.Should().Be(originalTargetColumnId);

        // Redo: 再度削除されFK設定もクリアされる
        vm.RedoCommand.Execute(null);
        vm.Entities[1].Columns.Should().NotContain(c => c.Id == originalTargetColumnId);
        relationship.TargetColumnId.Should().BeNull();
    }

    [Fact(DisplayName = "PK チェック変更に伴う NULL 変更も 1 回の Undo/Redo で往復する")]
    public void PrimaryKeyChange_WithDependentNullableChange_CanUndoRedoAsSingleStep()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.SelectedEntity = vm.Entities[0];
        vm.SelectedEntity.Columns.Add(
            new ColumnViewModel(
                new Column
                {
                    Name = "Code",
                    DataType = "int",
                    IsNullable = true,
                }
            )
        );
        var column = vm.SelectedEntity.Columns[1];

        column.IsPrimaryKey = true;

        column.IsNullable.Should().BeFalse();

        vm.UndoCommand.Execute(null);
        column.IsPrimaryKey.Should().BeFalse();
        column.IsNullable.Should().BeTrue();

        vm.RedoCommand.Execute(null);
        column.IsPrimaryKey.Should().BeTrue();
        column.IsNullable.Should().BeFalse();
    }

    [Fact(DisplayName = "新規で図をクリアした後は Undo/Redo 履歴もリセットされる")]
    public void NewDiagram_ClearsUndoRedoHistory()
    {
        var vm = new MainViewModel(new StubDialogService());
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

    [Fact(DisplayName = "カラム順変更は Undo/Redo できる")]
    public void ColumnOrderChange_CanUndoRedo()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        var entity = vm.Entities[0];
        entity.Columns[0].Name = "A";
        entity.Columns.Add(new ColumnViewModel(new Column { Name = "B", DataType = "int" }));
        entity.Columns.Add(new ColumnViewModel(new Column { Name = "C", DataType = "int" }));

        vm.UndoRedo.Execute(new MoveColumnOrderCommand(entity.Columns, entity.Columns[0], 2));

        entity.Columns.Select(c => c.Name).Should().Equal("B", "C", "A");

        vm.UndoCommand.Execute(null);
        entity.Columns.Select(c => c.Name).Should().Equal("A", "B", "C");

        vm.RedoCommand.Execute(null);
        entity.Columns.Select(c => c.Name).Should().Equal("B", "C", "A");
    }

    [Fact(DisplayName = "格子整列は Undo/Redo できる")]
    public void AutoLayoutGrid_CanUndoRedo()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        vm.Entities[0].X = 500;
        vm.Entities[0].Y = 300;
        vm.Entities[1].X = 120;
        vm.Entities[1].Y = 700;
        var before = vm.Entities.Select(entity => (entity.X, entity.Y)).ToArray();

        vm.AutoLayoutGridCommand.Execute(null);

        var after = vm.Entities.Select(entity => (entity.X, entity.Y)).ToArray();
        after.Should().NotEqual(before);

        vm.UndoCommand.Execute(null);
        vm.Entities.Select(entity => (entity.X, entity.Y)).Should().Equal(before);

        vm.RedoCommand.Execute(null);
        vm.Entities.Select(entity => (entity.X, entity.Y)).Should().Equal(after);
    }

    [Fact(DisplayName = "木整列は Undo/Redo できる")]
    public void AutoLayoutTree_CanUndoRedo()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        vm.Entities[1].Columns.Add(new ColumnViewModel(new Column { Name = "ParentId", DataType = "int" }));
        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);
        vm.Entities[0].X = 600;
        vm.Entities[0].Y = 500;
        vm.Entities[1].X = 100;
        vm.Entities[1].Y = 150;
        var before = vm.Entities.Select(entity => (entity.X, entity.Y)).ToArray();

        vm.AutoLayoutTreeCommand.Execute(null);

        var after = vm.Entities.Select(entity => (entity.X, entity.Y)).ToArray();
        after.Should().NotEqual(before);

        vm.UndoCommand.Execute(null);
        vm.Entities.Select(entity => (entity.X, entity.Y)).Should().Equal(before);

        vm.RedoCommand.Execute(null);
        vm.Entities.Select(entity => (entity.X, entity.Y)).Should().Equal(after);
    }

    // ---------------- ダイアログサービス (IDialogService) ----------------

    [Fact(DisplayName = "NewDiagram: 確認でキャンセルするとダイアグラムは保持される")]
    public void NewDiagram_ConfirmDeclined_KeepsDiagram()
    {
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = new MainViewModel(dialogs);
        vm.AddEntityCommand.Execute(null);

        vm.NewDiagramCommand.Execute(null);

        vm.Entities.Should().HaveCount(1);
        dialogs.ConfirmMessages.Should().ContainSingle().Which.Should().Contain("クリア");
    }

    [Fact(DisplayName = "NewDiagram: 確認で OK するとダイアグラムがクリアされる")]
    public void NewDiagram_ConfirmAccepted_ClearsDiagram()
    {
        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = new MainViewModel(dialogs);
        vm.AddEntityCommand.Execute(null);

        vm.NewDiagramCommand.Execute(null);

        vm.Entities.Should().BeEmpty();
        dialogs.ConfirmMessages.Should().HaveCount(1);
    }

    [Fact(DisplayName = "重複リレーション作成時は情報ダイアログが表示され追加されない")]
    public void RelationshipMode_DuplicateRelationship_ShowsInformation()
    {
        var dialogs = new StubDialogService();
        var vm = new MainViewModel(dialogs);
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        var a = vm.Entities[0];
        var b = vm.Entities[1];

        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(a);
        vm.OnEntityClicked(b);

        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(a);
        vm.OnEntityClicked(b);

        vm.Relationships.Should().HaveCount(1);
        dialogs.InformationMessages.Should().ContainSingle().Which.Should().Contain("すでに存在します");
    }
}
