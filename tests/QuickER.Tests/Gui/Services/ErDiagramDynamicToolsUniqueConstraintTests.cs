using System.Linq;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// GUI 実行ホスト（<see cref="ErDiagramDynamicTools"/>）の一意制約ツール
/// （<c>set_unique_constraint</c> / <c>remove_unique_constraint</c>）と、要約出力・Undo 可能性を検証する。
/// </summary>
public class ErDiagramDynamicToolsUniqueConstraintTests
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

    /// <summary>Customer(CustomerId PK / Email / TenantId / Code) を持つ ViewModel を用意する</summary>
    private static MainViewModel CreateVmWithCustomer()
    {
        var vm = new MainViewModel();
        Exec(vm, "add_entity", new { table_name = "Customer" });
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "Customer",
                column_name = "CustomerId",
                data_type = "int",
                is_primary_key = true,
                is_nullable = false,
            }
        );

        foreach (var columnName in new[] { "Email", "TenantId", "Code" })
        {
            Exec(
                vm,
                "add_column",
                new
                {
                    table_name = "Customer",
                    column_name = columnName,
                    data_type = "nvarchar(100)",
                    is_nullable = false,
                }
            );
        }

        return vm;
    }

    /// <summary>Customer エンティティを取り出す</summary>
    private static EntityViewModel Customer(MainViewModel vm) =>
        vm.Entities.Single(entity => entity.TableName == "Customer");

    [Fact(DisplayName = "set_unique_constraint で一意制約を追加でき Undo で取り消せる")]
    public void SetUniqueConstraint_AddsConstraint_AndIsUndoable()
    {
        var vm = CreateVmWithCustomer();

        var (_, success) = Exec(
            vm,
            "set_unique_constraint",
            new
            {
                table_name = "Customer",
                columns = new[] { "TenantId", "Code" },
                name = "UQ_Customer_Tenant",
            }
        );

        success.Should().BeTrue();
        var entity = Customer(vm);
        var constraint = entity.UniqueConstraints.Should().ContainSingle().Subject;
        constraint.Name.Should().Be("UQ_Customer_Tenant");
        constraint
            .ColumnIds.Should()
            .Equal(
                entity.Columns.Single(column => column.Name == "TenantId").Id,
                entity.Columns.Single(column => column.Name == "Code").Id
            );
        // 構成列のキー標識（UQ マーク）も追従する
        entity
            .Columns.Single(column => column.Name == "Code")
            .IsUniqueConstraintMember.Should()
            .BeTrue();

        vm.UndoCommand.Execute(null);
        Customer(vm).UniqueConstraints.Should().BeEmpty();
    }

    [Fact(DisplayName = "set_unique_constraint は同じ列集合を upsert して同一制約を保つ")]
    public void SetUniqueConstraint_SameColumnSet_Upserts()
    {
        var vm = CreateVmWithCustomer();
        Exec(
            vm,
            "set_unique_constraint",
            new { table_name = "Customer", columns = new[] { "TenantId", "Code" } }
        );
        var originalId = Customer(vm).UniqueConstraints.Single().Id;

        var (result, success) = Exec(
            vm,
            "set_unique_constraint",
            new
            {
                table_name = "Customer",
                columns = new[] { "code", "tenantid" },
                name = "UQ_Renamed",
            }
        );

        success.Should().BeTrue();
        result.Should().NotBeNullOrWhiteSpace();

        var entity = Customer(vm);
        var constraint = entity.UniqueConstraints.Should().ContainSingle().Subject;
        constraint.Id.Should().Be(originalId);
        constraint.Name.Should().Be("UQ_Renamed");
        constraint
            .ColumnIds.Should()
            .Equal(
                entity.Columns.Single(column => column.Name == "Code").Id,
                entity.Columns.Single(column => column.Name == "TenantId").Id
            );
    }

    [Fact(DisplayName = "set_unique_constraint は存在しない列名をエラーにする")]
    public void SetUniqueConstraint_UnknownColumn_Fails()
    {
        var vm = CreateVmWithCustomer();

        var (result, success) = Exec(
            vm,
            "set_unique_constraint",
            new { table_name = "Customer", columns = new[] { "NoSuchColumn" } }
        );

        success.Should().BeFalse();
        result.Should().Contain("NoSuchColumn");
        Customer(vm).UniqueConstraints.Should().BeEmpty();
    }

    [Fact(DisplayName = "remove_unique_constraint は列集合で特定した制約を削除し Undo で戻せる")]
    public void RemoveUniqueConstraint_RemovesConstraint_AndIsUndoable()
    {
        var vm = CreateVmWithCustomer();
        Exec(
            vm,
            "set_unique_constraint",
            new { table_name = "Customer", columns = new[] { "Email" } }
        );

        var (_, success) = Exec(
            vm,
            "remove_unique_constraint",
            new { table_name = "Customer", columns = new[] { "email" } }
        );

        success.Should().BeTrue();
        Customer(vm).UniqueConstraints.Should().BeEmpty();

        vm.UndoCommand.Execute(null);
        Customer(vm).UniqueConstraints.Should().ContainSingle();
    }

    [Fact(DisplayName = "remove_unique_constraint は列集合が一致しなければエラーにする")]
    public void RemoveUniqueConstraint_NoExactMatch_Fails()
    {
        var vm = CreateVmWithCustomer();
        Exec(
            vm,
            "set_unique_constraint",
            new { table_name = "Customer", columns = new[] { "TenantId", "Code" } }
        );

        var (_, success) = Exec(
            vm,
            "remove_unique_constraint",
            new { table_name = "Customer", columns = new[] { "Code" } }
        );

        success.Should().BeFalse();
        Customer(vm).UniqueConstraints.Should().ContainSingle();
    }

    [Fact(DisplayName = "remove_column は削除列を含む一意制約を制約ごと削除する")]
    public void RemoveColumn_CascadesToUniqueConstraints()
    {
        var vm = CreateVmWithCustomer();
        Exec(
            vm,
            "set_unique_constraint",
            new { table_name = "Customer", columns = new[] { "Email" } }
        );
        Exec(
            vm,
            "set_unique_constraint",
            new { table_name = "Customer", columns = new[] { "TenantId", "Code" } }
        );

        var (_, success) = Exec(
            vm,
            "remove_column",
            new { table_name = "Customer", column_name = "TenantId" }
        );

        success.Should().BeTrue();
        var entity = Customer(vm);
        entity.UniqueConstraints.Should().ContainSingle();
        entity
            .UniqueConstraints[0]
            .ColumnIds.Should()
            .Equal(entity.Columns.Single(column => column.Name == "Email").Id);
    }

    [Fact(DisplayName = "get_diagram_summary は一意制約（名前 or 合成名・構成列）を出力する")]
    public void GetDiagramSummary_ListsUniqueConstraints()
    {
        var vm = CreateVmWithCustomer();
        Exec(
            vm,
            "set_unique_constraint",
            new { table_name = "Customer", columns = new[] { "Email" } }
        );
        Exec(
            vm,
            "set_unique_constraint",
            new
            {
                table_name = "Customer",
                columns = new[] { "TenantId", "Code" },
                name = "UQ_Tenant_Code",
            }
        );

        var (result, success) = Exec(vm, "get_diagram_summary", new { });

        success.Should().BeTrue();
        result.Should().Contain(UniqueConstraint.SynthesizeName("Customer", ["Email"]));
        result.Should().Contain("UQ_Tenant_Code (TenantId, Code)");
    }
}
