using System.Collections.ObjectModel;
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
        new(
            new Entity
            {
                X = x,
                Y = y,
                TableName = "T",
            }
        );

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

        var rel = new RelationshipViewModel(new Relationship { SourceEntityId = a.Id, TargetEntityId = b.Id }, a, b);
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

    [Fact(DisplayName = "PropertyChangeCommand: 適用後フックが Execute/Undo の両方で呼ばれる")]
    public void PropertyChangeCommand_AfterApply_IsInvoked()
    {
        var e = NewEntity();
        var count = 0;
        var cmd = new PropertyChangeCommand(e, nameof(EntityViewModel.TableName), "T", "顧客", () => count++);

        cmd.Execute();
        cmd.Undo();

        count.Should().Be(2);
    }

    [Fact(DisplayName = "MoveColumnOrderCommand: Execute / Undo でカラム順が往復する")]
    public void MoveColumnOrderCommand_ExecuteUndo()
    {
        var first = new ColumnViewModel(new Column { Name = "A", DataType = "int" });
        var second = new ColumnViewModel(new Column { Name = "B", DataType = "int" });
        var third = new ColumnViewModel(new Column { Name = "C", DataType = "int" });
        var columns = new ObservableCollection<ColumnViewModel> { first, second, third };
        var cmd = new MoveColumnOrderCommand(columns, first, 2);

        cmd.Execute();
        columns.Select(x => x.Name).Should().Equal("B", "C", "A");

        cmd.Undo();
        columns.Select(x => x.Name).Should().Equal("A", "B", "C");
    }

    [Fact(DisplayName = "AddColumnCommand: Undo で取り除かれ、Redo で再追加される")]
    public void AddColumnCommand_RoundTrip()
    {
        var columns = new ObservableCollection<ColumnViewModel>();
        var column = new ColumnViewModel(new Column { Name = "Code", DataType = "int" });
        var cmd = new AddColumnCommand(columns, column);

        cmd.Execute();
        columns.Should().Contain(column);

        cmd.Undo();
        columns.Should().NotContain(column);
    }

    [Fact(DisplayName = "RemoveColumnCommand: Undo で元の位置に復元される")]
    public void RemoveColumnCommand_RoundTrip()
    {
        var first = new ColumnViewModel(new Column { Name = "A", DataType = "int" });
        var second = new ColumnViewModel(new Column { Name = "B", DataType = "int" });
        var third = new ColumnViewModel(new Column { Name = "C", DataType = "int" });
        var columns = new ObservableCollection<ColumnViewModel> { first, second, third };
        var cmd = new RemoveColumnCommand(columns, second, [], () => { });

        cmd.Execute();
        columns.Select(x => x.Name).Should().Equal("A", "C");

        cmd.Undo();
        columns.Select(x => x.Name).Should().Equal("A", "B", "C");
    }
}
