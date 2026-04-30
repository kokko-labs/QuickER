using ERDesigner.Models;
using ERDesigner.UndoRedo;
using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.UndoRedo;

/// <summary>
/// <see cref="DuplicateEntityCommand"/> のテスト。
/// </summary>
public class DuplicateEntityCommandTests
{
    [Fact(DisplayName = "Execute: 元と異なる ID/位置の複製が追加される")]
    public void Execute_AddsDuplicate()
    {
        var main = new MainViewModel();
        var src = new EntityViewModel(new Entity
        {
            DisplayName = "Original",
            TableName = "Tbl",
            X = 10, Y = 20,
            Columns = { new Column { Name = "Id", DataType = "int", IsPrimaryKey = true } }
        });
        main.Entities.Add(src);

        var cmd = new DuplicateEntityCommand(main, src);
        cmd.Execute();

        cmd.Duplicated.Should().NotBeNull();
        main.Entities.Should().Contain(cmd.Duplicated!);
        cmd.Duplicated!.Id.Should().NotBe(src.Id);
        cmd.Duplicated.DisplayName.Should().EndWith("_Copy");
        cmd.Duplicated.X.Should().Be(40);
        cmd.Duplicated.Y.Should().Be(50);
        cmd.Duplicated.Columns.Should().HaveCount(1);
    }

    [Fact(DisplayName = "Undo / Redo: 複製の追加・削除が往復する")]
    public void UndoRedo_RoundTrip()
    {
        var main = new MainViewModel();
        var src = new EntityViewModel(new Entity { DisplayName = "X" });
        main.Entities.Add(src);

        var cmd = new DuplicateEntityCommand(main, src);
        cmd.Execute();
        var dup = cmd.Duplicated!;
        main.Entities.Should().Contain(dup);

        cmd.Undo();
        main.Entities.Should().NotContain(dup);

        cmd.Execute();
        main.Entities.Should().Contain(dup);
    }
}
