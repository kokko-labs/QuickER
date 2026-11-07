using System.Text.Json;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

internal static partial class UndoOperationRows
{
    /// <summary>
    /// 内蔵チャットのツール（<see cref="ErDiagramDynamicTools"/>）の入口：1 回の呼び出しが宣言どおりの履歴を積み、Undo / Redo で図が並びまで戻る
    /// </summary>
    /// <remarks>
    /// <para>
    /// どのツールがどのコマンドを積むかは <c>ErDiagramDynamicTools.cs</c> の各メソッドの <c>vm.UndoRedo.Execute(new …Command(…))</c>
    /// （add_entity・remove_entity・add_column・remove_column・add_relationship・remove_relationship・set_unique_constraint・
    /// remove_unique_constraint）、<c>vm.ApplyPrimaryKey</c>（set_primary_key＝<c>MainViewModel.cs</c> の <c>SetPrimaryKeyCommand</c>）と、
    /// 追跡対象のプロパティの代入（set_entity_property・set_column_property＝<c>DiagramChangeTracker</c> の <c>PropertyChangeCommand</c>）から読む。
    /// 結果の文言は 2 ホストの表（<c>ToolScenarioTests</c>）が見るので、ここは成否と履歴だけを見る。
    /// </para>
    /// <para>
    /// <see cref="SnapshotChangeCommand"/> はツールからは積まれない（積むのは主キーの切り替えと関係の種別の変更で、
    /// set_primary_key は <c>SetPrimaryKeyCommand</c> が追跡を止めて適用し、種別を変えるツールは無い）ので、この入口の行は無い。
    /// </para>
    /// </remarks>
    private static IEnumerable<UndoRow> ToolRows()
    {
        yield return Row(
                "tool-add-entity-pushes-add-entity-command",
                "ツール add_entity は AddEntityCommand を 1 件積み、Undo / Redo で表が戻る"
            )
            .Diagram(ToolDiagram)
            .Operate(
                UndoEntry.Tool,
                "add_entity（Added）",
                vm => CallTool(vm, "add_entity", new { table_name = "Added" })
            )
            .Commands(typeof(AddEntityCommand))
            .Check(
                UndoStage.Operated,
                "表が増える",
                vm => TableOrder(vm).Should().Equal("Parent", "Child", "Added")
            )
            .Check(
                UndoStage.Undone,
                "表が元の並びへ戻る",
                vm => TableOrder(vm).Should().Equal("Parent", "Child")
            );

        yield return Row(
                "tool-remove-entity-pushes-remove-entity-command",
                "ツール remove_entity は RemoveEntityCommand を 1 件積み、Undo で表と、その表を結ぶリレーションが元の位置へ戻る"
            )
            .Diagram(ToolDiagram)
            .Operate(
                UndoEntry.Tool,
                "remove_entity（Child）",
                vm => CallTool(vm, "remove_entity", new { table_name = "Child" })
            )
            .Commands(typeof(RemoveEntityCommand))
            .Check(
                UndoStage.Operated,
                "表とリレーションが消える",
                vm =>
                {
                    TableOrder(vm).Should().Equal("Parent");
                    vm.Relationships.Should().BeEmpty();
                }
            )
            .Check(
                UndoStage.Undone,
                "表とリレーションが戻る",
                vm =>
                {
                    TableOrder(vm).Should().Equal("Parent", "Child");
                    vm.Relationships.Should().ContainSingle();
                }
            );

        yield return Row(
                "tool-add-column-pushes-add-column-command",
                "ツール add_column は AddColumnCommand を 1 件積み、Undo / Redo で列が戻る"
            )
            .Diagram(ToolDiagram)
            .Operate(
                UndoEntry.Tool,
                "add_column（Parent.Extra）",
                vm =>
                    CallTool(
                        vm,
                        "add_column",
                        new
                        {
                            table_name = "Parent",
                            column_name = "Extra",
                            data_type = "int",
                        }
                    )
            )
            .Commands(typeof(AddColumnCommand))
            .Check(
                UndoStage.Operated,
                "列が末尾へ増える",
                vm =>
                    ColumnNames(vm, "Parent").Should().Equal("Id", "Code", "Name", "Note", "Extra")
            )
            .Check(
                UndoStage.Undone,
                "列が元の並びへ戻る",
                vm => ColumnNames(vm, "Parent").Should().Equal("Id", "Code", "Name", "Note")
            );

        yield return Row(
                "tool-remove-column-pushes-remove-column-command",
                "ツール remove_column は RemoveColumnCommand を 1 件積み、Undo で列が元の位置へ戻る"
            )
            .Diagram(ToolDiagram)
            .Operate(
                UndoEntry.Tool,
                "remove_column（Parent.Name＝一意制約の構成列）",
                vm =>
                    CallTool(
                        vm,
                        "remove_column",
                        new { table_name = "Parent", column_name = "Name" }
                    )
            )
            .Commands(typeof(RemoveColumnCommand))
            .Check(
                UndoStage.Operated,
                "列が消える",
                vm => ColumnNames(vm, "Parent").Should().Equal("Id", "Code", "Note")
            )
            .Check(
                UndoStage.Undone,
                "列が元の位置へ戻り、一意制約の構成列も戻る",
                vm =>
                {
                    ColumnNames(vm, "Parent").Should().Equal("Id", "Code", "Name", "Note");
                    TableNamed(vm, "Parent")
                        .UniqueConstraints.Single()
                        .ColumnIds.Should()
                        .HaveCount(2);
                }
            );

        yield return Row(
                "tool-set-entity-property-pushes-property-change-command",
                "ツール set_entity_property の改名は追跡が PropertyChangeCommand を 1 件積み、Undo / Redo で表名が戻る"
            )
            .Diagram(ToolDiagram)
            .Operate(
                UndoEntry.Tool,
                "set_entity_property（Parent → Customer）",
                vm =>
                    CallTool(
                        vm,
                        "set_entity_property",
                        new { table_name = "Parent", new_table_name = "Customer" }
                    )
            )
            .Commands(typeof(PropertyChangeCommand))
            .Check(
                UndoStage.Operated,
                "表名が変わる",
                vm => TableOrder(vm).Should().Equal("Customer", "Child")
            )
            .Check(
                UndoStage.Undone,
                "表名が戻る",
                vm => TableOrder(vm).Should().Equal("Parent", "Child")
            );

        yield return Row(
                "tool-set-column-property-pushes-property-change-command",
                "ツール set_column_property の型の変更は追跡が PropertyChangeCommand を 1 件積み、Undo / Redo で型が戻る"
            )
            .Diagram(ToolDiagram)
            .Operate(
                UndoEntry.Tool,
                "set_column_property（Parent.Note の型を nvarchar(100) へ）",
                vm =>
                    CallTool(
                        vm,
                        "set_column_property",
                        new
                        {
                            table_name = "Parent",
                            column_name = "Note",
                            data_type = "nvarchar(100)",
                        }
                    )
            )
            .Commands(typeof(PropertyChangeCommand))
            .Check(
                UndoStage.Operated,
                "型が変わる",
                vm => ColumnNamed(vm, "Parent", "Note").DataType.Should().Be("nvarchar(100)")
            )
            .Check(
                UndoStage.Undone,
                "型が戻る",
                vm => ColumnNamed(vm, "Parent", "Note").DataType.Should().Be("nvarchar(50)")
            );

        yield return Row(
                "tool-add-relationship-pushes-add-relationship-command",
                "ツール add_relationship は AddRelationshipCommand を 1 件積み、Undo / Redo でリレーションが戻る"
            )
            .Diagram(ToolDiagram)
            .Operate(
                UndoEntry.Tool,
                "add_relationship（Parent.Id → Child.AltParentId）",
                vm =>
                    CallTool(
                        vm,
                        "add_relationship",
                        new
                        {
                            source_table = "Parent",
                            target_table = "Child",
                            relationship_type = "OneToMany",
                            source_columns = new[] { "Id" },
                            target_columns = new[] { "AltParentId" },
                        }
                    )
            )
            .Commands(typeof(AddRelationshipCommand))
            .Check(
                UndoStage.Operated,
                "リレーションが 2 本になる",
                vm => RelationshipOrder(vm).Should().Equal("Parent->Child", "Parent->Child")
            )
            .Check(
                UndoStage.Undone,
                "リレーションが 1 本へ戻る",
                vm => RelationshipOrder(vm).Should().Equal("Parent->Child")
            );

        yield return Row(
                "tool-remove-relationship-pushes-remove-relationship-command",
                "ツール remove_relationship は RemoveRelationshipCommand を 1 件積み、Undo でリレーションが戻る"
            )
            .Diagram(ToolDiagram)
            .Operate(
                UndoEntry.Tool,
                "remove_relationship（Parent → Child）",
                vm =>
                    CallTool(
                        vm,
                        "remove_relationship",
                        new { source_table = "Parent", target_table = "Child" }
                    )
            )
            .Commands(typeof(RemoveRelationshipCommand))
            .Check(
                UndoStage.Operated,
                "リレーションが消える",
                vm => vm.Relationships.Should().BeEmpty()
            )
            .Check(
                UndoStage.Undone,
                "リレーションが戻る",
                vm => RelationshipOrder(vm).Should().Equal("Parent->Child")
            );

        yield return Row(
                "tool-set-primary-key-pushes-set-primary-key-command",
                "ツール set_primary_key は SetPrimaryKeyCommand を 1 件積み、Undo / Redo で主キーの膜と順序が戻る"
            )
            .Diagram(ToolDiagram)
            .Operate(
                UndoEntry.Tool,
                "set_primary_key（Parent を Id, Code の複合へ）",
                vm =>
                    CallTool(
                        vm,
                        "set_primary_key",
                        new { table_name = "Parent", columns = new[] { "Id", "Code" } }
                    )
            )
            .Commands(typeof(SetPrimaryKeyCommand))
            .Check(
                UndoStage.Operated,
                "主キーが Id, Code になる",
                vm => PrimaryKeyOrder(TableNamed(vm, "Parent")).Should().Equal("Id", "Code")
            )
            .Check(
                UndoStage.Undone,
                "主キーが Id だけへ戻る",
                vm => PrimaryKeyOrder(TableNamed(vm, "Parent")).Should().Equal("Id")
            );

        yield return Row(
                "tool-set-unique-constraint-adds-unique-constraint",
                "ツール set_unique_constraint は新しい列集合なら AddUniqueConstraintCommand を 1 件積み、Undo / Redo で制約が戻る"
            )
            .Diagram(ToolDiagram)
            .Operate(
                UndoEntry.Tool,
                "set_unique_constraint（Parent の Note）",
                vm =>
                    CallTool(
                        vm,
                        "set_unique_constraint",
                        new { table_name = "Parent", columns = new[] { "Note" } }
                    )
            )
            .Commands(typeof(AddUniqueConstraintCommand))
            .Check(
                UndoStage.Operated,
                "一意制約が 2 本になる",
                vm => TableNamed(vm, "Parent").UniqueConstraints.Should().HaveCount(2)
            )
            .Check(
                UndoStage.Undone,
                "一意制約が 1 本へ戻る",
                vm => TableNamed(vm, "Parent").UniqueConstraints.Should().ContainSingle()
            );

        yield return Row(
                "tool-set-unique-constraint-reorders-columns",
                "ツール set_unique_constraint は同じ列集合で順序だけ違うなら ChangeUniqueConstraintColumnsCommand を 1 件積み、Undo / Redo で構成列の順が戻る"
            )
            .Diagram(ToolDiagram)
            .Operate(
                UndoEntry.Tool,
                "set_unique_constraint（Code, Name を Name, Code の順へ）",
                vm =>
                    CallTool(
                        vm,
                        "set_unique_constraint",
                        new { table_name = "Parent", columns = new[] { "Name", "Code" } }
                    )
            )
            .Commands(typeof(ChangeUniqueConstraintColumnsCommand))
            .Check(
                UndoStage.Operated,
                "構成列が Name, Code の順になる",
                vm => ConstraintColumnNames(vm).Should().Equal("Name", "Code")
            )
            .Check(
                UndoStage.Undone,
                "構成列が Code, Name の順へ戻る",
                vm => ConstraintColumnNames(vm).Should().Equal("Code", "Name")
            );

        yield return Row(
                "tool-remove-unique-constraint-pushes-remove-unique-constraint-command",
                "ツール remove_unique_constraint は RemoveUniqueConstraintCommand を 1 件積み、Undo / Redo で制約が戻る"
            )
            .Diagram(ToolDiagram)
            .Operate(
                UndoEntry.Tool,
                "remove_unique_constraint（Parent の Code, Name）",
                vm =>
                    CallTool(
                        vm,
                        "remove_unique_constraint",
                        new { table_name = "Parent", columns = new[] { "Code", "Name" } }
                    )
            )
            .Commands(typeof(RemoveUniqueConstraintCommand))
            .Check(
                UndoStage.Operated,
                "一意制約が消える",
                vm => TableNamed(vm, "Parent").UniqueConstraints.Should().BeEmpty()
            )
            .Check(
                UndoStage.Undone,
                "一意制約が戻る",
                vm => TableNamed(vm, "Parent").UniqueConstraints.Should().ContainSingle()
            );
    }

    /// <summary>ツールを呼び、成功したことを表明する（失敗の文言を添える）</summary>
    private static void CallTool(MainViewModel vm, string toolName, object arguments)
    {
        var (result, success) = ErDiagramDynamicTools.Execute(
            toolName,
            JsonSerializer.SerializeToElement(arguments),
            vm
        );

        success.Should().BeTrue("ツール {0} が成功する（応答: {1}）", toolName, result);
    }

    /// <summary>テーブルの列名を宣言順で返す</summary>
    private static List<string> ColumnNames(MainViewModel vm, string table) =>
        TableNamed(vm, table).Columns.Select(column => column.Name).ToList();

    /// <summary>テーブルの列を名前で引く</summary>
    private static ColumnViewModel ColumnNamed(MainViewModel vm, string table, string column) =>
        TableNamed(vm, table).Columns.Single(candidate => candidate.Name == column);

    /// <summary>Parent の唯一の一意制約の構成列名を宣言順で返す</summary>
    private static List<string> ConstraintColumnNames(MainViewModel vm)
    {
        var parent = TableNamed(vm, "Parent");

        return parent
            .UniqueConstraints.Single()
            .ColumnIds.Select(id => parent.Columns.Single(column => column.Id == id).Name)
            .ToList();
    }

    /// <summary>
    /// Parent（Id＝主キー・Code・Name・Note。Code, Name の名前のない一意制約）と Child（Id＝主キー・ParentId・AltParentId）、
    /// Parent.Id → Child.ParentId のリレーション 1 本の図（ツールの入口の行の図）
    /// </summary>
    private static MainViewModel ToolDiagram()
    {
        var parentId = new Column
        {
            Name = "Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var code = new Column
        {
            Name = "Code",
            DataType = "nvarchar(20)",
            IsNullable = false,
        };
        var name = new Column
        {
            Name = "Name",
            DataType = "nvarchar(50)",
            IsNullable = true,
        };
        var note = new Column
        {
            Name = "Note",
            DataType = "nvarchar(50)",
            IsNullable = true,
        };
        var parent = new Entity
        {
            TableName = "Parent",
            Columns = { parentId, code, name, note },
            UniqueConstraints = { new UniqueConstraint { ColumnIds = [code.Id, name.Id] } },
        };

        var childParentId = new Column
        {
            Name = "ParentId",
            DataType = "int",
            IsNullable = true,
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
                    IsNullable = false,
                },
                childParentId,
                new Column
                {
                    Name = "AltParentId",
                    DataType = "int",
                    IsNullable = true,
                },
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
                        Type = RelationshipType.OneToMany,
                        ConstraintName = "FK_Child_Parent",
                        ColumnPairs = { new RelationshipColumnPair(parentId.Id, childParentId.Id) },
                    },
                },
            }
        );

        return vm;
    }
}
