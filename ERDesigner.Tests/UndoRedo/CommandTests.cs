using ERDesigner.Models;
using ERDesigner.UndoRedo;
using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.UndoRedo;

/// <summary>
/// 個々の Undo コマンド (<see cref="MoveEntityCommand"/> など) のテスト。
/// </summary>
public class CommandTests
{
    private static EntityViewModel NewEntity(double x = 0, double y = 0) =>
        new(new Entity { X = x, Y = y, TableName = "T" });

    [Fact(DisplayName = "MoveEntityCommand: Execute / Undo で座標が往復する")]
    public void MoveEntityCommand_ExecuteUndo()
    {
        var e = NewEntity(10, 20);
        var cmd = new MoveEntityCommand(e, 10, 20, 100, 200);

        cmd.Execute();
        e.X.Should().Be(100);
        e.Y.Should().Be(200);

        cmd.Undo();
        e.X.Should().Be(10);
        e.Y.Should().Be(20);
    }

    [Fact(DisplayName = "AddEntityCommand: Undo で取り除かれ、Redo で再追加される")]
    public void AddEntityCommand_RoundTrip()
    {
        var main = new MainViewModel();
        var e = NewEntity();
        var cmd = new AddEntityCommand(main, e);

        cmd.Execute();
        main.Entities.Should().Contain(e);

        cmd.Undo();
        main.Entities.Should().NotContain(e);
    }

    [Fact(DisplayName = "RemoveEntityCommand: 関連リレーションも削除・復元される")]
    public void RemoveEntityCommand_AlsoRemovesRelationships()
    {
        var main = new MainViewModel();
        var a = NewEntity();
        var b = NewEntity();
        main.Entities.Add(a);
        main.Entities.Add(b);

        var rel = new RelationshipViewModel(
            new Relationship { SourceEntityId = a.Id, TargetEntityId = b.Id },
            a, b);
        main.Relationships.Add(rel);

        var cmd = new RemoveEntityCommand(main, a);
        cmd.Execute();

        main.Entities.Should().NotContain(a);
        main.Relationships.Should().NotContain(rel);

        cmd.Undo();
        main.Entities.Should().Contain(a);
        main.Relationships.Should().Contain(rel);
    }

    [Fact(DisplayName = "PropertyChangeCommand: 任意プロパティを Undo/Redo できる")]
    public void PropertyChangeCommand_Works()
    {
        var e = NewEntity();
        var cmd = new PropertyChangeCommand(e, nameof(EntityViewModel.TableName), "T", "顧客");

        cmd.Execute();
        e.TableName.Should().Be("顧客");

        cmd.Undo();
        e.TableName.Should().Be("T");
    }
}
