using System.Text.Json;
using ERDesigner.Services;
using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary><see cref="ErDiagramDynamicTools"/> の各ツール実行（エンティティ・カラム操作）を検証するテストクラス</summary>
public class ErDiagramDynamicToolsTests
{
    /// <summary>テスト対象のメイン ViewModel を生成する</summary>
    private static MainViewModel CreateVm() => new MainViewModel();

    /// <summary>引数オブジェクトを JSON 化してツールを実行し、結果と成否を返す</summary>
    private static (string Result, bool Success) Exec(MainViewModel vm, string toolName, object args)
    {
        var json = JsonSerializer.Serialize(args);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        return ErDiagramDynamicTools.Execute(toolName, element, vm);
    }

    /// <summary>add_entity でテーブルが追加され、列が自動生成されないことを検証する</summary>
    [Fact(DisplayName = "add_entity でテーブルが追加され列は自動生成されない")]
    public void AddEntity_CreatesEntityWithNoColumns()
    {
        var vm = CreateVm();

        var (_, success) = Exec(vm, "add_entity", new { table_name = "Book" });

        success.Should().BeTrue();
        var entity = vm.Entities.Single(e => e.TableName == "Book");
        // 列は AI が add_column で定義する想定のため、ここでは自動生成されない
        entity.Columns.Should().BeEmpty();
    }

    /// <summary>add_column で is_primary_key=true の列が主キーとして追加されることを検証する</summary>
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

    /// <summary>add_column を複数回呼ぶと指定どおりの列構成（主キーは 1 列）になることを検証する</summary>
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

    /// <summary>set_column_property でカラム説明を更新できることを検証する</summary>
    [Fact(DisplayName = "set_column_property でカラムの説明を更新できる")]
    public void SetColumnProperty_UpdatesDescription()
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
                description = "",
            }
        );

        var (_, success) = Exec(
            vm,
            "set_column_property",
            new
            {
                table_name = "Book",
                column_name = "BookId",
                description = "書籍を一意に識別するID",
            }
        );

        success.Should().BeTrue();
        var column = vm.Entities.Single(e => e.TableName == "Book").Columns.Single(c => c.Name == "BookId");
        column.Description.Should().Be("書籍を一意に識別するID");
    }

    /// <summary>set_column_property でデータ型と NULL 許容を同時更新できることを検証する</summary>
    [Fact(DisplayName = "set_column_property でデータ型と NULL 許容を更新できる")]
    public void SetColumnProperty_UpdatesDataTypeAndNullable()
    {
        var vm = CreateVm();
        Exec(vm, "add_entity", new { table_name = "Book" });
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "Book",
                column_name = "Title",
                data_type = "nvarchar(100)",
                is_primary_key = false,
                is_nullable = false,
            }
        );

        var (_, success) = Exec(
            vm,
            "set_column_property",
            new
            {
                table_name = "Book",
                column_name = "Title",
                data_type = "nvarchar(500)",
                is_nullable = true,
            }
        );

        success.Should().BeTrue();
        var column = vm.Entities.Single(e => e.TableName == "Book").Columns.Single(c => c.Name == "Title");
        column.DataType.Should().Be("nvarchar(500)");
        column.IsNullable.Should().BeTrue();
    }

    /// <summary>存在しないカラムを set_column_property で指定すると失敗を返すことを検証する</summary>
    [Fact(DisplayName = "set_column_property で存在しないカラムを指定するとエラーになる")]
    public void SetColumnProperty_UnknownColumn_ReturnsError()
    {
        var vm = CreateVm();
        Exec(vm, "add_entity", new { table_name = "Book" });

        var (_, success) = Exec(
            vm,
            "set_column_property",
            new
            {
                table_name = "Book",
                column_name = "NoSuchColumn",
                description = "説明",
            }
        );

        success.Should().BeFalse();
    }
}
