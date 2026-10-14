using System.Windows;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Tests.TestSupport;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// ミニマップの描画データが、テーブルの移動・タイトルの色の変更の直後に最新の状態を映すことを固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// ミニマップの線はリレーションの端点（X1 / Y1 / X2 / Y2）から作る。テーブルの位置の変更通知は、
/// ミニマップを作り直す側が先に受け、リレーションが端点を計算し直すのはその後になるため、
/// 端点をそのまま読むと 1 つ前の位置の線が残る。
/// </para>
/// <para>既知の失敗は台帳（<see cref="Known"/>）に載せる。突き合わせの規則は <see cref="KnownFailureLedger"/>。</para>
/// </remarks>
public class MiniMapFreshnessTests
{
    /// <summary>既知の失敗（ケース ID → 期待する失敗の理由の断片）</summary>
    private static readonly KnownFailureLedger Known = new(new Dictionary<string, string>());

    /// <summary>親子 2 テーブルをリレーションで結び、離して置いた図を読み込む</summary>
    private static MainViewModel Build(int extraTables = 0)
    {
        var parent = new Entity
        {
            TableName = "Parent",
            Columns =
            {
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
            },
        };
        var child = new Entity
        {
            TableName = "Child",
            Columns =
            {
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
                new Column { Name = "ParentId", DataType = "int" },
            },
        };
        var diagram = new ErDiagram
        {
            Entities = { parent, child },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    ColumnPairs =
                    {
                        new RelationshipColumnPair(parent.Columns[0].Id, child.Columns[1].Id),
                    },
                },
            },
        };

        for (var i = 0; i < extraTables; i++)
        {
            diagram.Entities.Add(new Entity { TableName = "Extra" + i });
        }

        var vm = new MainViewModel();
        vm.ReplaceDiagramFromModule(diagram);
        vm.Entities[0].X = 0;
        vm.Entities[0].Y = 0;
        vm.Entities[1].X = 600;
        vm.Entities[1].Y = 0;

        return vm;
    }

    /// <summary>ミニマップ上の子テーブルの矩形（少し広げる）が、線の子側の端を含むことを表明する</summary>
    private static void AssertLineEndsOnChild(MainViewModel vm)
    {
        var childIndex = vm.Entities.IndexOf(vm.Relationships[0].Target);
        var rect = vm.MiniMapEntities[childIndex];
        var bounds = new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        bounds.Inflate(0.5, 0.5);
        var line = vm.MiniMapLines.Single();

        bounds
            .Contains(new Point(line.X2, line.Y2))
            .Should()
            .BeTrue(
                $"移動した直後の線の端はテーブルの縁にある（線の端 ({line.X2:F1}, {line.Y2:F1})・矩形 {bounds}）"
            );
    }

    [Fact(
        DisplayName = "テーブルを動かした直後のミニマップの線は、動かした後の位置のテーブルにつながる"
    )]
    public void LineFollowsEntityMove()
    {
        Known.Run(
            "line-after-move",
            () =>
            {
                var vm = Build();

                vm.Entities[1].Y = 500;

                AssertLineEndsOnChild(vm);
            }
        );
    }

    [Fact(
        DisplayName = "テーブルの移動を Undo した直後のミニマップの線は、戻した位置のテーブルにつながる"
    )]
    public void LineFollowsMoveUndo()
    {
        Known.Run(
            "line-after-undo",
            () =>
            {
                var vm = Build();
                vm.UndoRedo.Execute(
                    new QuickER.UndoRedo.MoveEntityCommand(vm.Entities[1], 600, 0, 900, 500)
                );

                vm.UndoCommand.Execute(null);

                vm.Entities[1].Y.Should().Be(0);
                AssertLineEndsOnChild(vm);
            }
        );
    }

    [Fact(DisplayName = "テーブルのタイトルの色を変えると、ミニマップの色も変わる")]
    public void TitleColorReachesMiniMap()
    {
        Known.Run(
            "title-color",
            () =>
            {
                var vm = Build();

                vm.Entities[0].TitleBackgroundColor = "#FF112233";

                vm.MiniMapEntities[0]
                    .TitleBackgroundColor.Should()
                    .Be(
                        vm.Entities[0].TitleBackgroundColor,
                        "タイトルの色の変更をミニマップへ映す"
                    );
            }
        );
    }

    [Theory(
        DisplayName = "選んだテーブルの色をまとめて変えても、ミニマップの作り直しは 1 回で、色は映る"
    )]
    [InlineData(10)]
    [InlineData(30)]
    public void BulkTitleColor_RebuildsMiniMapOnce(int extraTables)
    {
        Known.Run(
            "bulk-title-color-count",
            () =>
            {
                var vm = Build(extraTables);
                vm.SelectAllEntitiesCommand.Execute(null);
                var before = vm.MiniMapRecalculationCountForTests;

                vm.BulkChangeTitleColorCommand.Execute("#FF445566");

                (vm.MiniMapRecalculationCountForTests - before)
                    .Should()
                    .Be(1, "まとめての色の変更でミニマップの作り直しは 1 回");
                vm.MiniMapEntities.Select(entity => entity.TitleBackgroundColor)
                    .Should()
                    .OnlyContain(color => color == vm.Entities[0].TitleBackgroundColor);
            }
        );
    }
}
