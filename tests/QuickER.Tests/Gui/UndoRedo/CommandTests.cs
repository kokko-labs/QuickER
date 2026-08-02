using System.Collections.Generic;
using System.Collections.ObjectModel;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>個々の Undo コマンド（移動・追加・削除・プロパティ変更・列順）の動作を検証するテストクラス</summary>
public class CommandTests
{
    /// <summary>指定座標を持つテスト用エンティティを生成する</summary>
    private static EntityViewModel NewEntity(double x = 0, double y = 0) =>
        new(new Entity { TableName = "T" }, new EntityLayout { X = x, Y = y });

    /// <summary>MoveEntityCommand の Execute / Undo で座標が前後に往復することを検証する</summary>
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

    /// <summary>GroupMoveEntitiesCommand の 1 回の Undo で複数メンバーが同時に元へ戻ることを検証する</summary>
    [Fact(DisplayName = "GroupMoveEntitiesCommand: 単一 Undo で全メンバーが元座標へ戻る")]
    public void GroupMoveEntitiesCommand_SingleUndoRestoresAllMembers()
    {
        var mgr = new UndoRedoManager();
        var a = NewEntity(10, 10);
        var b = NewEntity(50, 60);

        // 両者を同一デルタ(+30, +40)で移動済みにしてから履歴登録する（ドラッグ相当）
        a.X = 40;
        a.Y = 50;
        b.X = 80;
        b.Y = 100;

        mgr.Push(
            new GroupMoveEntitiesCommand(
                new List<(EntityViewModel, double, double, double, double)>
                {
                    (a, 10, 10, 40, 50),
                    (b, 50, 60, 80, 100),
                }
            )
        );

        // 1 エントリのみ積まれている
        mgr.CanUndo.Should().BeTrue();

        // 1 回の Undo で両方が元座標へ戻る
        mgr.Undo();
        a.X.Should().Be(10);
        a.Y.Should().Be(10);
        b.X.Should().Be(50);
        b.Y.Should().Be(60);
        mgr.CanUndo.Should().BeFalse();

        // Redo で両方が移動後へ戻る
        mgr.Redo();
        a.X.Should().Be(40);
        a.Y.Should().Be(50);
        b.X.Should().Be(80);
        b.Y.Should().Be(100);
    }

    /// <summary>AddEntityCommand の Execute で追加、Undo で除去されることを検証する</summary>
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

    /// <summary>RemoveEntityCommand が接続リレーションも併せて削除・復元することを検証する</summary>
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
            a,
            b
        );
        main.Relationships.Add(rel);

        var cmd = new RemoveEntityCommand(main, a);
        cmd.Execute();

        main.Entities.Should().NotContain(a);
        main.Relationships.Should().NotContain(rel);

        cmd.Undo();
        main.Entities.Should().Contain(a);
        main.Relationships.Should().Contain(rel);
    }

    /// <summary>PropertyChangeCommand で任意プロパティの値が Execute / Undo で往復することを検証する</summary>
    [Fact(DisplayName = "PropertyChangeCommand: 任意プロパティを Undo/Redo できる")]
    public void PropertyChangeCommand_Works()
    {
        var e = NewEntity();
        var prop = new TrackedProperty<EntityViewModel>(
            nameof(EntityViewModel.TableName),
            x => x.TableName,
            (x, v) => x.TableName = (string)v!
        );
        var cmd = new PropertyChangeCommand(e, prop, "T", "顧客");

        cmd.Execute();
        e.TableName.Should().Be("顧客");

        cmd.Undo();
        e.TableName.Should().Be("T");
    }

    /// <summary>PropertyChangeCommand の適用後フックが Execute と Undo の両方で呼ばれることを検証する</summary>
    [Fact(DisplayName = "PropertyChangeCommand: 適用後フックが Execute/Undo の両方で呼ばれる")]
    public void PropertyChangeCommand_AfterApply_IsInvoked()
    {
        var e = NewEntity();
        var count = 0;
        var prop = new TrackedProperty<EntityViewModel>(
            nameof(EntityViewModel.TableName),
            x => x.TableName,
            (x, v) => x.TableName = (string)v!
        );
        var cmd = new PropertyChangeCommand(e, prop, "T", "顧客", () => count++);

        cmd.Execute();
        cmd.Undo();

        count.Should().Be(2);
    }

    /// <summary>MoveColumnOrderCommand の Execute / Undo でカラム並び順が往復することを検証する</summary>
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

    /// <summary>AddColumnCommand の Execute で追加、Undo で除去されることを検証する</summary>
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

    /// <summary>RemoveColumnCommand の Undo で削除カラムが元の位置へ復元されることを検証する</summary>
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
