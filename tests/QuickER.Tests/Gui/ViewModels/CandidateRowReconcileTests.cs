using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 列ペアの行（外部キー）と構成列の行（一意制約）が、正本の変更に合わせて選択と候補を入れ替えるときに、
/// コンボボックスへ「候補に無い選択」を当てず、選択中の項目を候補から外さないことを固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// コンボボックスは、選択中の項目が ItemsSource に無いと選択を落とし（<c>null</c> を書き戻し）、
/// それが利用者の編集として正本を変える。実ウィンドウでの見え方は
/// <c>CandidateSelectionOrderWindowTests</c> が見る。ここは VM の段で、
/// 「選択が変わる瞬間にその値が候補にある」「候補から外れる瞬間にそれが選択中でない」の 2 つを
/// 観測点（<see cref="SelectionCandidateProbe"/>）で数える（WPF が無くても順序の誤りを捉えられる）。
/// </para>
/// <para>既知の失敗は台帳（<see cref="Known"/>）に載せる。突き合わせの規則は <see cref="KnownCaseLedger"/>。</para>
/// </remarks>
public class CandidateRowReconcileTests
{
    /// <summary>既知の失敗（ケース ID → 期待する失敗の理由の断片）</summary>
    private static readonly KnownCaseLedger Known = new("CandidateRowReconcileTests", []);

    /// <summary>
    /// 親（Id＝主キー・Code＝一意制約）と子（ChildId＝主キー・ParentId・ParentCode）を、
    /// (Id → ParentId)・(Code → ParentCode) の複合外部キーで結んだ図を読み込む
    /// </summary>
    private static (MainViewModel Vm, RelationshipViewModel Relationship) BuildCompositeForeignKey()
    {
        var id = new Column
        {
            Name = "Id",
            DataType = "int",
            IsPrimaryKey = true,
        };
        var code = new Column { Name = "Code", DataType = "nvarchar(20)" };
        var parentId = new Column { Name = "ParentId", DataType = "int" };
        var parentCode = new Column { Name = "ParentCode", DataType = "nvarchar(20)" };
        var parent = new Entity
        {
            TableName = "Parent",
            Columns = { id, code },
            UniqueConstraints = { new UniqueConstraint { ColumnIds = [code.Id] } },
        };
        var child = new Entity
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
                parentId,
                parentCode,
            },
        };
        var vm = new MainViewModel();
        vm.ReplaceDiagramFromModule(
            new ErDiagram
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
                            new RelationshipColumnPair(id.Id, parentId.Id),
                            new RelationshipColumnPair(code.Id, parentCode.Id),
                        },
                    },
                },
            }
        );

        return (vm, vm.Relationships.Single());
    }

    /// <summary>Item（Id＝主キー・A・B）に UNIQUE(A, B) を持つ図を読み込み、そのテーブルを選ぶ</summary>
    private static (
        MainViewModel Vm,
        EntityViewModel Entity,
        UniqueConstraintViewModel Constraint
    ) BuildUniqueConstraint()
    {
        var a = new Column { Name = "A", DataType = "int" };
        var b = new Column { Name = "B", DataType = "int" };
        var vm = new MainViewModel();
        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                Entities =
                {
                    new Entity
                    {
                        TableName = "Item",
                        Columns =
                        {
                            new Column
                            {
                                Name = "Id",
                                DataType = "int",
                                IsPrimaryKey = true,
                            },
                            a,
                            b,
                        },
                        UniqueConstraints = { new UniqueConstraint { ColumnIds = [a.Id, b.Id] } },
                    },
                },
            }
        );
        var entity = vm.Entities.Single();
        vm.OnEntityClicked(entity);

        return (vm, entity, entity.UniqueConstraints.Single());
    }

    /// <summary>行の名前の並び（外部キー）</summary>
    private static IEnumerable<(string?, string?)> PairNames(RelationshipViewModel relationship) =>
        relationship.ColumnPairRows.Select(row =>
            (row.SelectedSourceColumn?.Name, row.SelectedTargetColumn?.Name)
        );

    /// <summary>正本の列ペアを名前で返す</summary>
    private static IEnumerable<(string, string)> ModelPairNames(
        RelationshipViewModel relationship
    ) =>
        relationship.ColumnPairs.Select(pair =>
            (
                relationship.Source.Columns.Single(column => column.Id == pair.SourceColumnId).Name,
                relationship.Target.Columns.Single(column => column.Id == pair.TargetColumnId).Name
            )
        );

    /// <summary>正本の構成列を名前で返す</summary>
    private static IEnumerable<string> MemberNames(
        EntityViewModel entity,
        UniqueConstraintViewModel constraint
    ) =>
        constraint.ColumnIds.Select(columnId =>
            entity.Columns.Single(column => column.Id == columnId).Name
        );

    [Fact(DisplayName = "外部キーの先頭の行を外すとき、繰り上がる選択は候補に入ってから当てる")]
    public void ForeignKeyRowRemoval_AppliesSelectionWithinCandidates()
    {
        Known.Run(
            "fk-row-remove",
            () =>
            {
                var (vm, relationship) = BuildCompositeForeignKey();
                using var probe = SelectionCandidateProbe.Attach(relationship);

                vm.RemoveRelationshipColumnPairCommand.Execute(relationship.ColumnPairRows[0]);

                ModelPairNames(relationship).Should().Equal(("Code", "ParentCode"));
                PairNames(relationship).Should().Equal(("Code", "ParentCode"));
                probe.Violations.Should().BeEmpty();
            }
        );
    }

    [Fact(
        DisplayName = "外部キーの列ペアの並びを入れ替えるとき、各行の選択は候補に入ってから当てる"
    )]
    public void ForeignKeyReorder_AppliesSelectionWithinCandidates()
    {
        Known.Run(
            "fk-reverse",
            () =>
            {
                var (_, relationship) = BuildCompositeForeignKey();
                using var probe = SelectionCandidateProbe.Attach(relationship);

                relationship.SetColumnPairs(relationship.SnapshotColumnPairs().Reverse().ToList());

                PairNames(relationship).Should().Equal(("Code", "ParentCode"), ("Id", "ParentId"));
                probe.Violations.Should().BeEmpty();
            }
        );
    }

    [Fact(DisplayName = "一意制約の先頭の行を外すとき、繰り上がる選択は候補に入ってから当てる")]
    public void UniqueMemberRemoval_AppliesSelectionWithinCandidates()
    {
        Known.Run(
            "uq-row-remove",
            () =>
            {
                var (vm, entity, constraint) = BuildUniqueConstraint();
                using var probe = SelectionCandidateProbe.Attach(constraint);

                vm.RemoveUniqueConstraintMemberCommand.Execute(constraint.Members[0]);

                MemberNames(entity, constraint).Should().Equal("B");
                constraint
                    .Members.Select(member => member.SelectedColumn?.Name)
                    .Should()
                    .Equal("B");
                probe.Violations.Should().BeEmpty();
            }
        );
    }

    [Fact(
        DisplayName = "列ペアが使う列が直接消えたとき、選択中の項目を候補から外す前にペアを落とす"
    )]
    public void ColumnRemovedOutsideCommand_DropsPairBeforeCandidates()
    {
        Known.Run(
            "fk-column-removed",
            () =>
            {
                var (vm, relationship) = BuildCompositeForeignKey();
                var generation = vm.UndoRedo.ChangeGeneration;
                var edited = 0;
                relationship.ColumnPairSelectionEdited += (_, _) => edited++;
                using var probe = SelectionCandidateProbe.Attach(relationship);

                // コマンドを通らない列の削除（ペアを先に外す処理が無い経路）
                var child = relationship.Target;
                child.Columns.Remove(child.Columns.Single(column => column.Name == "ParentCode"));

                ModelPairNames(relationship).Should().Equal(("Id", "ParentId"));
                probe.Violations.Should().BeEmpty();
                edited.Should().Be(0, "列の消滅による整合は利用者の編集として扱わない");
                vm.UndoRedo.ChangeGeneration.Should().Be(generation);
            }
        );
    }

    [Fact(DisplayName = "外部キーの行を外すとき、外した行の後片付けの書き込みで正本を変えない")]
    public void ForeignKeyRowTeardownWrite_DoesNotEditModel()
    {
        Known.Run(
            "fk-teardown-write",
            () =>
            {
                var (vm, relationship) = BuildCompositeForeignKey();

                // 外した行のコンボボックスが、行リストからの削除の通知の最中に選択を落とす形を再現する
                relationship.ColumnPairRows.CollectionChanged += (_, e) =>
                {
                    if (e.Action == NotifyCollectionChangedAction.Remove)
                    {
                        foreach (RelationshipColumnPairViewModel row in e.OldItems!)
                        {
                            row.SelectedSourceColumn = null;
                        }
                    }
                };
                var generation = vm.UndoRedo.ChangeGeneration;

                vm.RemoveRelationshipColumnPairCommand.Execute(relationship.ColumnPairRows[0]);

                ModelPairNames(relationship)
                    .Should()
                    .Equal(
                        new[] { ("Code", "ParentCode") },
                        "外した行の後片付けの書き込みで正本を変えない"
                    );
                vm.UndoRedo.ChangeGeneration.Should()
                    .Be(generation + 1, "外した行の後片付けの書き込みで履歴を積まない");
            }
        );
    }

    [Fact(DisplayName = "一意制約の行を外すとき、外した行の後片付けの書き込みで正本を変えない")]
    public void UniqueMemberTeardownWrite_DoesNotEditModel()
    {
        Known.Run(
            "uq-teardown-write",
            () =>
            {
                var (vm, entity, constraint) = BuildUniqueConstraint();
                constraint.Members.CollectionChanged += (_, e) =>
                {
                    if (e.Action == NotifyCollectionChangedAction.Remove)
                    {
                        foreach (UniqueConstraintMemberViewModel member in e.OldItems!)
                        {
                            member.SelectedColumn = null;
                        }
                    }
                };
                var generation = vm.UndoRedo.ChangeGeneration;

                vm.RemoveUniqueConstraintMemberCommand.Execute(constraint.Members[0]);

                MemberNames(entity, constraint)
                    .Should()
                    .Equal(new[] { "B" }, "外した行の後片付けの書き込みで正本を変えない");
                vm.UndoRedo.ChangeGeneration.Should()
                    .Be(generation + 1, "外した行の後片付けの書き込みで履歴を積まない");
            }
        );
    }

    [Fact(DisplayName = "図から外れた一意制約の行への書き込みは、Undo で戻る制約を変えない")]
    public void LateWriteToRemovedConstraint_DoesNotCorruptUndo()
    {
        Known.Run(
            "uq-late-write-removed-constraint",
            () =>
            {
                var (vm, entity, constraint) = BuildUniqueConstraint();
                var member = constraint.Members[0];

                vm.RemoveUniqueConstraintCommand.Execute(constraint);
                member.SelectedColumn = null;
                vm.UndoCommand.Execute(null);

                entity
                    .UniqueConstraints.Should()
                    .ContainSingle()
                    .Which.Should()
                    .BeSameAs(constraint);
                MemberNames(entity, constraint)
                    .Should()
                    .Equal(new[] { "A", "B" }, "図から外れた制約の行への書き込みは正本を変えない");
            }
        );
    }

    [Fact(
        DisplayName = "図から外れたリレーションの行への書き込みは、Undo で戻るリレーションを変えない"
    )]
    public void LateWriteToRemovedRelationship_DoesNotCorruptUndo()
    {
        Known.Run(
            "fk-late-write-removed-relationship",
            () =>
            {
                var (vm, relationship) = BuildCompositeForeignKey();
                var row = relationship.ColumnPairRows[0];

                vm.OnRelationshipClicked(relationship);
                vm.RemoveSelectedRelationshipCommand.Execute(null);
                row.SelectedSourceColumn = null;
                vm.UndoCommand.Execute(null);

                vm.Relationships.Should().ContainSingle().Which.Should().BeSameAs(relationship);
                ModelPairNames(relationship)
                    .Should()
                    .Equal(
                        new[] { ("Id", "ParentId"), ("Code", "ParentCode") },
                        "図から外れたリレーションの行への書き込みは正本を変えない"
                    );
            }
        );
    }

    [Fact(
        DisplayName = "図から外れたテーブルの一意制約の行への書き込みは、Undo で戻る制約を変えない"
    )]
    public void LateWriteToConstraintOfRemovedEntity_DoesNotCorruptUndo()
    {
        Known.Run(
            "uq-late-write-removed-entity",
            () =>
            {
                var (vm, entity, constraint) = BuildUniqueConstraint();
                var member = constraint.Members[0];

                vm.RemoveSelectedEntityCommand.Execute(null);
                member.SelectedColumn = null;
                vm.UndoCommand.Execute(null);

                MemberNames(entity, constraint)
                    .Should()
                    .Equal(
                        new[] { "A", "B" },
                        "図から外れたテーブルの制約の行への書き込みは正本を変えない"
                    );
            }
        );
    }

    [Fact(
        DisplayName = "列ペアが無いリレーションを多対多へ切り替えると、片側だけ選んだ空スロットも捨てる"
    )]
    public void SwitchToManyToMany_WithoutPairs_DiscardsPendingSlot()
    {
        Known.Run(
            "fk-many-to-many-pending",
            () =>
            {
                var (vm, relationship) = BuildCompositeForeignKey();
                relationship.SetColumnPairs([]);
                relationship.AddPendingColumnPairSlot();
                relationship.ColumnPairRows.Single().SelectedSourceColumn = relationship
                    .Source
                    .Columns[0];

                relationship.Type = RelationshipType.ManyToMany;

                relationship
                    .ColumnPairRows.Should()
                    .BeEmpty("多対多へ切り替えたら空スロットも捨てる");

                relationship.Type = RelationshipType.OneToMany;

                relationship
                    .ColumnPairRows.Should()
                    .BeEmpty("多対多へ切り替えたら空スロットも捨てる");
                vm.Relationships.Should().ContainSingle();
            }
        );
    }

    [Fact(DisplayName = "選択が落ちた確定済みの構成列の行も、× で正本から外せる")]
    public void CommittedMemberWithNulledSelection_CanBeRemoved()
    {
        Known.Run(
            "uq-committed-row-null",
            () =>
            {
                var (vm, entity, constraint) = BuildUniqueConstraint();

                // コンボボックスが正本の反映中に選択を落とした形（表示だけが空になった確定済みの行）
                constraint.Members[0].ApplySelection(null, isPendingSlot: false);

                vm.RemoveUniqueConstraintMemberCommand.Execute(constraint.Members[0]);

                MemberNames(entity, constraint)
                    .Should()
                    .Equal(new[] { "B" }, "確定済みの行は選択が落ちても × で外せる");
            }
        );
    }

    [Fact(DisplayName = "列ペアの差し替えに自分の一覧をそのまま渡しても、列ペアは消えない")]
    public void SetColumnPairs_WithOwnList_KeepsPairs()
    {
        Known.Run(
            "fk-set-self",
            () =>
            {
                var (_, relationship) = BuildCompositeForeignKey();

                relationship.SetColumnPairs(relationship.ColumnPairs);

                ModelPairNames(relationship)
                    .Should()
                    .Equal(
                        new[] { ("Id", "ParentId"), ("Code", "ParentCode") },
                        "自分の一覧をそのまま渡しても消えない"
                    );
            }
        );
    }

    [Fact(DisplayName = "構成列の差し替えに自分の一覧をそのまま渡しても、構成列は消えない")]
    public void SetColumnIds_WithOwnList_KeepsMembers()
    {
        Known.Run(
            "uq-set-self",
            () =>
            {
                var (_, entity, constraint) = BuildUniqueConstraint();

                constraint.SetColumnIds(constraint.ColumnIds);

                MemberNames(entity, constraint)
                    .Should()
                    .Equal(new[] { "A", "B" }, "自分の一覧をそのまま渡しても消えない");
            }
        );
    }
}

/// <summary>
/// 行の選択と候補の入れ替えの順序を観測する。選択が変わる瞬間にその値が候補にあること、
/// 候補から外れる瞬間にそれが選択中でないことを、外部キーの行・一意制約の行の両方で数える
/// </summary>
/// <remarks>後から増えた行にも付く（行リストの増加を購読する）</remarks>
internal sealed class SelectionCandidateProbe : IDisposable
{
    /// <summary>見つけた違反</summary>
    public List<string> Violations { get; } = new();

    /// <summary>購読を外す処理</summary>
    private readonly List<Action> _detach = new();

    /// <summary>外部キーの行（親列・子列）を観測する</summary>
    public static SelectionCandidateProbe Attach(RelationshipViewModel relationship)
    {
        var probe = new SelectionCandidateProbe();

        foreach (var row in relationship.ColumnPairRows)
        {
            probe.WatchPairRow(row);
        }

        probe.WatchAdditions(relationship.ColumnPairRows, row => probe.WatchPairRow(row));

        return probe;
    }

    /// <summary>一意制約の構成列の行を観測する</summary>
    public static SelectionCandidateProbe Attach(UniqueConstraintViewModel constraint)
    {
        var probe = new SelectionCandidateProbe();

        foreach (var member in constraint.Members)
        {
            probe.WatchMember(member);
        }

        probe.WatchAdditions(constraint.Members, member => probe.WatchMember(member));

        return probe;
    }

    private void WatchAdditions<T>(ObservableCollection<T> rows, Action<T> watch)
    {
        NotifyCollectionChangedEventHandler handler = (_, e) =>
        {
            if (e.NewItems is null)
            {
                return;
            }

            foreach (T row in e.NewItems)
            {
                watch(row);
            }
        };
        rows.CollectionChanged += handler;
        _detach.Add(() => rows.CollectionChanged -= handler);
    }

    private void WatchPairRow(RelationshipColumnPairViewModel row)
    {
        Watch(
            row,
            nameof(RelationshipColumnPairViewModel.SelectedSourceColumn),
            () => row.SelectedSourceColumn,
            row.AvailableSourceColumns,
            "親列"
        );
        Watch(
            row,
            nameof(RelationshipColumnPairViewModel.SelectedTargetColumn),
            () => row.SelectedTargetColumn,
            row.AvailableTargetColumns,
            "子列"
        );
    }

    private void WatchMember(UniqueConstraintMemberViewModel member) =>
        Watch(
            member,
            nameof(UniqueConstraintMemberViewModel.SelectedColumn),
            () => member.SelectedColumn,
            member.AvailableColumns,
            "構成列"
        );

    private void Watch(
        INotifyPropertyChanged row,
        string propertyName,
        Func<ColumnViewModel?> selection,
        ObservableCollection<ColumnViewModel> candidates,
        string side
    )
    {
        PropertyChangedEventHandler onSelection = (_, e) =>
        {
            if (e.PropertyName != propertyName)
            {
                return;
            }

            var selected = selection();

            if (selected is not null && !candidates.Contains(selected))
            {
                Violations.Add($"{side}: 候補に無い選択 {selected.Name} を当てた");
            }
        };
        NotifyCollectionChangedEventHandler onCandidates = (_, e) =>
        {
            if (
                e.Action
                    is NotifyCollectionChangedAction.Remove
                        or NotifyCollectionChangedAction.Replace
                && e.OldItems is not null
            )
            {
                var selected = selection();

                foreach (ColumnViewModel removed in e.OldItems)
                {
                    if (ReferenceEquals(removed, selected))
                    {
                        Violations.Add($"{side}: 選択中の項目を候補から外した {removed.Name}");
                    }
                }
            }
        };
        row.PropertyChanged += onSelection;
        candidates.CollectionChanged += onCandidates;
        _detach.Add(() =>
        {
            row.PropertyChanged -= onSelection;
            candidates.CollectionChanged -= onCandidates;
        });
    }

    public void Dispose()
    {
        foreach (var detach in _detach)
        {
            detach();
        }
    }
}
