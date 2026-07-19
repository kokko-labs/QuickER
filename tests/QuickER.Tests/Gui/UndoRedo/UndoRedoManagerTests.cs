using FluentAssertions;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary><see cref="UndoRedoManager"/> の Execute / Push / Undo / Redo と履歴集約を検証するテストクラス</summary>
public class UndoRedoManagerTests
{
    /// <summary>Execute / Undo の呼び出し回数を記録するテスト用コマンド</summary>
    private sealed class StubCommand : IUndoableCommand
    {
        /// <summary>Execute が呼ばれた回数</summary>
        public int ExecuteCount { get; private set; }

        /// <summary>Undo が呼ばれた回数</summary>
        public int UndoCount { get; private set; }

        /// <inheritdoc />
        public string Description => "stub";

        /// <inheritdoc />
        public void Execute() => ExecuteCount++;

        /// <inheritdoc />
        public void Undo() => UndoCount++;
    }

    /// <summary>Execute でコマンドが実行され Undo 可能・Redo 不可になることを検証する</summary>
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

    /// <summary>Push は Execute を呼ばずに Undo スタックへ登録することを検証する</summary>
    [Fact(DisplayName = "Push は Execute を呼ばずに Undo スタックへ積む")]
    public void Push_DoesNotExecuteButRegisters()
    {
        var mgr = new UndoRedoManager();
        var cmd = new StubCommand();

        mgr.Push(cmd);

        cmd.ExecuteCount.Should().Be(0);
        mgr.CanUndo.Should().BeTrue();
    }

    /// <summary>Undo で Undo が、Redo で再 Execute が呼ばれ、可否フラグが連動することを検証する</summary>
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

    /// <summary>Undo 後に新たな Execute を行うと Redo スタックが破棄されることを検証する</summary>
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

    /// <summary>同一 GroupId のプロパティ変更が 1 履歴へ集約され、まとめて Undo / Redo されることを検証する</summary>
    [Fact(DisplayName = "同一グループの PropertyChangeCommand は 1 回の Undo/Redo で処理される")]
    public void Push_GroupedPropertyChanges_AreHandledAsSingleStep()
    {
        var mgr = new UndoRedoManager();
        var entity = new EntityViewModel(new QuickER.Model.Entity { TableName = "A" });
        var groupId = new object();

        entity.TableName = "B";
        var tableNameProp = new TrackedProperty<EntityViewModel>(
            nameof(EntityViewModel.TableName),
            x => x.TableName,
            (x, v) => x.TableName = (string)v!
        );
        mgr.Push(new PropertyChangeCommand(entity, tableNameProp, "A", "B") { GroupId = groupId });

        entity.Description = "desc";
        var descriptionProp = new TrackedProperty<EntityViewModel>(
            nameof(EntityViewModel.Description),
            x => x.Description,
            (x, v) => x.Description = (string)v!
        );
        mgr.Push(
            new PropertyChangeCommand(entity, descriptionProp, string.Empty, "desc")
            {
                GroupId = groupId,
            }
        );

        mgr.Undo();
        entity.TableName.Should().Be("A");
        entity.Description.Should().BeEmpty();

        mgr.Redo();
        entity.TableName.Should().Be("B");
        entity.Description.Should().Be("desc");
    }
}
