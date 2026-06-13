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

    /// <summary>テスト用に親テーブル（PK 付き）と子テーブル（PK + 任意列）を作成する</summary>
    private static void SetupParentAndChild(MainViewModel vm, string childExtraColumn)
    {
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
        Exec(vm, "add_entity", new { table_name = "Order" });
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "Order",
                column_name = "OrderId",
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
                table_name = "Order",
                column_name = childExtraColumn,
                data_type = "int",
                is_primary_key = false,
                is_nullable = true,
            }
        );
    }

    /// <summary>add_relationship で明示指定した source_column / target_column が使用されることを検証する</summary>
    [Fact(DisplayName = "add_relationship で明示指定したカラムがそのまま使用される")]
    public void AddRelationship_WithExplicitColumns_UsesSpecifiedColumns()
    {
        var vm = CreateVm();

        // FK 列名が命名規則（CustomerId）と異なる OwnerId のケース
        SetupParentAndChild(vm, childExtraColumn: "OwnerId");

        var (_, success) = Exec(
            vm,
            "add_relationship",
            new
            {
                source_table = "Customer",
                source_column = "CustomerId",
                target_table = "Order",
                target_column = "OwnerId",
                relationship_type = "OneToMany",
            }
        );

        success.Should().BeTrue();
        var relationship = vm.Relationships.Single();
        var order = vm.Entities.Single(e => e.TableName == "Order");
        relationship.SourceColumnId.Should().Be(vm.Entities.Single(e => e.TableName == "Customer").Columns.Single(c => c.Name == "CustomerId").Id);
        relationship.TargetColumnId.Should().Be(order.Columns.Single(c => c.Name == "OwnerId").Id);
    }

    /// <summary>add_relationship で存在しないカラムを指定するとエラーになることを検証する</summary>
    [Fact(DisplayName = "add_relationship で存在しない target_column を指定するとエラーになる")]
    public void AddRelationship_UnknownTargetColumn_ReturnsError()
    {
        var vm = CreateVm();
        SetupParentAndChild(vm, childExtraColumn: "OwnerId");

        var (result, success) = Exec(
            vm,
            "add_relationship",
            new
            {
                source_table = "Customer",
                target_table = "Order",
                target_column = "NoSuchColumn",
                relationship_type = "OneToMany",
            }
        );

        success.Should().BeFalse();
        result.Should().Contain("NoSuchColumn");
        vm.Relationships.Should().BeEmpty();
    }

    /// <summary>カラム省略時に名前から FK を推測できない場合、無関係な列が FK 化されず未割当となることを検証する</summary>
    [Fact(DisplayName = "add_relationship でカラム省略かつ推測不能な場合は参照先列が未割当となる")]
    public void AddRelationship_NoLikelyColumn_LeavesTargetUnassigned()
    {
        var vm = CreateVm();

        // FK らしい名前の列が無い（従来は先頭の非 PK 列 Quantity がフォールバックで FK 化されていた）
        SetupParentAndChild(vm, childExtraColumn: "Quantity");

        var (_, success) = Exec(
            vm,
            "add_relationship",
            new
            {
                source_table = "Customer",
                target_table = "Order",
                relationship_type = "OneToMany",
            }
        );

        success.Should().BeTrue();
        var relationship = vm.Relationships.Single();
        relationship.TargetColumnId.Should().BeNull();

        var quantityColumn = vm.Entities.Single(e => e.TableName == "Order").Columns.Single(c => c.Name == "Quantity");
        quantityColumn.IsForeignKey.Should().BeFalse();
        quantityColumn.IsNullable.Should().BeTrue();
    }

    /// <summary>AI ツールで FK 列を削除し Undo すると、列と併せてリレーションの FK 参照も復元されることを検証する</summary>
    [Fact(DisplayName = "remove_column で削除した FK 列は Undo でリレーションの参照も復元される")]
    public void RemoveColumn_UsedAsFk_UndoRestoresRelationshipReference()
    {
        var vm = CreateVm();

        // 親（PK）と子（PK + FK 候補列）を作成し、ツール経由でリレーションを張る
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
        Exec(vm, "add_entity", new { table_name = "Order" });
        Exec(
            vm,
            "add_column",
            new
            {
                table_name = "Order",
                column_name = "OrderId",
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
                table_name = "Order",
                column_name = "CustomerId",
                data_type = "int",
                is_primary_key = false,
                is_nullable = false,
            }
        );
        Exec(vm, "add_relationship", new { source_table = "Customer", target_table = "Order" });

        var relationship = vm.Relationships.Single();
        var originalTargetColumnId = relationship.TargetColumnId;
        originalTargetColumnId.Should().NotBeNull("add_relationship が FK 列を参照先として解決する");

        var (_, success) = Exec(vm, "remove_column", new { table_name = "Order", column_name = "CustomerId" });
        success.Should().BeTrue();

        // 削除後はリレーションの FK 参照がクリアされる
        relationship.TargetColumnId.Should().BeNull();

        // Undo でカラムが復元され、リレーションの FK 参照も復元される
        vm.UndoCommand.Execute(null);
        var order = vm.Entities.Single(e => e.TableName == "Order");
        order.Columns.Should().Contain(c => c.Id == originalTargetColumnId);
        relationship.TargetColumnId.Should().Be(originalTargetColumnId);
    }

    /// <summary>ツール説明文に複合キー禁止の設計ルールが含まれることを検証する（AI への指示はツール説明文経由のため）</summary>
    [Fact(DisplayName = "ツール説明文に複合PK・複合FKの禁止ルールが含まれる")]
    public void GetDefinitions_DescriptionsContainCompositeKeyProhibition()
    {
        var definitions = ErDiagramDynamicTools.GetDefinitions();

        definitions.Single(d => d.Name == "add_column").Description.Should().Contain(ErDesignRules.SinglePrimaryKeyRule);
        definitions.Single(d => d.Name == "add_relationship").Description.Should().Contain(ErDesignRules.SingleColumnForeignKeyRule);
        definitions.Single(d => d.Name == "add_entity").Description.Should().Contain("主キー列を 1 列だけ");
    }

    /// <summary>OpenAI Function Calling 用の ChatTool 変換が、全ツールを名前付きで生成することを検証する</summary>
    [Fact(DisplayName = "ToOpenAiTools は全ツール定義を ChatTool へ変換する")]
    public void ToOpenAiTools_ConvertsAllDefinitions()
    {
        var definitions = ErDiagramDynamicTools.GetDefinitions();
        var tools = ErDiagramDynamicTools.ToOpenAiTools();

        tools.Should().HaveCount(definitions.Count);
        tools.Select(t => t.FunctionName).Should().BeEquivalentTo(definitions.Select(d => d.Name));
    }
}
