using AwesomeAssertions;
using QuickER.Model;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>
/// テーブルの追加・複製・削除の Undo / Redo が、主選択と名前付きクエリを図と食い違わせないこと、
/// 連動変更のスナップショットを決まった順で当てることを固定するテストクラス。
/// </summary>
/// <remarks>既知の失敗は台帳（<see cref="Known"/>）に載せる。突き合わせの規則は <see cref="KnownCaseLedger"/>。</remarks>
public class EntityLifecycleUndoTests
{
    /// <summary>既知の失敗（ケース ID → 期待する失敗の理由の断片）</summary>
    private static readonly KnownCaseLedger Known = new("EntityLifecycleUndoTests", []);

    /// <summary>2 つのテーブル（それぞれ主キー 1 列）を読み込む</summary>
    internal static MainViewModel Build()
    {
        var vm = new MainViewModel();
        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                Entities =
                {
                    new Entity
                    {
                        TableName = "First",
                        Columns =
                        {
                            new Column
                            {
                                Name = "Id",
                                DataType = "int",
                                IsPrimaryKey = true,
                            },
                        },
                    },
                    new Entity
                    {
                        TableName = "Second",
                        Columns =
                        {
                            new Column
                            {
                                Name = "Id",
                                DataType = "int",
                                IsPrimaryKey = true,
                            },
                        },
                    },
                },
            }
        );

        return vm;
    }

    [Fact(
        DisplayName = "テーブルの追加の Undo で、そのテーブルのクエリも外れ、Redo で元の位置へ戻る"
    )]
    public void AddUndo_RemovesQueries_AndRedoRestoresThem()
    {
        Known.Run(
            "add-undo-queries",
            () =>
            {
                var vm = Build();
                vm.Queries.Add(
                    new QueryDefinition { EntityId = vm.Entities[0].Id, Name = "Before" }
                );

                vm.AddEntityCommand.Execute(null);
                var added = vm.Entities[^1];
                var query = new QueryDefinition { EntityId = added.Id, Name = "OnAdded" };
                vm.Queries.Add(query);
                vm.Queries.Add(
                    new QueryDefinition { EntityId = vm.Entities[1].Id, Name = "After" }
                );

                vm.UndoCommand.Execute(null);

                vm.Entities.Should().NotContain(added);
                vm.Queries.Should().NotContain(query, "追加の Undo で、そのテーブルのクエリも外す");

                vm.RedoCommand.Execute(null);

                vm.Entities.Should().Contain(added);
                vm.Queries.Select(q => q.Name).Should().Equal("Before", "OnAdded", "After");
                vm.Queries[1].Should().BeSameAs(query);
            }
        );
    }

    [Fact(
        DisplayName = "連動変更の Undo を外側の追跡停止なしで当てても、その列の続く変更は履歴に残る"
    )]
    public void GroupedUndoWithoutOuterSuspension_KeepsBaselineCurrent()
    {
        var code = new Column
        {
            Name = "Code",
            DataType = "int",
            IsNullable = true,
        };
        var vm = new MainViewModel();
        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                Entities =
                {
                    new Entity { TableName = "Item", Columns = { code } },
                },
            }
        );
        var column = vm.Entities.Single().Columns.Single();

        // 主キー化（NULL 許容の連動を含めて 1 履歴）を、MainViewModel の Undo を通さずに戻す
        // （外側の追跡停止が無いので、取り直しは適用の内側の追跡停止の終わりに走る）
        column.IsPrimaryKey = true;
        vm.UndoRedo.Undo();

        column.IsPrimaryKey.Should().BeFalse();
        column.IsNullable.Should().BeTrue();

        var generation = vm.UndoRedo.ChangeGeneration;
        column.IsNullable = false;

        vm.UndoRedo.ChangeGeneration.Should()
            .Be(generation + 1, "戻した列の基準も現在値で取り直すので、続く変更が記録される");

        vm.UndoCommand.Execute(null);

        column.IsNullable.Should().BeTrue();
    }

    [Fact(
        DisplayName = "連動変更のスナップショットは、辞書の並びに依らず追跡プロパティの宣言順で当てる"
    )]
    public void SnapshotValues_AreAppliedInDeclaredOrder()
    {
        Known.Run(
            "snapshot-order",
            () =>
            {
                var column = new ColumnViewModel(
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    }
                );

                // 主キーを外して NULL を許す変更を、NULL 許容 → 主キーの並びの辞書で当てる
                // （主キーが true の間は IsNullable の setter が値を捨てるため、宣言順でなければ NULL 許容が入らない）
                var snapshot = new Dictionary<string, object?>
                {
                    [nameof(ColumnViewModel.IsNullable)] = true,
                    [nameof(ColumnViewModel.IsPrimaryKey)] = false,
                };

                DiagramChangeTracker.ApplySnapshotValues(column, snapshot);

                column.IsPrimaryKey.Should().BeFalse();
                column.IsNullable.Should().BeTrue("宣言順で当てる");
            }
        );
    }
}
