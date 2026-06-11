using ERDesigner.Models;
using ERDesigner.UndoRedo;
using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.UndoRedo;

/// <summary><see cref="ImportSchemaCommand"/> の置換・復元・リレーション取込を検証するテストクラス</summary>
public class ImportSchemaCommandTests
{
    /// <summary>Execute で既存図が取込内容へ置換され、Undo で元へ復元、再 Execute で再置換されることを検証する</summary>
    [Fact(DisplayName = "Execute で既存ダイアグラムが置換され、Undo で復元される")]
    public void ExecuteUndo_Replaces_And_Restores()
    {
        var main = new MainViewModel();
        var existing = new EntityViewModel(new Entity { TableName = "Old" });
        main.Entities.Add(existing);

        var newEntity = new Entity { TableName = "New" };
        var cmd = new ImportSchemaCommand(main, new[] { newEntity }, Array.Empty<Relationship>());

        cmd.Execute();
        main.Entities.Should().HaveCount(1);
        main.Entities[0].TableName.Should().Be("New");

        cmd.Undo();
        main.Entities.Should().ContainSingle().Which.TableName.Should().Be("Old");

        cmd.Execute();
        main.Entities.Should().ContainSingle().Which.TableName.Should().Be("New");
    }

    /// <summary>エンティティに加えリレーションも取り込まれ、種別が保持されることを検証する</summary>
    [Fact(DisplayName = "リレーションも含めて取り込まれる")]
    public void Execute_ImportsRelationships()
    {
        var main = new MainViewModel();
        var a = new Entity { TableName = "A" };
        var b = new Entity { TableName = "B" };
        var rel = new Relationship
        {
            SourceEntityId = a.Id,
            TargetEntityId = b.Id,
            Type = RelationshipType.OneToMany,
        };

        var cmd = new ImportSchemaCommand(main, new[] { a, b }, new[] { rel });
        cmd.Execute();

        main.Entities.Should().HaveCount(2);
        main.Relationships.Should().HaveCount(1);
        main.Relationships[0].Type.Should().Be(RelationshipType.OneToMany);
    }
}
