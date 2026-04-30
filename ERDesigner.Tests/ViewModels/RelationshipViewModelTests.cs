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
        new(new Entity { X = x, Y = y, Width = 200 });

    [Theory(DisplayName = "種別ごとに正しいマーカー種別が返る")]
    [InlineData(RelationshipType.OneToOne, RelationshipViewModel.MarkerKind.One, RelationshipViewModel.MarkerKind.One)]
    [InlineData(RelationshipType.OneToMany, RelationshipViewModel.MarkerKind.One, RelationshipViewModel.MarkerKind.Many)]
    [InlineData(RelationshipType.ManyToMany, RelationshipViewModel.MarkerKind.Many, RelationshipViewModel.MarkerKind.Many)]
    public void Markers_AreCorrect(RelationshipType type,
        RelationshipViewModel.MarkerKind expectedSource,
        RelationshipViewModel.MarkerKind expectedTarget)
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

        var changed = new System.Collections.Generic.List<string?>();
        rel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        rel.Type = RelationshipType.OneToMany;

        changed.Should().Contain(nameof(RelationshipViewModel.Label));
        changed.Should().Contain(nameof(RelationshipViewModel.SourceMarker));
        changed.Should().Contain(nameof(RelationshipViewModel.TargetMarker));
        rel.Label.Should().Be("1―N");
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
