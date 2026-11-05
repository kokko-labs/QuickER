using System.Text.Json;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.Mcp;
using QuickER.Services;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.Services;

/// <summary><see cref="ErDiagramDynamicTools"/> の各ツール実行（エンティティ・カラム操作）を検証するテストクラス</summary>
public class ErDiagramDynamicToolsTests
{
    /// <summary>テスト対象のメイン ViewModel を生成する</summary>
    private static MainViewModel CreateVm() => new MainViewModel();

    /// <summary>引数オブジェクトを JSON 化してツールを実行し、結果と成否を返す</summary>
    private static (string Result, bool Success) Exec(
        MainViewModel vm,
        string toolName,
        object args
    )
    {
        var json = JsonSerializer.Serialize(args);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        return ErDiagramDynamicTools.Execute(toolName, element, vm);
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
        Exec(
            vm,
            "add_relationship",
            new
            {
                source_table = "Customer",
                target_table = "Order",
                relationship_type = "OneToMany",
            }
        );

        var relationship = vm.Relationships.Single();
        var originalTargetColumnId = relationship
            .ColumnPairs.Should()
            .ContainSingle("add_relationship が FK 列を参照先として解決する")
            .Subject.TargetColumnId;

        var (_, success) = Exec(
            vm,
            "remove_column",
            new { table_name = "Order", column_name = "CustomerId" }
        );
        success.Should().BeTrue();

        // 削除後はリレーションの列ペアがクリアされる
        relationship.ColumnPairs.Should().BeEmpty();

        // Undo でカラムが復元され、リレーションの列ペアも復元される
        vm.UndoCommand.Execute(null);
        var order = vm.Entities.Single(e => e.TableName == "Order");
        order.Columns.Should().Contain(c => c.Id == originalTargetColumnId);
        relationship
            .ColumnPairs.Should()
            .ContainSingle()
            .Subject.TargetColumnId.Should()
            .Be(originalTargetColumnId);
    }

    /// <summary>
    /// ツール説明文（英語）に複合主キーの扱い（コード生成器が非対応・宣言は set_primary_key）と、
    /// 複合外部キーの指定方法が含まれることを検証する（AI への指示はツール説明文経由のため）
    /// </summary>
    [Fact(DisplayName = "ツール説明文に複合PKの扱いと複合FKの指定方法が含まれる")]
    public void GetDefinitions_DescriptionsContainCompositeKeyGuidance()
    {
        var definitions = ErDiagramToolCatalog.GetDefinitions();

        definitions
            .Single(d => d.Name == "add_column")
            .Description.Should()
            .Contain("the C# code generator does not")
            .And.Contain("set_primary_key");
        definitions
            .Single(d => d.Name == "add_relationship")
            .Description.Should()
            .Contain("two or more entries define a composite foreign key");
        definitions
            .Single(d => d.Name == "add_entity")
            .Description.Should()
            .Contain("exactly one primary key column");
    }

    /// <summary>OpenAI Function Calling 用の ChatTool 変換が、全ツールを名前付きで生成することを検証する</summary>
    [Fact(DisplayName = "ToOpenAiTools は全ツール定義を ChatTool へ変換する")]
    public void ToOpenAiTools_ConvertsAllDefinitions()
    {
        var definitions = ErDiagramToolCatalog.GetDefinitions();
        var tools = ChatToolConverter.ToOpenAiTools(definitions);

        tools.Should().HaveCount(definitions.Count);
        tools.Select(t => t.FunctionName).Should().BeEquivalentTo(definitions.Select(d => d.Name));
    }

    /// <summary>Anthropic (Claude) 用の Tool 変換が、全ツールを名前・説明・スキーマ付きで生成することを検証する</summary>
    [Fact(DisplayName = "ToAnthropicTools は全ツール定義を Anthropic Tool へ変換する")]
    public void ToAnthropicTools_ConvertsAllDefinitions()
    {
        var definitions = ErDiagramToolCatalog.GetDefinitions();
        var tools = ChatToolConverter.ToAnthropicTools(definitions);

        tools.Should().HaveCount(definitions.Count);
        tools.Select(t => t.Name).Should().BeEquivalentTo(definitions.Select(d => d.Name));
        tools.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.Description));

        // input_schema は object 型で、required・properties が元定義どおりに引き継がれること
        var addRelationship = tools.Single(t => t.Name == "add_relationship");
        addRelationship.InputSchema.Type.GetString().Should().Be("object");
        addRelationship
            .InputSchema.Required.Should()
            .BeEquivalentTo(["source_table", "target_table", "relationship_type"]);
        addRelationship.InputSchema.Properties.Should().ContainKey("source_table");
        addRelationship.InputSchema.Properties.Should().ContainKey("relationship_type");
    }
}
