using System.Linq;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// GUI 実行ホスト（<see cref="ErDiagramDynamicTools"/>）の主キーツール（<c>set_primary_key</c>）の Undo 可能性を検証する。
/// 主キーの意味論（指定順・NOT NULL 化・不正な引数）は 2 ホストの表（<c>ToolScenarioRows.PrimaryKey.cs</c>）が見る。
/// </summary>
public class ErDiagramDynamicToolsPrimaryKeyTests
{
    /// <summary>引数オブジェクトを JSON 化してツールを実行する</summary>
    private static (string Result, bool Success) Exec(
        MainViewModel vm,
        string toolName,
        object args
    )
    {
        var element = JsonSerializer.SerializeToElement(args);
        return ErDiagramDynamicTools.Execute(toolName, element, vm);
    }

    /// <summary>TenantRegion(TenantId PK / RegionCode PK / Code NULL 許容) を持つ ViewModel を用意する</summary>
    private static MainViewModel CreateVmWithTenantRegion()
    {
        var vm = new MainViewModel();
        Exec(vm, "add_entity", new { table_name = "TenantRegion" });
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "TenantRegion",
                column_name = "TenantId",
                data_type = "int",
                is_primary_key = true,
                is_nullable = false,
            }
        );
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "TenantRegion",
                column_name = "RegionCode",
                data_type = "nvarchar(10)",
                is_primary_key = true,
                is_nullable = false,
            }
        );
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "TenantRegion",
                column_name = "Code",
                data_type = "nvarchar(20)",
                is_nullable = true,
            }
        );

        // 準備で積んだ履歴は検証の邪魔になるため捨てる（以降の Undo は主キー操作だけを戻す）
        vm.UndoRedo.Clear();

        return vm;
    }

    /// <summary>TenantRegion エンティティを取り出す</summary>
    private static EntityViewModel TenantRegion(MainViewModel vm) =>
        vm.Entities.Single(entity => entity.TableName == "TenantRegion");

    /// <summary>実効順の主キー列名を取り出す</summary>
    private static List<string> Order(EntityViewModel entity) =>
        entity.GetPrimaryKeyColumnsInOrder().Select(column => column.Name).ToList();

    [Fact(
        DisplayName = "set_primary_key は列宣言順と逆順の複合主キーを指定順どおり記録し Undo で戻せる"
    )]
    public void SetPrimaryKey_CompositeReversedOrder_IsRecordedAndUndoable()
    {
        var vm = CreateVmWithTenantRegion();

        var (_, success) = Exec(
            vm,
            "set_primary_key",
            new { table_name = "TenantRegion", columns = new[] { "RegionCode", "TenantId" } }
        );

        success.Should().BeTrue();
        Order(TenantRegion(vm)).Should().Equal("RegionCode", "TenantId");

        vm.UndoCommand.Execute(null);
        Order(TenantRegion(vm)).Should().Equal("TenantId", "RegionCode");
    }

    [Fact(DisplayName = "set_primary_key は NULL 許容列を主キーに含めると NOT NULL へ正規化する")]
    public void SetPrimaryKey_NullableColumn_BecomesNotNull()
    {
        var vm = CreateVmWithTenantRegion();

        var (_, success) = Exec(
            vm,
            "set_primary_key",
            new { table_name = "TenantRegion", columns = new[] { "Code" } }
        );

        success.Should().BeTrue();
        var entity = TenantRegion(vm);
        var code = entity.Columns.Single(column => column.Name == "Code");
        code.IsPrimaryKey.Should().BeTrue();
        code.IsNullable.Should().BeFalse();
        Order(entity).Should().Equal("Code");

        // 主キーから外れた列の NULL 許容は据え置き（外したことが NULL 許容の変更まで意味しない）
        entity.Columns.Single(column => column.Name == "TenantId").IsNullable.Should().BeFalse();

        vm.UndoCommand.Execute(null);
        code = TenantRegion(vm).Columns.Single(column => column.Name == "Code");
        code.IsPrimaryKey.Should().BeFalse();
        code.IsNullable.Should().BeTrue();
    }
}
