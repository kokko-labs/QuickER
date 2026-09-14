using AwesomeAssertions;
using QuickER.Model;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>
/// 図の一括置換（<c>Clear()</c> を伴う経路）で旧要素の変更追跡・イベント購読が漏れなく終了することを
/// 検証するテストクラス。
/// </summary>
/// <remarks>
/// <c>ObservableCollection.Clear()</c> は Reset 通知で <c>OldItems</c> を持たないため、
/// <c>CollectionChanged</c> 側の自動解除が一切効かない。解除が漏れると
/// <see cref="DiagramChangeTracker"/> のスナップショット辞書に旧要素が残り続け（＝図 1 枚分ずつ単調増加）、
/// 生き残った購読経由で旧 VM のプロパティ変更が現在の Undo スタックへ幽霊コマンドを積む
/// （＝クリーンなはずの文書が無操作でダーティになる）。ビルドでも型検査でも出ないためここで固定する。
/// </remarks>
public class DiagramChangeTrackerDetachTests
{
    /// <summary>1 テーブル 1 列 1 制約のエンティティを組み立てる</summary>
    private static Entity BuildEntity(string tableName)
    {
        var column = new Column { Name = tableName + "Id", DataType = "int" };
        var entity = new Entity { TableName = tableName };
        entity.Columns.Add(column);
        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_" + tableName, ColumnIds = { column.Id } }
        );
        return entity;
    }

    /// <summary>親子 2 テーブル＋リレーション 1 本の図を組み立てる</summary>
    private static ErDiagram BuildDiagram(string prefix)
    {
        var parent = BuildEntity(prefix + "Parent");
        var child = BuildEntity(prefix + "Child");

        var relationship = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ConstraintName = "FK_" + prefix,
        };

        return new ErDiagram { Entities = { parent, child }, Relationships = { relationship } };
    }

    /// <summary>置換後、旧エンティティ・旧カラム・旧一意制約・旧リレーションの編集が履歴へ届かないことを検証する</summary>
    [Fact(DisplayName = "図を置換すると旧要素の編集は履歴へ届かない")]
    public void ReplaceDiagram_DetachesPreviousElementsFromTracking()
    {
        var vm = new MainViewModel();
        vm.ReplaceDiagramFromModule(BuildDiagram("Old"));

        var oldEntity = vm.Entities[0];
        var oldColumn = oldEntity.Columns[0];
        var oldConstraint = oldEntity.UniqueConstraints[0];
        var oldRelationship = vm.Relationships[0];

        // 旧図とは Guid がまったく重ならない図で丸ごと置換する
        vm.ReplaceDiagramFromModule(BuildDiagram("New"));

        var generationBefore = vm.UndoRedo.ChangeGeneration;

        oldEntity.TableName = "GhostTable";
        oldEntity.Memo = "GhostMemo";
        oldColumn.Name = "GhostColumn";
        oldColumn.DataType = "nvarchar(10)";
        oldConstraint.Name = "GhostUnique";
        oldRelationship.ConstraintName = "GhostFk";

        vm.UndoRedo.ChangeGeneration.Should()
            .Be(generationBefore, "図から外れた要素の変更は変更世代を動かさない");
        vm.UndoRedo.CanUndo.Should().BeFalse("幽霊コマンドが Undo スタックへ積まれない");
    }

    /// <summary>置換後、追跡器のスナップショット辞書に旧要素が残らない（リークしない）ことを検証する</summary>
    [Fact(DisplayName = "図を置換すると追跡器に旧要素が残らない")]
    public void ReplaceDiagram_RemovesPreviousElementsFromSnapshots()
    {
        var vm = new MainViewModel();
        vm.ReplaceDiagramFromModule(BuildDiagram("Old"));

        var oldEntity = vm.Entities[0];
        var oldColumn = oldEntity.Columns[0];
        var oldConstraint = oldEntity.UniqueConstraints[0];
        var oldRelationship = vm.Relationships[0];

        var tracker = vm.ChangeTrackerForTests;
        tracker.IsTrackedForTests(oldEntity).Should().BeTrue("置換前は追跡されている");

        vm.ReplaceDiagramFromModule(BuildDiagram("New"));

        tracker.IsTrackedForTests(oldEntity).Should().BeFalse();
        tracker.IsTrackedForTests(oldColumn).Should().BeFalse();
        tracker.IsTrackedForTests(oldConstraint).Should().BeFalse();
        tracker.IsTrackedForTests(oldRelationship).Should().BeFalse();
    }

    /// <summary>取込コマンドの Undo で図へ戻した要素が、二重購読にならないことを検証する</summary>
    /// <remarks>
    /// 一括置換時の明示的な解除（<c>ClearDiagramCollections</c>）と、再追加時の <c>CollectionChanged</c> 経由の
    /// 購読が二重にならないことの確認（購読が 2 本になると 1 回の編集で履歴が 2 つ積まれる）。
    /// </remarks>
    [Fact(DisplayName = "取込 Undo で戻した図の編集は履歴へ 1 回だけ積まれる")]
    public void ImportSchemaUndo_RestoredElementsAreTrackedExactlyOnce()
    {
        var vm = new MainViewModel();
        var existing = new EntityViewModel(new Entity { TableName = "Old" });
        vm.Entities.Add(existing);

        var command = new ImportSchemaCommand(vm, [new Entity { TableName = "New" }], [], "取込");

        command.Execute();
        command.Undo();

        vm.Entities.Should().ContainSingle().Which.Should().BeSameAs(existing);

        var generationBefore = vm.UndoRedo.ChangeGeneration;
        existing.TableName = "Edited";

        vm.UndoRedo.ChangeGeneration.Should()
            .Be(generationBefore + 1, "購読は 1 本だけ＝履歴も 1 つだけ積まれる");
    }
}
