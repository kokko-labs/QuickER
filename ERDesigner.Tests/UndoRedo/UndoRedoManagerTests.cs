using ERDesigner.UndoRedo;
using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.UndoRedo;

/// <summary>
/// <see cref="UndoRedoManager"/> の動作を検証するテストクラスです。
/// </summary>
public class UndoRedoManagerTests
{
    /// <summary>
    /// テスト用のシンプルなコマンド。<see cref="Execute"/> と <see cref="Undo"/> の呼び出しを記録します。
    /// </summary>
    private sealed class StubCommand : IUndoableCommand
    {
        public int ExecuteCount { get; private set; }
        public int UndoCount { get; private set; }
        public string Description => "stub";

        public void Execute() => ExecuteCount++;

        public void Undo() => UndoCount++;
    }

    [Fact(DisplayName = "Execute するとコマンドが実行され Undo 可能になる")]
    public void Execute_RunsCommandAndEnablesUndo()
    {
        var mgr = new UndoRedoManager();
        var cmd = new StubCommand();

        mgr.Execute(cmd);

        cmd.ExecuteCount.Should().Be(1);
        mgr.CanUndo.Should().BeTrue();
        mgr.CanRedo.Should().BeFalse();
    }

    [Fact(DisplayName = "Push は Execute を呼ばずに Undo スタックへ積む")]
    public void Push_DoesNotExecuteButRegisters()
    {
        var mgr = new UndoRedoManager();
        var cmd = new StubCommand();

        mgr.Push(cmd);

        cmd.ExecuteCount.Should().Be(0);
        mgr.CanUndo.Should().BeTrue();
    }

    [Fact(DisplayName = "Undo / Redo が正しく繰り返し動作する")]
    public void UndoRedo_RoundTrip()
    {
        var mgr = new UndoRedoManager();
        var cmd = new StubCommand();
        mgr.Execute(cmd);

        mgr.Undo();
        cmd.UndoCount.Should().Be(1);
        mgr.CanRedo.Should().BeTrue();

        mgr.Redo();
        cmd.ExecuteCount.Should().Be(2);
        mgr.CanUndo.Should().BeTrue();
    }

    [Fact(DisplayName = "Execute 後は Redo スタックがクリアされる")]
    public void Execute_ClearsRedoStack()
    {
        var mgr = new UndoRedoManager();
        var a = new StubCommand();
        var b = new StubCommand();

        mgr.Execute(a);
        mgr.Undo();
        mgr.CanRedo.Should().BeTrue();

        mgr.Execute(b);
        mgr.CanRedo.Should().BeFalse();
    }

    [Fact(DisplayName = "同一グループの PropertyChangeCommand は 1 回の Undo/Redo で処理される")]
    public void Push_GroupedPropertyChanges_AreHandledAsSingleStep()
    {
        var mgr = new UndoRedoManager();
        var entity = new EntityViewModel(new ERDesigner.Models.Entity { TableName = "A" });
        var groupId = new object();

        entity.TableName = "B";
        mgr.Push(new PropertyChangeCommand(entity, nameof(EntityViewModel.TableName), "A", "B") { GroupId = groupId });

        entity.Description = "desc";
        mgr.Push(new PropertyChangeCommand(entity, nameof(EntityViewModel.Description), string.Empty, "desc") { GroupId = groupId });

        mgr.Undo();
        entity.TableName.Should().Be("A");
        entity.Description.Should().BeEmpty();

        mgr.Redo();
        entity.TableName.Should().Be("B");
        entity.Description.Should().Be("desc");
    }
}
