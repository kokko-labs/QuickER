using System.Linq;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Services;
using QuickER.Tests.Mcp.Tools;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// GUI 実行ホスト（<see cref="ErDiagramDynamicTools"/>）が、リレーションから外れた子列の外部キー印を落とすことを
/// 3 つの削除経路で検証する。意味論の対はファイル実行ホスト側の <c>DocumentErDiagramForeignKeyMarkTests</c>
/// （同じ準備手順 <see cref="DocumentErDiagramForeignKeyMarkTests.SetupSteps"/> を流す）。
/// </summary>
public class ErDiagramDynamicToolsForeignKeyMarkTests
{
    /// <summary>引数オブジェクトを JSON 化してツールを実行する</summary>
    private static (string Result, bool Success) Exec(MainViewModel vm, string tool, object args) =>
        ErDiagramDynamicTools.Execute(tool, JsonSerializer.SerializeToElement(args), vm);

    /// <summary>準備手順を流した ViewModel を作る</summary>
    private static MainViewModel CreateVm()
    {
        var vm = new MainViewModel();

        foreach (var (tool, args) in DocumentErDiagramForeignKeyMarkTests.SetupSteps)
        {
            Exec(vm, tool, args).Success.Should().BeTrue($"{tool} must succeed");
        }

        return vm;
    }

    /// <summary>Order の列名 → 外部キー印</summary>
    private static Dictionary<string, bool> OrderMarks(MainViewModel vm) =>
        vm
            .Entities.Single(entity => entity.TableName == "Order")
            .Columns.ToDictionary(column => column.Name, column => column.IsForeignKey);

    [Fact(DisplayName = "GUI: remove_relationship は外れた子列の外部キー印を落とす")]
    public void RemoveRelationship_ReleasesChildColumnMark()
    {
        var vm = CreateVm();

        Exec(
            vm,
            "remove_relationship",
            new
            {
                source_table = "Customer",
                target_table = "Order",
                constraint_name = "FK_BillTo",
            }
        )
            .Success.Should()
            .BeTrue();

        var marks = OrderMarks(vm);
        marks["BillToCustomerId"].Should().BeFalse();
        marks["CustomerId"].Should().BeTrue();
        marks["RegionId"].Should().BeTrue();
    }

    [Fact(DisplayName = "GUI: 親テーブルの remove_entity は子テーブルに残る列の外部キー印を落とす")]
    public void RemoveParentEntity_ReleasesChildColumnMarks()
    {
        var vm = CreateVm();

        Exec(vm, "remove_entity", new { table_name = "Region" }).Success.Should().BeTrue();

        var marks = OrderMarks(vm);
        marks["RegionId"].Should().BeFalse();
        marks["RegionCode"].Should().BeFalse();
        marks["CustomerId"].Should().BeTrue();
        marks["BillToCustomerId"].Should().BeTrue();
    }

    [Fact(DisplayName = "GUI: 親列の remove_column は複合外部キーの子列すべての印を落とす")]
    public void RemoveParentColumn_ReleasesEveryCompositeChildColumnMark()
    {
        var vm = CreateVm();

        Exec(vm, "remove_column", new { table_name = "Region", column_name = "Code" })
            .Success.Should()
            .BeTrue();

        var marks = OrderMarks(vm);
        marks["RegionId"].Should().BeFalse();
        marks["RegionCode"].Should().BeFalse();
        marks["CustomerId"].Should().BeTrue();
    }

    [Fact(DisplayName = "GUI: 別のリレーションが使い続けている子列の外部キー印は残す")]
    public void RemoveRelationship_KeepsMarkOfColumnStillUsedByAnother()
    {
        var vm = CreateVm();

        Exec(vm, "remove_entity", new { table_name = "Account" }).Success.Should().BeTrue();

        OrderMarks(vm)["CustomerId"].Should().BeTrue();
    }
}
