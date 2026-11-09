using System.Text.Json;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// プロパティパネルが書き込む VM の setter（<see cref="EntityViewModel.TableName"/>・<see cref="ColumnViewModel.Name"/>・
/// <see cref="UniqueConstraintViewModel.Name"/>・<see cref="RelationshipViewModel.ConstraintName"/>）と、表の複製・貼り付けの
/// 入口（<see cref="MainViewModel.CreateEntityCopy(QuickER.Model.Entity, QuickER.Documents.EntityLayout, int)"/>）へ
/// 名前の総当たり（<see cref="NameFuzz"/>）を流し、重複の判定が <see cref="NameIdentityRule"/> と同じ答えになることを
/// 確かめるテストクラス。
/// </summary>
/// <remarks>
/// setter の入口は、図に先の名前と別の要素（<c>Zz</c>）を置き、別の要素の名前を後の名前へ書き換える。規則が「同じ名前」と言う組は
/// 書き換えが図に残らないこと（拒否）、「別の名前」と言う組は図に残ることを表明する（判定は書き換え後の
/// <see cref="MainViewModel.ToDiagramModel"/> で見る）。貼り付けの入口は、写した一意制約の名前が図の制約名とぶつかるときだけ
/// 名前が外れる（拒否）ことと、貼り付けた後の DDL の制約名がすべて違うことを表明する。
/// 結果は既知の失敗の一覧（<see cref="NameFuzzKnownFailures"/>）と厳密に照合する。
/// </remarks>
public sealed class NameIdentityViewModelFuzzTests
{
    [Fact(DisplayName = "プロパティパネルの名前の書き換えは名前の規則と同じ重複の判定になる")]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public void PropertySetters_AgreeWithNameRule()
    {
        var ledger = new FuzzFailureLog(NameFuzzKnownFailures.Set);

        NameFuzzRunner.Run(
            ledger,
            "gui-vm:EntityViewModel.TableName",
            NameKind.Table,
            name => RenameTable(null, name).Accepted,
            pair => RenameTable(pair.First, pair.Second).Outcome
        );
        NameFuzzRunner.Run(
            ledger,
            "gui-vm:ColumnViewModel.Name",
            NameKind.Column,
            name => RenameColumn(null, name).Accepted,
            pair => RenameColumn(pair.First, pair.Second).Outcome
        );
        NameFuzzRunner.Run(
            ledger,
            "gui-vm:UniqueConstraintViewModel.Name",
            NameKind.Constraint,
            name => RenameUniqueConstraint(null, name).Accepted,
            pair => RenameUniqueConstraint(pair.First, pair.Second).Outcome
        );
        NameFuzzRunner.Run(
            ledger,
            "gui-vm:RelationshipViewModel.ConstraintName",
            NameKind.Constraint,
            name => RenameForeignKey(null, name).Accepted,
            pair => RenameForeignKey(pair.First, pair.Second).Outcome
        );
        NameFuzzRunner.Run(
            ledger,
            "gui-vm:CreateEntityCopy",
            NameKind.Constraint,
            name => PasteCopiedTable(null, name).Accepted,
            pair => PasteCopiedTable(pair.First, pair.Second).Outcome
        );

        ledger.AssertMatchesKnown("gui-vm:");
    }

    /// <summary>
    /// テーブル <c>Other</c> に先の名前の一意制約（あれば）、テーブル <c>T</c> に名前 <c>Zz</c> の一意制約を置き、
    /// <c>Zz</c> を後の名前へ書き換える
    /// </summary>
    private static (bool Accepted, PairOutcome Outcome) RenameUniqueConstraint(
        string? first,
        string second
    )
    {
        var vm = new MainViewModel();

        if (first is not null)
        {
            vm.Entities.Add(TableWithUniqueConstraint("Other", first));
        }

        var target = TableWithUniqueConstraint("T", "Zz");
        vm.Entities.Add(target);
        target.UniqueConstraints.Single().Name = second;
        var names = vm.ToDiagramModel()
            .Entities.Select(entity => entity.UniqueConstraints.Single().Name ?? "")
            .ToList();

        return Judge(names, second);
    }

    /// <summary>
    /// 主キー Id を持つ P と列 C1・C2 を持つ T を置き、P→T の外部キーを先の名前（あれば・C1）と名前 <c>Zz</c>（C2）で張り、
    /// <c>Zz</c> を後の名前へ書き換える
    /// </summary>
    private static (bool Accepted, PairOutcome Outcome) RenameForeignKey(
        string? first,
        string second
    )
    {
        var vm = new MainViewModel();
        var parent = new EntityViewModel(
            new Entity
            {
                TableName = "P",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                },
            }
        );
        var child = new EntityViewModel(
            new Entity
            {
                TableName = "T",
                Columns =
                {
                    new Column { Name = "C1", DataType = "int" },
                    new Column { Name = "C2", DataType = "int" },
                },
            }
        );
        vm.Entities.Add(parent);
        vm.Entities.Add(child);

        void Link(string childColumn, string name) =>
            vm.Relationships.Add(
                new RelationshipViewModel(
                    new Relationship
                    {
                        SourceEntityId = parent.Id,
                        TargetEntityId = child.Id,
                        Type = RelationshipType.OneToMany,
                        ColumnPairs =
                        {
                            new RelationshipColumnPair(
                                parent.Columns[0].Id,
                                child.Columns.Single(column => column.Name == childColumn).Id
                            ),
                        },
                        ConstraintName = name,
                    },
                    parent,
                    child
                )
            );

        if (first is not null)
        {
            Link("C1", first);
        }

        Link("C2", "Zz");
        vm.Relationships[^1].ConstraintName = second;
        var names = vm.ToDiagramModel()
            .Relationships.Select(relationship => relationship.ConstraintName ?? "")
            .ToList();

        return Judge(names, second);
    }

    /// <summary>
    /// テーブル A に先の名前の一意制約（あれば）、テーブル B に後の名前の一意制約を置き、B をコピー → B を削除 → 貼り付ける
    /// </summary>
    /// <remarks>
    /// 受け付けた＝貼り付けたテーブルの一意制約が後の名前を保った（外れれば名前なし＝合成名）。
    /// 貼り付けた後の図の DDL の制約名がすべて違うことも表明する（違反 <c>ddl-constraint-names-duplicated</c>）
    /// </remarks>
    private static (bool Accepted, PairOutcome Outcome) PasteCopiedTable(
        string? first,
        string second
    )
    {
        var vm = new MainViewModel();

        if (first is not null)
        {
            vm.Entities.Add(TableWithUniqueConstraint("A", first));
        }

        var source = TableWithUniqueConstraint("B", second);
        vm.Entities.Add(source);
        vm.OnEntityClicked(source);
        vm.CopySelectedEntityCommand.Execute(null);
        vm.RemoveSelectedEntityCommand.Execute(null);
        vm.PasteCopiedEntityCommand.Execute(null);

        var diagram = vm.ToDiagramModel();
        var names = diagram
            .Entities.Select(entity => entity.UniqueConstraints.Single().Name ?? "")
            .ToList();
        var pasted = names[^1];
        var accepted = pasted.Length > 0;
        var duplicates = DdlConstraintNames.Duplicates(diagram);
        var violations = new List<string>();

        if (accepted && pasted != NameIdentityRule.StoredForm(second))
        {
            violations.Add("stored-form-differs");
        }

        if (duplicates.Count > 0)
        {
            violations.Add("ddl-constraint-names-duplicated");
        }

        return (
            accepted,
            new PairOutcome(
                accepted,
                violations,
                $"{Describe(names)} / DDL duplicates {Describe(duplicates)}"
            )
        );
    }

    /// <summary>列 C を持ち、C の一意制約（指定の名前）を持つテーブル</summary>
    private static EntityViewModel TableWithUniqueConstraint(string tableName, string name)
    {
        var column = new Column { Name = "C", DataType = "int" };

        return new EntityViewModel(
            new Entity
            {
                TableName = tableName,
                Columns = { column },
                UniqueConstraints =
                {
                    new UniqueConstraint { Name = name, ColumnIds = { column.Id } },
                },
            }
        );
    }

    /// <summary>テーブル <c>Zz</c>（と、あれば先の名前のテーブル）を置き、<c>Zz</c> を後の名前へ書き換える</summary>
    private static (bool Accepted, PairOutcome Outcome) RenameTable(string? first, string second)
    {
        var vm = new MainViewModel();

        if (first is not null)
        {
            Exec(vm, "add_entity", new { table_name = first });
        }

        Exec(vm, "add_entity", new { table_name = "Zz" });
        var target = vm.Entities.Single(entity => entity.TableName == "Zz");
        target.TableName = second;
        var names = vm.ToDiagramModel().Entities.Select(entity => entity.TableName).ToList();

        return Judge(names, second);
    }

    /// <summary>テーブル T に列 <c>Zz</c>（と、あれば先の名前の列）を置き、<c>Zz</c> を後の名前へ書き換える</summary>
    private static (bool Accepted, PairOutcome Outcome) RenameColumn(string? first, string second)
    {
        var vm = new MainViewModel();
        Exec(vm, "add_entity", new { table_name = "T" });

        foreach (var name in first is null ? ["Zz"] : new[] { first, "Zz" })
        {
            Exec(
                vm,
                "add_column",
                new
                {
                    table_name = "T",
                    column_name = name,
                    data_type = "int",
                }
            );
        }

        var target = vm.Entities.Single().Columns.Single(column => column.Name == "Zz");
        target.Name = second;
        var names = vm.ToDiagramModel().Entities.Single().Columns.Select(c => c.Name).ToList();

        return Judge(names, second);
    }

    /// <summary>
    /// 書き換え後の図の名前（並びは書き換えた要素が最後）から、書き換えが残ったか（受け付けたか）と保存形を判定する
    /// </summary>
    private static (bool Accepted, PairOutcome Outcome) Judge(List<string> names, string second)
    {
        var stored = names[^1];
        var accepted = stored != "Zz";
        IReadOnlyList<string> violations =
            accepted && stored != NameIdentityRule.StoredForm(second)
                ? ["stored-form-differs"]
                : [];

        return (accepted, new PairOutcome(accepted, violations, Describe(names)));
    }

    private static void Exec(MainViewModel vm, string tool, object args)
    {
        var (result, success) = ErDiagramDynamicTools.Execute(
            tool,
            JsonSerializer.SerializeToElement(args),
            vm
        );

        if (!success)
        {
            throw new InvalidOperationException($"前提の組み立てに失敗: {tool}: {result}");
        }
    }

    private static string Describe(IEnumerable<string> names) =>
        "[" + string.Join(", ", names.Select(NameFuzz.Describe)) + "]";
}
