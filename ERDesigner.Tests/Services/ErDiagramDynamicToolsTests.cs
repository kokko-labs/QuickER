using System.Text.Json;
using ERDesigner.Services;
using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary>
/// <see cref="ErDiagramDynamicTools"/> のツール実行ロジックのテスト。
/// </summary>
public class ErDiagramDynamicToolsTests
{
    private static MainViewModel CreateVm() => new MainViewModel();

    private static (string Result, bool Success) Exec(MainViewModel vm, string toolName, object args)
    {
        var json = JsonSerializer.Serialize(args);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        return ErDiagramDynamicTools.Execute(toolName, element, vm);
    }

    [Fact(DisplayName = "add_entity でテーブルが追加され列は自動生成されない")]
    public void AddEntity_CreatesEntityWithNoColumns()
    {
        var vm = CreateVm();

        var (_, success) = Exec(vm, "add_entity", new { table_name = "Book" });

        success.Should().BeTrue();
        var entity = vm.Entities.Single(e => e.TableName == "Book");
        // 列は AI が add_column で定義するため、自動生成されないこと
        entity.Columns.Should().BeEmpty();
    }

    [Fact(DisplayName = "add_column で is_primary_key=true の列を追加できる")]
    public void AddColumn_WithIsPrimaryKeyTrue_SetsPrimaryKey()
    {
        var vm = CreateVm();
        Exec(vm, "add_entity", new { table_name = "Book" });

        var (_, success) = Exec(
            vm,
            "add_column",
            new
            {
                table_name = "Book",
                column_name = "BookId",
                data_type = "int",
                is_primary_key = true,
                is_nullable = false,
            }
        );

        success.Should().BeTrue();
        var entity = vm.Entities.Single(e => e.TableName == "Book");
        entity.Columns.Single(c => c.Name == "BookId").IsPrimaryKey.Should().BeTrue();
    }

    [Fact(DisplayName = "add_column を複数回呼び出すと AI が設計した列構成になる")]
    public void AddColumn_MultipleColumns_ReflectsAiDesign()
    {
        var vm = CreateVm();
        Exec(vm, "add_entity", new { table_name = "Book" });

        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "Book",
                column_name = "BookId",
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
                table_name = "Book",
                column_name = "Title",
                data_type = "nvarchar(200)",
                is_primary_key = false,
                is_nullable = false,
            }
        );
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "Book",
                column_name = "AuthorId",
                data_type = "int",
                is_primary_key = false,
                is_nullable = false,
            }
        );

        var entity = vm.Entities.Single(e => e.TableName == "Book");

        // PK は BookId のみ
        entity.Columns.Where(c => c.IsPrimaryKey).Should().ContainSingle(c => c.Name == "BookId");
        entity.Columns.Should().HaveCount(3);
    }
}
