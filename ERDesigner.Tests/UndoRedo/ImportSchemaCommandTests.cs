using ERDesigner.Models;
using ERDesigner.UndoRedo;
using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.UndoRedo;

/// <summary>
/// <see cref="ImportSchemaCommand"/> のテスト。
/// </summary>
public class ImportSchemaCommandTests
{
    [Fact(DisplayName = "Execute で既存ダイアグラムが置換され、Undo で復元される")]
    public void ExecuteUndo_Replaces_And_Restores()
    {
        var main = new MainViewModel();
        var existing = new EntityViewModel(new Entity { DisplayName = "Old" });
        main.Entities.Add(existing);

        var newEntity = new Entity { DisplayName = "New" };
        var cmd = new ImportSchemaCommand(main,
            new[] { newEntity },
            System.Array.Empty<Relationship>());

        cmd.Execute();
        main.Entities.Should().HaveCount(1);
        main.Entities[0].DisplayName.Should().Be("New");

        cmd.Undo();
        main.Entities.Should().ContainSingle().Which.DisplayName.Should().Be("Old");

        cmd.Execute();
        main.Entities.Should().ContainSingle().Which.DisplayName.Should().Be("New");
    }

    [Fact(DisplayName = "リレーションも含めて取り込まれる")]
    public void Execute_ImportsRelationships()
    {
        var main = new MainViewModel();
        var a = new Entity { DisplayName = "A" };
        var b = new Entity { DisplayName = "B" };
        var rel = new Relationship { SourceEntityId = a.Id, TargetEntityId = b.Id, Type = RelationshipType.OneToMany };

        var cmd = new ImportSchemaCommand(main, new[] { a, b }, new[] { rel });
        cmd.Execute();

        main.Entities.Should().HaveCount(2);
        main.Relationships.Should().HaveCount(1);
        main.Relationships[0].Type.Should().Be(RelationshipType.OneToMany);
    }
}
