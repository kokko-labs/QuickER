using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary><see cref="DuplicateEntityCommand"/> の複製生成と Undo / Redo を検証するテストクラス</summary>
public class DuplicateEntityCommandTests
{
    /// <summary>複製が新しい ID・右下ずらし位置・"_Copy" 名・色・カラムを引き継いで追加されることを検証する</summary>
    [Fact(DisplayName = "Execute: 元と異なる ID/位置の複製が追加される")]
    public void Execute_AddsDuplicate()
    {
        var main = new MainViewModel();
        var src = new EntityViewModel(
            new Entity
            {
                TableName = "Original",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                        Description = "主キー",
                    },
                },
            },
            new EntityLayout
            {
                TitleBackgroundColor = "#E7DDF9",
                X = 10,
                Y = 20,
            }
        );
        main.Entities.Add(src);

        var cmd = new DuplicateEntityCommand(main, src);
        cmd.Execute();

        cmd.Duplicated.Should().NotBeNull();
        main.Entities.Should().Contain(cmd.Duplicated!);
        cmd.Duplicated!.Id.Should().NotBe(src.Id);
        cmd.Duplicated.TableName.Should().EndWith("_Copy");
        cmd.Duplicated.X.Should().Be(40);
        cmd.Duplicated.Y.Should().Be(50);
        cmd.Duplicated.TitleBackgroundColor.Should().Be("#E7DDF9");
        cmd.Duplicated.Columns.Should().HaveCount(1);
        cmd.Duplicated.Columns[0].Description.Should().Be("主キー");
    }

    /// <summary>Undo で複製が除去され、Redo で同一インスタンスが再追加されることを検証する</summary>
    [Fact(DisplayName = "Undo / Redo: 複製の追加・削除が往復する")]
    public void UndoRedo_RoundTrip()
    {
        var main = new MainViewModel();
        var src = new EntityViewModel(new Entity { TableName = "X" });
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
