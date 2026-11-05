using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>個々の Undo コマンドの、往復の表では見えない動作（プロパティ変更の適用後フック）を検証するテストクラス</summary>
public class CommandTests
{
    /// <summary>テスト用エンティティを生成する</summary>
    private static EntityViewModel NewEntity() =>
        new(new Entity { TableName = "T" }, new EntityLayout());

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
}
