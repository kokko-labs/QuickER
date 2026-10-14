using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using AwesomeAssertions;
using QuickER.Behaviors;
using QuickER.Model;
using QuickER.Tests.TestSupport;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 図全体に掛かる再計算（ミニマップの作り直し・キャンバスサイズの通知・列ルールの適用・候補や標識の作り直し）が、
/// 1 回の操作で 1 回にまとまり、テーブル数に比例して増えないことを、回数で固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 位置や幅の変更・要素の出入りは 1 件ごとに通知されるため、まとめずに受けると 1 回の操作でテーブル数ぶん
/// 作り直しが走る（500 テーブルの整列 1 回の Undo でミニマップを 1,000 回作り直していた）。遅さは時間では
/// 測らず（機械の速さと負荷に左右される）、作り直しの回数を内部の数え上げで数える。テーブル数を変えて回数が
/// 変わらないことも見る。
/// </para>
/// <para>
/// 既知の失敗は台帳（<see cref="Known"/>）に載せる。突き合わせの規則は <see cref="KnownFailureLedger"/>。
/// </para>
/// </remarks>
public class LayoutRefreshCountTests
{
    /// <summary>既知の失敗（ケース ID → 期待する失敗の理由の断片）</summary>
    private static readonly KnownFailureLedger Known = new(new Dictionary<string, string>());

    /// <summary>テーブル数（親子の鎖と余分なリレーションを持つ）の図を読み込む</summary>
    private static MainViewModel Build(int tables)
    {
        var entities = new List<Entity>();

        for (var i = 0; i < tables; i++)
        {
            var entity = new Entity { TableName = "T" + i };
            entity.Columns.Add(
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                }
            );
            entity.Columns.Add(
                new Column
                {
                    Name = "ParentId",
                    DataType = "int",
                    IsNullable = true,
                }
            );
            entity.Columns.Add(
                new Column
                {
                    Name = "Name",
                    DataType = "nvarchar(50)",
                    IsNullable = true,
                }
            );
            entities.Add(entity);
        }

        var diagram = new ErDiagram();
        diagram.Entities.AddRange(entities);

        for (var i = 1; i < tables; i++)
        {
            var parent = entities[(i - 1) / 2];
            var child = entities[i];
            diagram.Relationships.Add(
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs =
                    {
                        new RelationshipColumnPair
                        {
                            SourceColumnId = parent.Columns[0].Id,
                            TargetColumnId = child.Columns[1].Id,
                        },
                    },
                }
            );
        }

        var vm = new MainViewModel();
        vm.ReplaceDiagramFromModule(diagram);

        return vm;
    }

    /// <summary>位置と幅をばらして、整列・幅の自動調整が必ず位置・幅を変える状態にする</summary>
    private static void Scramble(MainViewModel vm)
    {
        for (var i = 0; i < vm.Entities.Count; i++)
        {
            vm.Entities[i].X = 37 * ((i * 7) % vm.Entities.Count) + 5;
            vm.Entities[i].Y = 29 * ((i * 11) % vm.Entities.Count) + 3;
            vm.Entities[i].Width = 333;
        }
    }

    /// <summary>1 回の操作で増えた回数（ミニマップ・キャンバスサイズ・列ルール）</summary>
    private sealed record Counts(int MiniMap, int Canvas, int Rules);

    private static Counts Measure(MainViewModel vm, Action operation)
    {
        var miniMap = vm.MiniMapRecalculationCountForTests;
        var canvas = vm.CanvasSizeRefreshCountForTests;
        var rules = vm.RelationshipRuleApplicationCountForTests;

        operation();

        return new Counts(
            vm.MiniMapRecalculationCountForTests - miniMap,
            vm.CanvasSizeRefreshCountForTests - canvas,
            vm.RelationshipRuleApplicationCountForTests - rules
        );
    }

    /// <summary>操作の名前 → (準備, 操作, 列ルールも 1 回か)</summary>
    private static readonly IReadOnlyDictionary<
        string,
        (Action<MainViewModel> Prepare, Action<MainViewModel> Operate, bool CountsRules)
    > Operations = new Dictionary<string, (Action<MainViewModel>, Action<MainViewModel>, bool)>
    {
        ["整列（格子）"] = (Scramble, vm => vm.AutoLayoutGridCommand.Execute(null), false),
        ["整列（木）"] = (Scramble, vm => vm.AutoLayoutTreeCommand.Execute(null), false),
        ["整列（力学）"] = (Scramble, vm => vm.AutoLayoutForceCommand.Execute(null), false),
        ["整列の Undo"] = (
            vm =>
            {
                Scramble(vm);
                vm.AutoLayoutForceCommand.Execute(null);
            },
            vm => vm.UndoCommand.Execute(null),
            false
        ),
        ["整列の Redo"] = (
            vm =>
            {
                Scramble(vm);
                vm.AutoLayoutForceCommand.Execute(null);
                vm.UndoCommand.Execute(null);
            },
            vm => vm.RedoCommand.Execute(null),
            false
        ),
        ["幅の自動調整"] = (Scramble, vm => vm.AutoFitEntityWidthsCommand.Execute(null), false),
        ["グループのドラッグ 1 回の移動"] = (
            vm =>
            {
                vm.SelectAllEntitiesCommand.Execute(null);
                DragBehavior.BeginInteractionForTests(
                    vm.Entities[0],
                    vm.Entities[0].X,
                    vm.Entities[0].Y,
                    groupMembers: vm.Entities.Select(e => (e, e.X, e.Y)).ToList(),
                    owner: vm
                );
            },
            vm => DragBehavior.ApplyMove(new Point(30, 40)),
            false
        ),
        ["グループのドラッグの取り消し（元の位置へ戻す）"] = (
            vm =>
            {
                vm.SelectAllEntitiesCommand.Execute(null);
                DragBehavior.BeginInteractionForTests(
                    vm.Entities[0],
                    vm.Entities[0].X,
                    vm.Entities[0].Y,
                    groupMembers: vm.Entities.Select(e => (e, e.X, e.Y)).ToList(),
                    owner: vm
                );
                DragBehavior.ApplyMove(new Point(30, 40));
            },
            vm => DragBehavior.CancelActiveDrag(),
            false
        ),
        ["複数テーブルの削除"] = (
            vm => vm.SelectAllEntitiesCommand.Execute(null),
            vm => vm.DeleteSelectedCommand.Execute(null),
            true
        ),
        ["複数テーブルの削除の Undo"] = (
            vm =>
            {
                vm.SelectAllEntitiesCommand.Execute(null);
                vm.DeleteSelectedCommand.Execute(null);
            },
            vm => vm.UndoCommand.Execute(null),
            true
        ),
        ["複数テーブルの削除の Redo"] = (
            vm =>
            {
                vm.SelectAllEntitiesCommand.Execute(null);
                vm.DeleteSelectedCommand.Execute(null);
                vm.UndoCommand.Execute(null);
            },
            vm => vm.RedoCommand.Execute(null),
            true
        ),
    };

    public static TheoryData<string> OperationNames()
    {
        var data = new TheoryData<string>();

        foreach (var name in Operations.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    [Theory(
        DisplayName = "1 回の操作で、ミニマップの作り直しとキャンバスサイズの通知は 1 回、テーブル数に依らない"
    )]
    [MemberData(nameof(OperationNames))]
    public void Operation_RefreshesOnce_RegardlessOfTableCount(string operation) =>
        Known.Run(
            operation,
            () =>
            {
                var (prepare, operate, countsRules) = Operations[operation];

                foreach (var tables in new[] { 20, 40 })
                {
                    var vm = Build(tables);
                    Counts counts;

                    try
                    {
                        prepare(vm);
                        counts = Measure(vm, () => operate(vm));
                    }
                    finally
                    {
                        // ドラッグを始めた操作の後始末（進行中でなければ何もしない）
                        DragBehavior.CancelActiveDrag();
                    }

                    counts
                        .MiniMap.Should()
                        .Be(
                            1,
                            "{0} テーブルの {1} でミニマップの作り直しは 1 回",
                            tables,
                            operation
                        );
                    counts
                        .Canvas.Should()
                        .Be(
                            1,
                            "{0} テーブルの {1} でキャンバスサイズの通知は 1 回",
                            tables,
                            operation
                        );

                    if (countsRules)
                    {
                        counts
                            .Rules.Should()
                            .Be(1, "{0} テーブルの {1} で列ルールの適用は 1 回", tables, operation);
                    }
                }
            }
        );

    [Fact(
        DisplayName = "位置・大きさの後回しの範囲は、中で例外が起きても閉じ、以後の移動は直ちに作り直される"
    )]
    public void LayoutRefreshScope_ClosesEvenWhenActionThrows() =>
        Known.Run(
            nameof(LayoutRefreshScope_ClosesEvenWhenActionThrows),
            () =>
            {
                var vm = Build(4);

                var thrown = () =>
                    vm.RunDeferringLayoutRefresh(() =>
                    {
                        vm.Entities[0].X += 10;
                        throw new InvalidOperationException("中で失敗");
                    });

                thrown.Should().Throw<InvalidOperationException>();

                var counts = Measure(vm, () => vm.Entities[1].X += 10);

                counts
                    .MiniMap.Should()
                    .Be(1, "範囲は閉じていて、範囲の外の移動は直ちに作り直される");
                counts.Canvas.Should().Be(1);
            }
        );

    [Fact(
        DisplayName = "後処理で列ルールを直接呼ぶコマンドの Undo / Redo でも、列ルールの適用は 1 回ずつ（一括更新の終わりに任せる）"
    )]
    public void CommandsCallingRulesDirectly_ApplyRulesOnceOnUndoRedo() =>
        Known.Run(
            nameof(CommandsCallingRulesDirectly_ApplyRulesOnceOnUndoRedo),
            () =>
            {
                var vm = Build(6);
                var problems = new List<string>();

                void UndoRedo(string step, Action operation)
                {
                    operation();

                    foreach (
                        var (name, action) in new (string, Action)[]
                        {
                            ("Undo", () => vm.UndoCommand.Execute(null)),
                            ("Redo", () => vm.RedoCommand.Execute(null)),
                        }
                    )
                    {
                        var rules = Measure(vm, action).Rules;

                        if (rules != 1)
                        {
                            problems.Add($"{step} の {name}: 列ルールの適用 {rules} 回");
                        }
                    }
                }

                // 列の削除（RemoveColumnCommand の後処理が列ルールを直接呼ぶ）
                UndoRedo(
                    "列の削除",
                    () =>
                    {
                        vm.SelectedEntity = vm.Entities[1];
                        vm.RemoveColumnCommand.Execute(vm.Entities[1].Columns[1]);
                    }
                );

                // 主キーの切り替え（連動を畳んだ SnapshotChangeCommand の後処理が列ルールを直接呼ぶ）
                UndoRedo("主キーの切り替え", () => vm.Entities[2].Columns[2].IsPrimaryKey = true);

                problems
                    .Should()
                    .BeEmpty("一括更新の中で直接呼ばれた列ルールは、終わりの 1 回に畳まれる");
            }
        );

    [Fact(DisplayName = "リレーションの追加・削除とその Undo / Redo で、列ルールの適用は 1 回ずつ")]
    public void RelationshipAddAndRemove_ApplyRulesOnce() =>
        Known.Run(
            nameof(RelationshipAddAndRemove_ApplyRulesOnce),
            () =>
            {
                var vm = Build(6);
                var problems = new List<string>();

                void Expect(string step, Action action)
                {
                    var rules = Measure(vm, action).Rules;

                    if (rules != 1)
                    {
                        problems.Add($"{step}: 列ルールの適用 {rules} 回");
                    }
                }

                Expect(
                    "追加",
                    () =>
                    {
                        vm.StartAddOneToManyCommand.Execute(null);
                        vm.OnEntityClicked(vm.Entities[5]);
                        vm.OnEntityClicked(vm.Entities[4]);
                    }
                );
                Expect("追加の Undo", () => vm.UndoCommand.Execute(null));
                Expect("追加の Redo", () => vm.RedoCommand.Execute(null));

                vm.SelectedRelationship = vm.Relationships[0];
                Expect("削除", () => vm.RemoveSelectedRelationshipCommand.Execute(null));
                Expect("削除の Undo", () => vm.UndoCommand.Execute(null));

                problems.Should().BeEmpty("列ルールの適用は 1 回の操作で 1 回");
            }
        );

    [Fact(
        DisplayName = "NULL 許容の表示の切り替えは、テーブルの高さを捨てず、ミニマップを作り直さない"
    )]
    public void ToggleNullabilityDisplay_DoesNotInvalidateHeights() =>
        Known.Run(
            nameof(ToggleNullabilityDisplay_DoesNotInvalidateHeights),
            () =>
            {
                var vm = Build(10);
                var heightNotifications = 0;

                foreach (var entity in vm.Entities)
                {
                    entity.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(EntityViewModel.DisplayHeight))
                        {
                            heightNotifications++;
                        }
                    };
                }

                var counts = Measure(
                    vm,
                    () => vm.ShowNullabilityInDiagram = !vm.ShowNullabilityInDiagram
                );

                heightNotifications.Should().Be(0, "NULL 許容の表示は高さに関係しない");
                counts.MiniMap.Should().Be(0);
            }
        );

    [Fact(DisplayName = "列名の変更で、つながるリレーションの列ペアの候補を作り直さない")]
    public void ColumnRename_DoesNotRebuildColumnPairCandidates() =>
        Known.Run(
            nameof(ColumnRename_DoesNotRebuildColumnPairCandidates),
            () =>
            {
                var vm = Build(6);
                var parent = vm.Entities[0];
                var attached = vm
                    .Relationships.Where(r =>
                        ReferenceEquals(r.Source, parent) || ReferenceEquals(r.Target, parent)
                    )
                    .ToList();
                attached.Should().NotBeEmpty();
                var before = attached.Sum(r => r.ColumnPairCandidateRefreshCountForTests);

                parent.Columns[0].Name = "Key";

                attached
                    .Sum(r => r.ColumnPairCandidateRefreshCountForTests)
                    .Should()
                    .Be(before, "候補は列そのものの集まりで、名前は表示の束縛が追う");
            }
        );

    [Fact(
        DisplayName = "列の追加 1 回で、一意制約の構成列フラグの計算は 1 回（制約の数に依らない）"
    )]
    public void ColumnAdd_RefreshesUniqueConstraintFlagsOnce() =>
        Known.Run(
            nameof(ColumnAdd_RefreshesUniqueConstraintFlagsOnce),
            () =>
            {
                var model = new Entity { TableName = "Items" };

                for (var i = 0; i < 4; i++)
                {
                    model.Columns.Add(new Column { Name = "C" + i, DataType = "int" });
                    model.UniqueConstraints.Add(
                        new UniqueConstraint { ColumnIds = { model.Columns[i].Id } }
                    );
                }

                var entity = new EntityViewModel(model);
                var before = entity.UniqueConstraintFlagRefreshCountForTests;

                entity.Columns.Add(
                    new ColumnViewModel(new Column { Name = "Extra", DataType = "int" })
                );

                (entity.UniqueConstraintFlagRefreshCountForTests - before)
                    .Should()
                    .Be(1, "構成列は変わらないので、制約ごとに計算し直さない");
            }
        );

    [Fact(
        DisplayName = "表示される標識が変わらない一意制約の構成列の変化では、標識の変更を通知しない"
    )]
    public void UniqueMembershipOnPrimaryKey_DoesNotNotifyKeyMark() =>
        Known.Run(
            nameof(UniqueMembershipOnPrimaryKey_DoesNotNotifyKeyMark),
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
                var keyMarkNotifications = 0;
                column.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ColumnViewModel.KeyMark))
                    {
                        keyMarkNotifications++;
                    }
                };

                column.IsUniqueConstraintMember = true;

                keyMarkNotifications.Should().Be(0, "主キーの標識が優先され、表示は変わらない");
            }
        );

    [Fact(DisplayName = "追跡しないプロパティの変更通知で、変更追跡は割り当てをしない")]
    public void TrackerHotPath_DoesNotAllocateForUntrackedProperties() =>
        Known.Run(
            nameof(TrackerHotPath_DoesNotAllocateForUntrackedProperties),
            () =>
            {
                var tracked = new EntityViewModel(new Entity { TableName = "A" });
                var untracked = new EntityViewModel(new Entity { TableName = "B" });
                var tracker = new DiagramChangeTracker(
                    new UndoRedoManager(),
                    new ObservableCollection<EntityViewModel>(),
                    new ObservableCollection<RelationshipViewModel>(),
                    () => { },
                    (_, _, _) => { }
                );
                tracker.AttachEntity(tracked);

                static long Allocated(EntityViewModel entity)
                {
                    // 一度回して JIT とキャッシュの割り当てを済ませてから測る
                    for (var i = 0; i < 10; i++)
                    {
                        entity.IsDimmed = !entity.IsDimmed;
                    }

                    var before = GC.GetAllocatedBytesForCurrentThread();

                    for (var i = 0; i < 1000; i++)
                    {
                        entity.IsDimmed = !entity.IsDimmed;
                    }

                    return GC.GetAllocatedBytesForCurrentThread() - before;
                }

                var withTracker = Allocated(tracked);
                var withoutTracker = Allocated(untracked);

                (withTracker - withoutTracker)
                    .Should()
                    .BeLessThan(
                        1000,
                        "追跡しないプロパティでは、1,000 回の通知で割り当てがほぼ増えない"
                    );
            }
        );
}
