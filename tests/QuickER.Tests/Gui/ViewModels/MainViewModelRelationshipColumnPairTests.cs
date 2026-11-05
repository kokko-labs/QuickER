using System.IO;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// リレーションの列ペア編集コマンド（行の追加・確定・差し替え・削除）と、その Undo / Redo・
/// 作成時の自動ペア化（複合主キー）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 永続化先は一時フォルダへ隔離し（<c>UsePersistenceForTests</c>）、実 %LOCALAPPDATA% へは触れない。
/// 列ペアの行の差し替え・削除、多対多への変更と列削除で全ペアが消えて 1 回の Undo で戻ることは、
/// 操作の往復の表（<see cref="QuickER.Tests.Gui.UndoRedo.UndoOperationRows"/> の <c>relationship-*</c> の行）が流す。
/// </remarks>
public class MainViewModelRelationshipColumnPairTests : IDisposable
{
    /// <summary>テスト専用の一時作業フォルダ（各テストで独立・後始末で削除する）</summary>
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-relpair-" + Guid.NewGuid().ToString("N")
    );

    public MainViewModelRelationshipColumnPairTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch
        {
            // 後始末の失敗はテスト結果に影響させない
        }
    }

    /// <summary>永続化を一時フォルダへ隔離した空の ViewModel を返す</summary>
    private MainViewModel CreateViewModel()
    {
        var vm = new MainViewModel(new StubDialogService());
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );
        vm.DisableFileWatchingForTests();

        return vm;
    }

    /// <summary>複合主キー (OrderId, LineNo) を持つ親と、対応する列を持つ子を並べた図を返す</summary>
    /// <param name="childColumns">子テーブルの列（PK 以外）を宣言順で指定する</param>
    private MainViewModel CreateDiagramWithCompositeKeyParent(params string[] childColumns)
    {
        var vm = CreateViewModel();
        var parent = new EntityViewModel(
            new Entity
            {
                TableName = "OrderLine",
                Columns =
                {
                    new Column
                    {
                        Name = "OrderId",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                    new Column
                    {
                        Name = "LineNo",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                },
            }
        );
        var child = new Entity
        {
            TableName = "Shipment",
            Columns =
            {
                new Column
                {
                    Name = "ShipmentId",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
            },
        };

        foreach (var columnName in childColumns)
        {
            child.Columns.Add(new Column { Name = columnName, DataType = "int" });
        }

        vm.Entities.Add(parent);
        vm.Entities.Add(new EntityViewModel(child));
        vm.UndoRedo.Clear();

        return vm;
    }

    /// <summary>リレーション作成モードで 2 つのエンティティを順にクリックする</summary>
    private static RelationshipViewModel CreateRelationship(MainViewModel vm)
    {
        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);

        return vm.Relationships[0];
    }

    /// <summary>
    /// 作成時の複合主キーの自動ペア化が、子に引き当てられた親 PK 列だけを（同じ子列へ重複して寄せずに）ペアにし、
    /// ペアの両端をロックして子側へ外部キーの印を付けることを検証する
    /// </summary>
    /// <param name="childColumns">子テーブルの列（PK 以外）を宣言順にカンマ区切りで指定する</param>
    /// <param name="expectedPairs">期待する列ペア（「親列&gt;子列」の「;」区切り・宣言順）</param>
    [Theory(DisplayName = "作成時に親の複合 PK は、子に引き当てられた列だけが重複なくペア化される")]
    [InlineData("OrderId,LineNo", "OrderId>OrderId;LineNo>LineNo")] // 作成時に親の複合 PK が列ごとに自動でペア化される
    [InlineData("OrderId", "OrderId>OrderId")] // 子には OrderId しか無いため、LineNo はペアを作れずパネルでの補完に回る
    [InlineData("OrderLineId", "OrderId>OrderLineId")] // 子の列は「親テーブル名+Id」の 1 本だけ＝両方の親 PK 列が同じ列へ寄る構成
    public void CreateRelationship_CompositePrimaryKey_PairsResolvedKeyColumns(
        string childColumns,
        string expectedPairs
    )
    {
        var vm = CreateDiagramWithCompositeKeyParent(childColumns.Split(','));
        var relationship = CreateRelationship(vm);
        var parent = vm.Entities[0];
        var child = vm.Entities[1];

        var expected = expectedPairs
            .Split(';')
            .Select(pair => pair.Split('>'))
            .Select(names =>
                (
                    parent.Columns.Single(c => c.Name == names[0]).Id,
                    child.Columns.Single(c => c.Name == names[1]).Id
                )
            );

        relationship
            .ColumnPairs.Select(pair => (pair.SourceColumnId, pair.TargetColumnId))
            .Should()
            .Equal(expected);

        // ペアの両端はロックされ、子側は FK フラグが立つ
        foreach (var pair in relationship.ColumnPairs)
        {
            parent
                .Columns.Single(c => c.Id == pair.SourceColumnId)
                .IsPrimaryKeyEditable.Should()
                .BeFalse();
            var target = child.Columns.Single(c => c.Id == pair.TargetColumnId);
            target.IsForeignKey.Should().BeTrue();
            target.IsForeignKeyEditable.Should().BeFalse();
        }
    }

    /// <summary>
    /// 複合主キーの各列が、親テーブル名由来の名前の子列を取り違えずに同名の子列へ付くことを検証する
    /// </summary>
    /// <param name="parentTable">親テーブル名</param>
    /// <param name="firstKey">親の主キーの 1 列目</param>
    /// <param name="secondKey">親の主キーの 2 列目（子にも同名の列がある）</param>
    [Theory(DisplayName = "作成時の複合主キーの自動ペア化は各列を同名の子列へ付ける")]
    [InlineData("Orders", "OrderId", "Revision")]
    [InlineData("Projects", "TenantId", "ProjectId")]
    public void CreateRelationship_CompositeKey_PairsEachColumnWithSameName(
        string parentTable,
        string firstKey,
        string secondKey
    )
    {
        var vm = CreateViewModel();
        vm.Entities.Add(
            new EntityViewModel(
                new Entity
                {
                    TableName = parentTable,
                    Columns =
                    {
                        new Column
                        {
                            Name = firstKey,
                            DataType = "int",
                            IsPrimaryKey = true,
                        },
                        new Column
                        {
                            Name = secondKey,
                            DataType = "int",
                            IsPrimaryKey = true,
                        },
                    },
                }
            )
        );
        vm.Entities.Add(
            new EntityViewModel(
                new Entity
                {
                    TableName = "Child",
                    Columns =
                    {
                        new Column
                        {
                            Name = "ChildId",
                            DataType = "int",
                            IsPrimaryKey = true,
                        },
                        new Column { Name = firstKey, DataType = "int" },
                        new Column { Name = secondKey, DataType = "int" },
                    },
                }
            )
        );
        vm.UndoRedo.Clear();

        var relationship = CreateRelationship(vm);
        var parent = vm.Entities[0];
        var child = vm.Entities[1];

        relationship
            .ColumnPairs.Select(pair =>
                (
                    parent.Columns.Single(c => c.Id == pair.SourceColumnId).Name,
                    child.Columns.Single(c => c.Id == pair.TargetColumnId).Name
                )
            )
            .Should()
            .Equal((firstKey, firstKey), (secondKey, secondKey));
    }

    [Fact(DisplayName = "空スロット行は片側だけの選択では履歴を動かさない")]
    public void PendingColumnPairRow_PartialSelection_DoesNotTouchHistory()
    {
        var vm = CreateDiagramWithCompositeKeyParent("OrderId", "LineNo");
        var relationship = CreateRelationship(vm);

        // 作成時に両 PK 列が埋まっているので、いったん 2 行目を外して空きを作る
        vm.RemoveRelationshipColumnPairCommand.Execute(relationship.ColumnPairRows[1]);
        vm.UndoRedo.Clear();

        // 空スロットの追加も取り消しもモデルを変えないため履歴に残らない
        vm.AddRelationshipColumnPairSlotCommand.Execute(relationship);
        relationship.ColumnPairRows.Should().HaveCount(2);
        vm.UndoRedo.CanUndo.Should().BeFalse();

        vm.RemoveRelationshipColumnPairCommand.Execute(relationship.ColumnPairRows[1]);
        relationship.ColumnPairRows.Should().ContainSingle();
        vm.UndoRedo.CanUndo.Should().BeFalse();

        // 改めて空スロットを出し、片側だけの選択では確定しないことを見る
        vm.AddRelationshipColumnPairSlotCommand.Execute(relationship);
        var pendingRow = relationship.ColumnPairRows[1];
        pendingRow.SelectedSourceColumn = vm.Entities[0].Columns[1];
        vm.UndoRedo.CanUndo.Should().BeFalse("片側だけでは列ペアにならない");
        relationship.ColumnPairs.Should().ContainSingle();

        pendingRow.SelectedTargetColumn = vm.Entities[1].Columns.Single(c => c.Name == "LineNo");
        vm.UndoRedo.CanUndo.Should().BeTrue("両側が揃った時点で 1 履歴になる");
        relationship.ColumnPairs.Should().HaveCount(2);

        vm.UndoRedo.Undo();
        relationship.ColumnPairs.Should().ContainSingle();
    }
}
