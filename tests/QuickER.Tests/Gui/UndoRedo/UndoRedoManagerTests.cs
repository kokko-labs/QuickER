using AwesomeAssertions;
using QuickER.UndoRedo;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary><see cref="UndoRedoManager"/> の Execute / Push / Undo / Redo と変更世代を検証するテストクラス</summary>
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

    /// <summary>指定回数だけ Execute / Undo が例外を投げ、以降は成功するテスト用コマンド</summary>
    private sealed class ThrowingCommand : IUndoableCommand
    {
        /// <summary>残りの Undo 失敗回数</summary>
        public int UndoFailures { get; set; }

        /// <summary>残りの Execute 失敗回数</summary>
        public int ExecuteFailures { get; set; }

        /// <summary>Undo が成功した回数</summary>
        public int UndoSucceeded { get; private set; }

        /// <summary>Execute が成功した回数</summary>
        public int ExecuteSucceeded { get; private set; }

        /// <inheritdoc />
        public string Description => "throwing";

        /// <inheritdoc />
        public void Execute()
        {
            if (ExecuteFailures > 0)
            {
                ExecuteFailures--;
                throw new InvalidOperationException("execute failed");
            }

            ExecuteSucceeded++;
        }

        /// <inheritdoc />
        public void Undo()
        {
            if (UndoFailures > 0)
            {
                UndoFailures--;
                throw new InvalidOperationException("undo failed");
            }

            UndoSucceeded++;
        }
    }

    /// <summary>Undo が例外を投げてもコマンドが undo スタックに残り、再試行できることを検証する</summary>
    /// <remarks>
    /// Pop してから実行する構造のため、戻さないとコマンドが両スタックから消える（＝履歴の握り潰し）。
    /// 失敗した操作をやり直す手段が無くなるうえ、Redo 側にも現れないので状態を戻す術が消える。
    /// </remarks>
    [Fact(DisplayName = "Undo が例外を投げてもコマンドは undo スタックに残り再試行できる")]
    public void Undo_WhenCommandThrows_KeepsCommandOnUndoStackAndNotifies()
    {
        var mgr = new UndoRedoManager();
        var cmd = new ThrowingCommand { UndoFailures = 1 };
        mgr.Push(cmd);

        var generationBefore = mgr.ChangeGeneration;
        var act = () => mgr.Undo();

        act.Should().Throw<InvalidOperationException>("失敗は呼び出し側へ伝える");
        mgr.CanUndo.Should().BeTrue("失敗したコマンドは元のスタックへ戻す");
        mgr.CanRedo.Should().BeFalse("実行できていないので Redo 側へは移さない");
        mgr.ChangeGeneration.Should().NotBe(generationBefore, "状態変更通知を発行する");

        // 2 回目は成功する＝再試行可能であることの実証
        mgr.Undo();
        cmd.UndoSucceeded.Should().Be(1);
        mgr.CanRedo.Should().BeTrue();
    }

    /// <summary>Redo が例外を投げてもコマンドが redo スタックに残り、再試行できることを検証する</summary>
    [Fact(DisplayName = "Redo が例外を投げてもコマンドは redo スタックに残り再試行できる")]
    public void Redo_WhenCommandThrows_KeepsCommandOnRedoStackAndNotifies()
    {
        var mgr = new UndoRedoManager();
        var cmd = new ThrowingCommand();
        mgr.Push(cmd);
        mgr.Undo();

        cmd.ExecuteFailures = 1;
        var generationBefore = mgr.ChangeGeneration;
        var act = () => mgr.Redo();

        act.Should().Throw<InvalidOperationException>();
        mgr.CanRedo.Should().BeTrue("失敗したコマンドは元のスタックへ戻す");
        mgr.CanUndo.Should().BeFalse("実行できていないので Undo 側へは移さない");
        mgr.ChangeGeneration.Should().NotBe(generationBefore, "状態変更通知を発行する");

        mgr.Redo();
        cmd.ExecuteSucceeded.Should().Be(1);
        mgr.CanUndo.Should().BeTrue();
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

    /// <summary>MarkChanged が履歴に触れず変更世代だけを進めることを検証する</summary>
    [Fact(DisplayName = "MarkChanged は履歴を変えずに変更世代だけを進める")]
    public void MarkChanged_BumpsGenerationWithoutTouchingHistory()
    {
        var mgr = new UndoRedoManager();
        var before = mgr.ChangeGeneration;

        mgr.MarkChanged();

        mgr.ChangeGeneration.Should().NotBe(before, "ダーティ判定が動くよう世代を進める");
        mgr.CanUndo.Should().BeFalse("履歴には積まない");
        mgr.CanRedo.Should().BeFalse();
    }

    /// <summary>MarkChanged が Redo スタックを破棄しないことを検証する（履歴に一切関与しない）</summary>
    [Fact(DisplayName = "MarkChanged は Redo スタックを破棄しない")]
    public void MarkChanged_KeepsRedoStack()
    {
        var mgr = new UndoRedoManager();
        mgr.Execute(new StubCommand());
        mgr.Undo();
        mgr.CanRedo.Should().BeTrue();

        mgr.MarkChanged();

        mgr.CanRedo.Should().BeTrue("Execute / Push と違い Redo は破棄しない");
        mgr.CanUndo.Should().BeFalse();
    }
}
