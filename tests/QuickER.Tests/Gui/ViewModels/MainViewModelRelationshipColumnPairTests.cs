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
/// 永続化先は一時フォルダへ隔離し（<c>UsePersistenceForTests</c>）、実 %APPDATA% へは触れない。
/// 列削除は「巻き添えでリレーションの列ペアを全クリアし、1 回の Undo で両方戻る」ことも併せて固定する。
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

    [Fact(DisplayName = "作成時に親の複合 PK が列ごとに自動でペア化される")]
    public void CreateRelationship_CompositePrimaryKey_PairsEveryKeyColumn()
    {
        var vm = CreateDiagramWithCompositeKeyParent("OrderId", "LineNo");
        var relationship = CreateRelationship(vm);
        var parent = vm.Entities[0];
        var child = vm.Entities[1];

        relationship.ColumnPairs.Should().HaveCount(2);
        relationship
            .ColumnPairs.Select(pair => (pair.SourceColumnId, pair.TargetColumnId))
            .Should()
            .Equal(
                (parent.Columns[0].Id, child.Columns.Single(c => c.Name == "OrderId").Id),
                (parent.Columns[1].Id, child.Columns.Single(c => c.Name == "LineNo").Id)
            );

        // 両端の全構成列がロックされ、子側は FK フラグが立つ
        parent.Columns.Should().OnlyContain(column => !column.IsPrimaryKeyEditable);
        child
            .Columns.Where(column => column.Name is "OrderId" or "LineNo")
            .Should()
            .OnlyContain(column => column.IsForeignKey && !column.IsForeignKeyEditable);
    }

    [Fact(DisplayName = "作成時に子列を引き当てられない親 PK 列はペアに含まれない")]
    public void CreateRelationship_UnresolvedKeyColumn_IsLeftOutOfPairs()
    {
        // 子には OrderId しか無いため、LineNo はペアを作れずパネルでの補完に回る
        var vm = CreateDiagramWithCompositeKeyParent("OrderId");
        var relationship = CreateRelationship(vm);

        relationship.ColumnPairs.Should().ContainSingle();
        relationship
            .ColumnPairs[0]
            .TargetColumnId.Should()
            .Be(vm.Entities[1].Columns.Single(c => c.Name == "OrderId").Id);
    }

    [Fact(DisplayName = "作成時に複数の親 PK 列が同じ子列へ寄っても子列は重複しない")]
    public void CreateRelationship_DuplicateResolvedTargetColumn_IsSkipped()
    {
        // 子の列は「親テーブル名+Id」の 1 本だけ＝両方の親 PK 列が同じ列へ寄る構成
        var vm = CreateDiagramWithCompositeKeyParent("OrderLineId");
        var relationship = CreateRelationship(vm);
        var duplicateCandidate = vm.Entities[1].Columns.Single(c => c.Name == "OrderLineId");

        relationship.ColumnPairs.Should().ContainSingle();
        relationship.ColumnPairs[0].SourceColumnId.Should().Be(vm.Entities[0].Columns[0].Id);
        relationship.ColumnPairs[0].TargetColumnId.Should().Be(duplicateCandidate.Id);
    }

    [Fact(DisplayName = "列ペア行の確定・差し替え・削除はそれぞれ 1 回の Undo で戻る")]
    public void ColumnPairRow_CommitReplaceRemove_AreSingleUndoSteps()
    {
        var vm = CreateDiagramWithCompositeKeyParent("OrderId", "LineNo", "AltLineNo");
        var relationship = CreateRelationship(vm);
        var child = vm.Entities[1];
        var altLineNo = child.Columns.Single(c => c.Name == "AltLineNo");
        vm.UndoRedo.Clear();

        // 2 行目の子列を差し替える（1 履歴）
        relationship.ColumnPairRows[1].SelectedTargetColumn = altLineNo;
        relationship.ColumnPairs[1].TargetColumnId.Should().Be(altLineNo.Id);

        vm.UndoRedo.Undo();
        relationship
            .ColumnPairs[1]
            .TargetColumnId.Should()
            .Be(child.Columns.Single(c => c.Name == "LineNo").Id);

        vm.UndoRedo.Redo();
        relationship.ColumnPairs[1].TargetColumnId.Should().Be(altLineNo.Id);

        // 2 行目を丸ごと削除する（1 履歴）。単列外部キーへ戻り FK フラグも追従する
        vm.RemoveRelationshipColumnPairCommand.Execute(relationship.ColumnPairRows[1]);
        relationship.ColumnPairs.Should().ContainSingle();
        altLineNo.IsForeignKey.Should().BeFalse();

        vm.UndoRedo.Undo();
        relationship.ColumnPairs.Should().HaveCount(2);
        altLineNo.IsForeignKey.Should().BeTrue();
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

    [Fact(DisplayName = "多対多への変更は複合外部キーの全ペアを消し、1 回の Undo で全て戻る")]
    public void ManyToMany_ClearsEveryColumnPair_AndUndoRestoresThemAtOnce()
    {
        var vm = CreateDiagramWithCompositeKeyParent("OrderId", "LineNo");
        var relationship = CreateRelationship(vm);
        var before = relationship.ColumnPairs.Select(pair => pair.TargetColumnId).ToList();
        before.Should().HaveCount(2);
        vm.UndoRedo.Clear();

        relationship.Type = RelationshipType.ManyToMany;
        relationship.ColumnPairs.Should().BeEmpty();
        relationship.ColumnPairRows.Should().BeEmpty();

        vm.UndoRedo.Undo();
        relationship.Type.Should().Be(RelationshipType.OneToMany);
        relationship.ColumnPairs.Select(pair => pair.TargetColumnId).Should().Equal(before);
        relationship.ColumnPairRows.Should().HaveCount(2);

        vm.UndoRedo.Redo();
        relationship.Type.Should().Be(RelationshipType.ManyToMany);
        relationship.ColumnPairs.Should().BeEmpty();
    }

    [Fact(DisplayName = "列削除は複合外部キーの全ペアを消し、1 回の Undo で全て戻る")]
    public void RemoveColumn_ClearsEveryColumnPair_AndUndoRestoresThemAtOnce()
    {
        var vm = CreateDiagramWithCompositeKeyParent("OrderId", "LineNo");
        var relationship = CreateRelationship(vm);
        var before = relationship.ColumnPairs.Select(pair => pair.TargetColumnId).ToList();
        before.Should().HaveCount(2);

        var child = vm.Entities[1];
        var removed = child.Columns.Single(column => column.Name == "LineNo");
        vm.SelectedEntity = child;
        vm.UndoRedo.Clear();

        vm.RemoveColumnCommand.Execute(removed);

        // 1 組だけ抜いて「縮んだ外部キー」にはせず、リレーション自体は線として残す
        child.Columns.Should().NotContain(removed);
        relationship.ColumnPairs.Should().BeEmpty();
        vm.Relationships.Should().ContainSingle();
        child.Columns.Single(column => column.Name == "OrderId").IsForeignKey.Should().BeFalse();

        vm.UndoRedo.Undo();
        child.Columns.Should().Contain(removed);
        relationship.ColumnPairs.Select(pair => pair.TargetColumnId).Should().Equal(before);
        child.Columns.Single(column => column.Name == "OrderId").IsForeignKey.Should().BeTrue();

        vm.UndoRedo.Redo();
        child.Columns.Should().NotContain(removed);
        relationship.ColumnPairs.Should().BeEmpty();
    }
}
