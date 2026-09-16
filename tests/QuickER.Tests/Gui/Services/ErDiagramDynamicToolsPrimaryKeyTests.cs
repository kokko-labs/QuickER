using System.Linq;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// GUI 実行ホスト（<see cref="ErDiagramDynamicTools"/>）の主キーツール（<c>set_primary_key</c>）と、
/// 要約出力・Undo 可能性を検証する。意味論の対はファイル実行ホスト側の
/// <c>DocumentErDiagramPrimaryKeyToolTests</c>。
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

    [Fact(DisplayName = "set_primary_key は列名の大文字小文字を無視して照合する")]
    public void SetPrimaryKey_ColumnNames_AreMatchedCaseInsensitively()
    {
        var vm = CreateVmWithTenantRegion();

        var (_, success) = Exec(
            vm,
            "set_primary_key",
            new { table_name = "TenantRegion", columns = new[] { "regioncode", "tenantid" } }
        );

        success.Should().BeTrue();
        Order(TenantRegion(vm)).Should().Equal("RegionCode", "TenantId");
    }

    [Fact(DisplayName = "set_primary_key は不正な引数をエラーにし図を変更しない")]
    public void SetPrimaryKey_InvalidArguments_FailWithoutTouchingDiagram()
    {
        var invalidArguments = new object[]
        {
            new { table_name = "Ghost", columns = new[] { "TenantId" } },
            new { table_name = "TenantRegion", columns = Array.Empty<string>() },
            new { table_name = "TenantRegion", columns = new[] { "NoSuchColumn" } },
            new { table_name = "TenantRegion", columns = new[] { "TenantId", "tenantid" } },
            new { table_name = "TenantRegion" },
        };

        foreach (var args in invalidArguments)
        {
            var vm = CreateVmWithTenantRegion();

            var (_, success) = Exec(vm, "set_primary_key", args);

            success.Should().BeFalse();
            Order(TenantRegion(vm)).Should().Equal("TenantId", "RegionCode");
            vm.UndoRedo.CanUndo.Should().BeFalse();
        }
    }

    [Fact(DisplayName = "get_diagram_summary は set_primary_key で設定した主キー順を注記する")]
    public void GetDiagramSummary_NotesPrimaryKeyOrder()
    {
        var vm = CreateVmWithTenantRegion();
        Exec(
            vm,
            "set_primary_key",
            new { table_name = "TenantRegion", columns = new[] { "RegionCode", "TenantId" } }
        );

        var (result, success) = Exec(vm, "get_diagram_summary", new { });

        success.Should().BeTrue();
        result.Should().Contain("RegionCode, TenantId");
    }
}
