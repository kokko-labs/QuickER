using AwesomeAssertions;
using QuickER.Model;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 図の一括置換が、要素ごとの再計算（列ルール・スナップショット再取得）を終了時の 1 回へまとめることを検証するテストクラス。
/// </summary>
/// <remarks>
/// 回数が要素数に比例すると、大きい図の開く・取込・外部変更の自動再読込の時間がリレーション数×要素数で伸びる。
/// 回数は内部の計数シームで観測し、正しさ（置換後の外部キー列のロックとフラグ）も併せて確かめる。
/// </remarks>
public sealed class MainViewModelBulkDiagramUpdateTests
{
    /// <summary>親 1 つと子 <paramref name="childCount"/> 個、子ごとに親への 1 対多リレーションを持つ図を作る</summary>
    private static ErDiagram BuildStar(int childCount)
    {
        var parent = new Entity { TableName = "Parent" };
        var parentId = new Column
        {
            Name = "Id",
            DataType = "int",
            IsPrimaryKey = true,
        };
        parent.Columns.Add(parentId);

        var diagram = new ErDiagram { TargetDbms = "sqlserver", Entities = { parent } };

        for (var i = 0; i < childCount; i++)
        {
            var child = new Entity { TableName = "Child" + i };
            child.Columns.Add(
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                }
            );
            var foreignKey = new Column { Name = "ParentId", DataType = "int" };
            child.Columns.Add(foreignKey);
            diagram.Entities.Add(child);
            diagram.Relationships.Add(
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = { new RelationshipColumnPair(parentId.Id, foreignKey.Id) },
                }
            );
        }

        return diagram;
    }

    /// <summary>図を置換したときの (列ルールの適用回数, スナップショット再取得回数) の増分を返す</summary>
    private static (int Rules, int Refreshes) CountReplace(int childCount)
    {
        var vm = new MainViewModel();
        var rulesBefore = vm.RelationshipRuleApplicationCountForTests;
        var refreshesBefore = vm.ChangeTrackerForTests.SnapshotRefreshCountForTests;

        vm.ReplaceDiagramFromModule(BuildStar(childCount));

        return (
            vm.RelationshipRuleApplicationCountForTests - rulesBefore,
            vm.ChangeTrackerForTests.SnapshotRefreshCountForTests - refreshesBefore
        );
    }

    /// <summary>置換の再計算回数がリレーション数に依らず一定（列ルールは 1 回）であることを検証する</summary>
    [Fact(
        DisplayName = "図の置換は列ルールを 1 回だけ適用し、再取得回数はリレーション数に依らない"
    )]
    public void ReplaceDiagram_RecomputesOncePerReplacement()
    {
        var small = CountReplace(1);
        var large = CountReplace(50);

        large
            .Rules.Should()
            .Be(1, "リレーションを足すたびではなく、一括置換の終了時に 1 回だけ適用する");
        large
            .Refreshes.Should()
            .Be(small.Refreshes, "全要素の再取得は最外側の終了時だけ＝リレーション数に依らない");
    }

    /// <summary>一括置換で止めていた列ルールが終了時に適用され、外部キー列がロック・フラグ付きになることを検証する</summary>
    [Fact(DisplayName = "図の置換後、外部キー列はロックされ外部キーの印が付く")]
    public void ReplaceDiagram_AppliesRelationshipColumnRulesAtTheEnd()
    {
        var vm = new MainViewModel();
        vm.ReplaceDiagramFromModule(BuildStar(3));

        foreach (
            var child in vm.Entities.Where(e =>
                e.TableName.StartsWith("Child", StringComparison.Ordinal)
            )
        )
        {
            var foreignKey = child.Columns.Single(c => c.Name == "ParentId");
            foreignKey.IsForeignKey.Should().BeTrue();
            foreignKey.IsForeignKeyManagedByRelationship.Should().BeTrue();
            foreignKey.IsForeignKeyEditable.Should().BeFalse();
        }

        vm.Entities.Single(e => e.TableName == "Parent")
            .Columns[0]
            .IsPrimaryKeyEditable.Should()
            .BeFalse();
        vm.UndoRedo.CanUndo.Should().BeFalse("一括置換の後始末（列ルールの適用）は履歴に積まない");
    }

    /// <summary>取込コマンドの Execute / Undo も一括更新を通り、列ルールを 1 回ずつだけ適用することを検証する</summary>
    [Fact(DisplayName = "取込コマンドの実行と取り消しは列ルールを 1 回ずつだけ適用する")]
    public void ImportSchemaCommand_RecomputesOnceForExecuteAndUndo()
    {
        var vm = new MainViewModel();
        vm.ReplaceDiagramFromModule(BuildStar(10));
        var diagram = BuildStar(20);
        var command = new ImportSchemaCommand(vm, diagram.Entities, diagram.Relationships, "取込");

        var before = vm.RelationshipRuleApplicationCountForTests;
        command.Execute();
        (vm.RelationshipRuleApplicationCountForTests - before).Should().Be(1);
        vm.Relationships.Should().HaveCount(20);

        before = vm.RelationshipRuleApplicationCountForTests;
        command.Undo();
        (vm.RelationshipRuleApplicationCountForTests - before).Should().Be(1);
        vm.Relationships.Should().HaveCount(10);
        vm.Relationships.SelectMany(r => r.Target.Columns)
            .Where(c => c.Name == "ParentId")
            .Should()
            .OnlyContain(c => c.IsForeignKey, "戻した図にも列ルールが付け直される");
    }

    /// <summary>入れ子の追跡停止は、最外側の終了時に 1 回だけスナップショットを取り直すことを検証する</summary>
    [Fact(DisplayName = "入れ子の追跡停止はスナップショットを最外側で 1 回だけ取り直す")]
    public void NestedRunWithoutTracking_RefreshesOnlyAtOutermost()
    {
        var vm = new MainViewModel();
        var tracker = vm.ChangeTrackerForTests;
        var before = tracker.SnapshotRefreshCountForTests;

        tracker.RunWithoutTracking(() =>
        {
            tracker.RunWithoutTracking(() => tracker.RunWithoutTracking(() => { }));
            tracker.RunWithoutTracking(() => { });
        });

        (tracker.SnapshotRefreshCountForTests - before).Should().Be(1);
    }
}
