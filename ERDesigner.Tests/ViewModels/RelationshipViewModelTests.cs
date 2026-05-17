using ERDesigner.Models;
using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.ViewModels;

/// <summary>
/// <see cref="RelationshipViewModel"/> の幾何計算・マーカー種別のテスト。
/// </summary>
public class RelationshipViewModelTests
{
    private static EntityViewModel NewEntity(double x, double y) =>
        new(
            new Entity
            {
                X = x,
                Y = y,
                Width = 200,
            }
        );

    [Theory(DisplayName = "種別ごとに正しいマーカー種別が返る")]
    [InlineData(RelationshipType.OneToOne, RelationshipViewModel.MarkerKind.One, RelationshipViewModel.MarkerKind.One)]
    [InlineData(RelationshipType.OneToMany, RelationshipViewModel.MarkerKind.One, RelationshipViewModel.MarkerKind.Many)]
    [InlineData(RelationshipType.ManyToMany, RelationshipViewModel.MarkerKind.Many, RelationshipViewModel.MarkerKind.Many)]
    public void Markers_AreCorrect(RelationshipType type, RelationshipViewModel.MarkerKind expectedSource, RelationshipViewModel.MarkerKind expectedTarget)
    {
        var a = NewEntity(0, 0);
        var b = NewEntity(300, 0);
        var rel = new RelationshipViewModel(new Relationship { Type = type }, a, b);

        rel.SourceMarker.Should().Be(expectedSource);
        rel.TargetMarker.Should().Be(expectedTarget);
    }

    [Fact(DisplayName = "種別を変更すると Label・マーカーの変更通知が走る")]
    public void TypeChanged_RaisesNotifications()
    {
        var a = NewEntity(0, 0);
        var b = NewEntity(300, 0);
        var rel = new RelationshipViewModel(new Relationship { Type = RelationshipType.OneToOne }, a, b);

        var changed = new List<string?>();
        rel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        rel.Type = RelationshipType.OneToMany;

        changed.Should().Contain(nameof(RelationshipViewModel.Label));
        changed.Should().Contain(nameof(RelationshipViewModel.SourceMarker));
        changed.Should().Contain(nameof(RelationshipViewModel.TargetMarker));
        rel.Label.Should().Be("1―N");
    }

    [Fact(DisplayName = "ラベルはエンティティ間の中央に表示される")]
    public void Label_IsCenteredBetweenEntityBounds()
    {
        var a = new EntityViewModel(
            new Entity
            {
                X = 0,
                Y = 0,
                Width = 100,
            }
        );
        var b = new EntityViewModel(
            new Entity
            {
                X = 500,
                Y = 0,
                Width = 300,
            }
        );
        var rel = new RelationshipViewModel(new Relationship { Type = RelationshipType.OneToMany }, a, b);

        rel.LabelX.Should().Be(300);
    }

    [Fact(DisplayName = "端点マーカーはエンティティと重ならない位置に表示される")]
    public void Markers_ArePositionedOutsideEntities()
    {
        var a = new EntityViewModel(
            new Entity
            {
                X = 100,
                Y = 100,
                Width = 200,
            }
        );
        var b = new EntityViewModel(
            new Entity
            {
                X = 100,
                Y = 400,
                Width = 200,
            }
        );
        var rel = new RelationshipViewModel(new Relationship { Type = RelationshipType.OneToMany }, a, b);

        rel.SourceMarkerY.Should().BeGreaterThan(a.Y + a.DisplayHeight + 10);
        rel.TargetMarkerY.Should().BeLessThan(b.Y - 10);
    }

    [Fact(DisplayName = "横方向でも端点マーカーはエンティティと重ならない位置に表示される")]
    public void Markers_ArePositionedOutsideEntities_Horizontally()
    {
        var a = new EntityViewModel(
            new Entity
            {
                X = 100,
                Y = 100,
                Width = 200,
            }
        );
        var b = new EntityViewModel(
            new Entity
            {
                X = 400,
                Y = 100,
                Width = 200,
            }
        );
        var rel = new RelationshipViewModel(new Relationship { Type = RelationshipType.OneToMany }, a, b);

        rel.SourceMarkerX.Should().BeGreaterThan(a.X + a.Width + 10);
        rel.TargetMarkerX.Should().BeLessThan(b.X - 10);
    }

    [Fact(DisplayName = "端点マーカーの描画領域中央はリレーション線上に一致する")]
    public void MarkerBounds_AreCenteredOnMarkerCoordinates()
    {
        var a = new EntityViewModel(
            new Entity
            {
                X = 100,
                Y = 100,
                Width = 200,
            }
        );
        var b = new EntityViewModel(
            new Entity
            {
                X = 400,
                Y = 260,
                Width = 200,
            }
        );
        var rel = new RelationshipViewModel(new Relationship { Type = RelationshipType.OneToMany }, a, b);

        (rel.SourceMarkerLeft + 10).Should().BeApproximately(rel.SourceMarkerX, 0.001);
        (rel.SourceMarkerTop + 10).Should().BeApproximately(rel.SourceMarkerY, 0.001);
        (rel.TargetMarkerLeft + 10).Should().BeApproximately(rel.TargetMarkerX, 0.001);
        (rel.TargetMarkerTop + 10).Should().BeApproximately(rel.TargetMarkerY, 0.001);
    }

    [Fact(DisplayName = "N側マーカーは対象エンティティ側を向く")]
    public void TargetManyMarker_FacesTargetEntity()
    {
        var a = new EntityViewModel(
            new Entity
            {
                X = 100,
                Y = 100,
                Width = 200,
            }
        );
        var b = new EntityViewModel(
            new Entity
            {
                X = 400,
                Y = 100,
                Width = 200,
            }
        );
        var rel = new RelationshipViewModel(new Relationship { Type = RelationshipType.OneToMany }, a, b);

        rel.TargetMarkerAngle.Should().BeApproximately(0, 0.001);
    }

    [Fact(DisplayName = "起点がN側のときマーカーは起点エンティティ側を向く")]
    public void SourceManyMarker_FacesSourceEntity()
    {
        var a = new EntityViewModel(
            new Entity
            {
                X = 100,
                Y = 100,
                Width = 200,
            }
        );
        var b = new EntityViewModel(
            new Entity
            {
                X = 400,
                Y = 100,
                Width = 200,
            }
        );
        var rel = new RelationshipViewModel(new Relationship { Type = RelationshipType.ManyToMany }, a, b);

        rel.SourceMarkerAngle.Should().BeApproximately(180, 0.001);
    }

    [Fact(DisplayName = "MainViewModel.RemoveSelectedRelationship でリレーションが削除される")]
    public void RemoveSelectedRelationship_Works()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);
        var rel = vm.Relationships[0];

        vm.OnRelationshipClicked(rel);
        vm.SelectedRelationship.Should().Be(rel);

        vm.RemoveSelectedRelationshipCommand.Execute(null);

        vm.Relationships.Should().BeEmpty();
        vm.SelectedRelationship.Should().BeNull();

        // Undo で復元されること
        vm.UndoCommand.Execute(null);
        vm.Relationships.Should().Contain(rel);
    }

    [Fact(DisplayName = "参照先列と外部キー列の候補が取得できる")]
    public void AvailableColumns_AreResolved()
    {
        var source = new EntityViewModel(
            new Entity
            {
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                    new Column { Name = "Code", DataType = "nvarchar(20)" },
                },
            }
        );
        var target = new EntityViewModel(
            new Entity
            {
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                    new Column { Name = "ParentId", DataType = "int" },
                },
            }
        );
        var rel = new RelationshipViewModel(new Relationship { Type = RelationshipType.OneToMany }, source, target);

        rel.AvailableSourceColumns.Should().ContainSingle(c => c.Name == "Id");
        rel.AvailableTargetColumns.Should().HaveCount(2);
        rel.CanSelectForeignKeyColumns.Should().BeTrue();
    }

    [Fact(DisplayName = "多対多では列選択が無効化される")]
    public void ManyToMany_DisablesColumnSelection()
    {
        var a = NewEntity(0, 0);
        var b = NewEntity(300, 0);
        a.Columns.Add(new ColumnViewModel(new Column { Name = "ParentId", DataType = "int" }));
        b.Columns.Add(new ColumnViewModel(new Column { Name = "ChildId", DataType = "int" }));
        var rel = new RelationshipViewModel(new Relationship { Type = RelationshipType.OneToMany }, a, b) { SourceColumnId = a.Columns[0].Id, TargetColumnId = b.Columns[0].Id };

        rel.Type = RelationshipType.ManyToMany;

        rel.CanSelectForeignKeyColumns.Should().BeFalse();
        rel.SourceColumnId.Should().BeNull();
        rel.TargetColumnId.Should().BeNull();
    }

    [Fact(DisplayName = "多対多では ON DELETE と ON UPDATE が無効化され既定値へ戻る")]
    public void ManyToMany_DisablesReferentialActions()
    {
        var a = NewEntity(0, 0);
        var b = NewEntity(300, 0);
        var rel = new RelationshipViewModel(
            new Relationship
            {
                Type = RelationshipType.OneToMany,
                OnDelete = ForeignKeyReferentialAction.Cascade,
                OnUpdate = ForeignKeyReferentialAction.SetNull,
            },
            a,
            b
        );

        rel.Type = RelationshipType.ManyToMany;

        rel.CanConfigureReferentialActions.Should().BeFalse();
        rel.OnDelete.Should().Be(ForeignKeyReferentialAction.NoAction);
        rel.OnUpdate.Should().Be(ForeignKeyReferentialAction.NoAction);
    }

    [Fact(DisplayName = "自己参照リレーションでは自己参照ループ描画情報が有効になる")]
    public void SelfRelationship_UsesSelfLoopGeometry()
    {
        var entity = NewEntity(100, 120);
        entity.Columns.Add(new ColumnViewModel(new Column { Name = "ParentId", DataType = "int" }));
        var rel = new RelationshipViewModel(new Relationship { Type = RelationshipType.OneToMany, TargetColumnId = entity.Columns.Last().Id }, entity, entity);

        rel.IsSelfRelationship.Should().BeTrue();
        rel.ShowSelfLoop.Should().BeTrue();
        rel.ShowEndpointMarkers.Should().BeFalse();
        rel.SelfLoopLeft.Should().BeGreaterThan(entity.X);
        rel.LabelX.Should().BeApproximately(rel.SelfLoopLeft + rel.SelfLoopWidth / 2 + 10, 0.001);
        rel.LabelY.Should().BeApproximately(rel.SelfLoopTop + rel.SelfLoopHeight / 2, 0.001);
    }

    [Fact(DisplayName = "同じカラムに PK と FK が両方設定されても状態は保持できる")]
    public void Column_CanHoldPkAndFkTogether()
    {
        var column = new ColumnViewModel(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
                IsForeignKey = true,
            }
        );

        column.IsPrimaryKey.Should().BeTrue();
        column.IsForeignKey.Should().BeTrue();
    }

    [Fact(DisplayName = "MainViewModel.RemoveColumn で指定カラムが削除される")]
    public void RemoveColumn_Works()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        var entity = vm.Entities[0];
        var col = entity.Columns[0];

        vm.RemoveColumnCommand.Execute(col);

        entity.Columns.Should().NotContain(col);
    }

    [Fact(DisplayName = "SqlDataTypes に SQL Server の型が含まれる")]
    public void SqlDataTypes_IncludesCommonTypes()
    {
        var vm = new MainViewModel();
        vm.SqlDataTypes.Should().Contain("int");
        vm.SqlDataTypes.Should().Contain("nvarchar(100)");
        vm.SqlDataTypes.Should().Contain("datetime2");
        vm.SqlDataTypes.Should().Contain("uniqueidentifier");
    }
}
