using System.IO;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>
/// 要素を削除するコマンドが、Undo で元の位置へ戻す（一覧の並びが削除の前後で変わらない）ことを、
/// 削除コマンドの全数で表明するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 図のテーブル・リレーション・列・一意制約の並びは、保存内容・DDL・制約の既定名に現れる。名前を持たない外部キーの既定名は
/// 並びの先頭から割り当てるため、リレーションが末尾へ戻ると 2 本の既定名が入れ替わり、次の DDL や同期が実在する
/// データベースに対して偽の drop と add を出す。
/// </para>
/// <para>
/// 位置の記録と復元は <c>IndexedRemoval</c> に 1 本化している。削除コマンドが手書きで位置を扱うと、
/// 一覧が変わったときの頭打ちの規則が写しごとにずれるため、ソース走査でも固定する。
/// </para>
/// </remarks>
public class RemovalOrderRoundTripTests
{
    /// <summary>同じ親子を結ぶ名前のないリレーション 2 本と、一意制約 2 本、列 4 本を持つ図を読み込む</summary>
    private static MainViewModel Build()
    {
        var parentId = new Column
        {
            Name = "Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var parentCode = new Column
        {
            Name = "Code",
            DataType = "nvarchar(10)",
            IsNullable = false,
        };
        var parentName = new Column
        {
            Name = "Name",
            DataType = "nvarchar(50)",
            IsNullable = true,
        };
        var parentNote = new Column
        {
            Name = "Note",
            DataType = "nvarchar(50)",
            IsNullable = true,
        };
        var parent = new Entity { TableName = "Parent" };
        parent.Columns.Add(parentId);
        parent.Columns.Add(parentCode);
        parent.Columns.Add(parentName);
        parent.Columns.Add(parentNote);
        parent.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_Parent_Code", ColumnIds = { parentCode.Id } }
        );
        parent.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_Parent_Name", ColumnIds = { parentName.Id } }
        );

        var childId = new Column
        {
            Name = "Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var childMain = new Column
        {
            Name = "MainParentId",
            DataType = "int",
            IsNullable = true,
        };
        var childSub = new Column
        {
            Name = "SubParentId",
            DataType = "int",
            IsNullable = true,
        };
        var child = new Entity { TableName = "Child" };
        child.Columns.Add(childId);
        child.Columns.Add(childMain);
        child.Columns.Add(childSub);

        Relationship Link(Column childColumn) =>
            new()
            {
                SourceEntityId = parent.Id,
                TargetEntityId = child.Id,
                Type = RelationshipType.OneToMany,
                ConstraintName = null,
                ColumnPairs =
                {
                    new RelationshipColumnPair
                    {
                        SourceColumnId = parentId.Id,
                        TargetColumnId = childColumn.Id,
                    },
                },
            };

        var vm = new MainViewModel();
        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                Entities = { parent, child },
                Relationships = { Link(childMain), Link(childSub) },
            }
        );

        return vm;
    }

    /// <summary>並びを含む図全体の状態を文字列にする（Id の並びで比べる）</summary>
    private static string Order(MainViewModel vm) =>
        string.Join(
            "|",
            vm.Entities.Select(e =>
                $"{e.Id}:[{string.Join(",", e.Columns.Select(c => c.Id))}]"
                + $"[{string.Join(",", e.UniqueConstraints.Select(u => u.Id))}]"
            )
        )
        + "#"
        + string.Join(",", vm.Relationships.Select(r => r.Id));

    private static void AssertRestoresOrder(MainViewModel vm, Action removal)
    {
        var before = Order(vm);

        removal();
        Order(vm).Should().NotBe(before, "削除で並びが変わる前提");

        vm.UndoCommand.Execute(null);

        Order(vm).Should().Be(before, "Undo で削除の前と同じ位置・同じ並びへ戻る");

        vm.RedoCommand.Execute(null);
        vm.UndoCommand.Execute(null);

        Order(vm).Should().Be(before, "2 回目の Undo でも同じ");
    }

    [Fact(
        DisplayName = "リレーションの削除を Undo すると、元の位置へ戻る（名前のない外部キーの既定名が入れ替わらない）"
    )]
    public void RemoveRelationship_Undo_RestoresPosition() =>
        UndoKnownFailures.Run(
            nameof(RemoveRelationship_Undo_RestoresPosition),
            () =>
            {
                var vm = Build();
                vm.SelectedRelationship = vm.Relationships[0];

                AssertRestoresOrder(vm, () => vm.RemoveSelectedRelationshipCommand.Execute(null));
            }
        );

    [Fact(DisplayName = "一意制約の削除を Undo すると、元の位置へ戻る")]
    public void RemoveUniqueConstraint_Undo_RestoresPosition() =>
        UndoKnownFailures.Run(
            nameof(RemoveUniqueConstraint_Undo_RestoresPosition),
            () =>
            {
                var vm = Build();
                vm.SelectedEntity = vm.Entities[0];

                AssertRestoresOrder(
                    vm,
                    () =>
                        vm.RemoveUniqueConstraintCommand.Execute(
                            vm.SelectedEntity.UniqueConstraints[0]
                        )
                );
            }
        );

    [Fact(
        DisplayName = "列の削除（その列を含む一意制約の連動削除を含む）を Undo すると、列も制約も元の位置へ戻る"
    )]
    public void RemoveColumn_Undo_RestoresColumnAndConstraintPositions() =>
        UndoKnownFailures.Run(
            nameof(RemoveColumn_Undo_RestoresColumnAndConstraintPositions),
            () =>
            {
                var vm = Build();
                vm.SelectedEntity = vm.Entities[0];
                var code = vm.SelectedEntity.Columns.Single(c => c.Name == "Code");

                AssertRestoresOrder(vm, () => vm.RemoveColumnCommand.Execute(code));
            }
        );

    [Fact(
        DisplayName = "テーブルの削除（接続するリレーションの連動削除を含む）を Undo すると、テーブルもリレーションも元の位置へ戻る"
    )]
    public void RemoveEntity_Undo_RestoresEntityAndRelationshipPositions() =>
        UndoKnownFailures.Run(
            nameof(RemoveEntity_Undo_RestoresEntityAndRelationshipPositions),
            () =>
            {
                var vm = Build();
                vm.SelectedEntity = vm.Entities[0];

                AssertRestoresOrder(vm, () => vm.RemoveSelectedEntityCommand.Execute(null));
            }
        );

    [Fact(
        DisplayName = "選択した複数のテーブルの削除を Undo すると、テーブルもリレーションも元の位置へ戻る"
    )]
    public void GroupRemoveEntities_Undo_RestoresPositions() =>
        UndoKnownFailures.Run(
            nameof(GroupRemoveEntities_Undo_RestoresPositions),
            () =>
            {
                var vm = Build();
                vm.SelectAllEntitiesCommand.Execute(null);

                AssertRestoresOrder(vm, () => vm.DeleteSelectedCommand.Execute(null));
            }
        );

    [Fact(DisplayName = "要素を取り除く削除コマンドは、位置の記録と復元を IndexedRemoval に任せる")]
    public void RemoveCommands_UseIndexedRemoval() =>
        UndoKnownFailures.Run(
            nameof(RemoveCommands_UseIndexedRemoval),
            () =>
            {
                var directory = Path.Combine(RepoRoot(), "src", "QuickER.Gui", "UndoRedo");
                var offenders = Directory
                    .EnumerateFiles(directory, "*Remove*Command.cs")
                    .Where(path =>
                    {
                        var source = File.ReadAllText(path);

                        // 取り除いた要素を Undo で戻す削除コマンドが対象（Execute が Remove を呼ぶもの）
                        return Regex.IsMatch(source, @"\.Remove(At)?\(")
                            && !source.Contains("IndexedRemoval.RemoveWhere");
                    })
                    .Select(Path.GetFileName)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();

                offenders
                    .Should()
                    .BeEmpty(
                        "位置の記録と復元の写しを削除コマンドごとに持たない（IndexedRemoval.RemoveWhere / Restore を使う）"
                    );
            }
        );

    /// <summary>リポジトリ直下（QuickER.slnx のある場所）を、テストアセンブリの位置から遡って解決する</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "QuickER.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("リポジトリ直下（QuickER.slnx）が見つかりません。");
    }
}
