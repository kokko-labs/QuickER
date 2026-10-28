using System.Text.Json;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// プロパティパネルが書き込む VM の setter（<see cref="EntityViewModel.TableName"/>・<see cref="ColumnViewModel.Name"/>）へ
/// 名前の総当たり（<see cref="NameFuzz"/>）を流し、重複の判定が <see cref="NameIdentityRule"/> と同じ答えになることを
/// 確かめるテストクラス。
/// </summary>
/// <remarks>
/// 図に先の名前と別の要素（<c>Zz</c>）を置き、別の要素の名前を後の名前へ書き換える。規則が「同じ名前」と言う組は
/// 書き換えが図に残らないこと（拒否）、「別の名前」と言う組は図に残ることを表明する（判定は書き換え後の
/// <see cref="MainViewModel.ToDiagramModel"/> で見る）。結果は既知の失敗の一覧（<see cref="NameFuzzKnownFailures"/>）と厳密に照合する。
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

        ledger.AssertMatchesKnown("gui-vm:");
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
