using System.Text.Json;
using Anthropic.Models.Messages;
using FluentAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="ChatToolConverter"/> の中立ツール定義（<see cref="CodexDynamicToolDefinition"/>）→
/// 各 LLM SDK 形式（OpenAI <see cref="OpenAI.Chat.ChatTool"/> / Anthropic <see cref="Tool"/>）変換を
/// 検証するテストクラス。変換結果の構造（名前・説明・パラメータスキーマの JSON 形）を直接確認する。
/// </summary>
/// <remarks>
/// ER 図操作ツールの具体定義（QuickER.AI.Chat 側）ではなく、テスト内で組み立てた小さな JSON Schema で
/// 変換ロジックそのものを検証する（純粋な形式変換であることを利用）。
/// </remarks>
public class ChatToolConverterTests
{
    /// <summary>
    /// テスト用の中立ツール定義を組み立てる。InputSchema は object 型で保持されるが、
    /// System.Text.Json は「ルート値の実行時型」で直列化するため、匿名型ツリーは全プロパティが正しく出力される
    /// （途中の object 化を避けるため入れ子も匿名型で構築している）。
    /// </summary>
    private static CodexDynamicToolDefinition MakeDefinition() =>
        new()
        {
            Name = "add_entity",
            Description = "エンティティを 1 つ追加する",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    table_name = new { type = "string", description = "テーブル名" },
                    column_count = new { type = "integer" },
                },
                required = new[] { "table_name" },
            },
        };

    /// <summary>OpenAI 変換で name / description / パラメータスキーマが正しく写ることを検証する</summary>
    [Fact(DisplayName = "ToOpenAiTools は name/description/パラメータスキーマを写す")]
    public void ToOpenAiTools_MapsNameDescriptionAndParameters()
    {
        var definitions = new List<CodexDynamicToolDefinition> { MakeDefinition() };

        var tools = ChatToolConverter.ToOpenAiTools(definitions);

        tools.Should().ContainSingle();

        var tool = tools[0];
        tool.FunctionName.Should().Be("add_entity");
        tool.FunctionDescription.Should().Be("エンティティを 1 つ追加する");

        // FunctionParameters は InputSchema を直列化した BinaryData なので、JSON として構造を確認する
        using var document = JsonDocument.Parse(tool.FunctionParameters.ToString());
        var root = document.RootElement;

        root.GetProperty("type").GetString().Should().Be("object");
        root.GetProperty("properties")
            .GetProperty("table_name")
            .GetProperty("type")
            .GetString()
            .Should()
            .Be("string");
        root.GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString())
            .Should()
            .Equal("table_name");
    }

    /// <summary>複数定義が順序を保って 1 対 1 で OpenAI ツールへ変換されることを検証する</summary>
    [Fact(DisplayName = "ToOpenAiTools は複数定義を順序保持で変換する")]
    public void ToOpenAiTools_PreservesOrderAndCount()
    {
        var definitions = new List<CodexDynamicToolDefinition>
        {
            new()
            {
                Name = "first",
                Description = "1 番目",
                InputSchema = new { type = "object" },
            },
            new()
            {
                Name = "second",
                Description = "2 番目",
                InputSchema = new { type = "object" },
            },
        };

        var tools = ChatToolConverter.ToOpenAiTools(definitions);

        tools.Select(t => t.FunctionName).Should().Equal("first", "second");
    }

    /// <summary>Anthropic 変換で name/description と properties/required が正しく写ることを検証する</summary>
    [Fact(DisplayName = "ToAnthropicTools は name/description と properties/required を写す")]
    public void ToAnthropicTools_MapsNameDescriptionAndSchema()
    {
        var definitions = new List<CodexDynamicToolDefinition> { MakeDefinition() };

        var tools = ChatToolConverter.ToAnthropicTools(definitions);

        tools.Should().ContainSingle();

        var tool = tools[0];
        tool.Name.Should().Be("add_entity");
        tool.Description.Should().Be("エンティティを 1 つ追加する");

        // properties は元スキーマの各プロパティが値ごとクローンされて入る
        tool.InputSchema.Properties.Should().ContainKey("table_name");
        tool.InputSchema.Properties.Should().ContainKey("column_count");
        tool.InputSchema.Properties!["table_name"]
            .GetProperty("type")
            .GetString()
            .Should()
            .Be("string");

        // required は文字列要素のみが取り込まれる
        tool.InputSchema.Required.Should().Equal("table_name");
    }

    /// <summary>
    /// properties / required を持たない最小スキーマでは、Anthropic 変換の properties が空・required が空になることを検証する
    /// （TryGetProperty が false になる分岐のカバー）。
    /// </summary>
    [Fact(DisplayName = "ToAnthropicTools はスキーマ欠落時に空 properties/required を生成する")]
    public void ToAnthropicTools_MissingPropertiesAndRequired_ProducesEmpty()
    {
        var definitions = new List<CodexDynamicToolDefinition>
        {
            new()
            {
                Name = "noop",
                Description = "引数なし",
                InputSchema = new { type = "object" },
            },
        };

        var tools = ChatToolConverter.ToAnthropicTools(definitions);

        var tool = tools[0];
        tool.InputSchema.Properties.Should().BeEmpty();
        tool.InputSchema.Required.Should().BeEmpty();
    }

    /// <summary>空の定義一覧では空のツール一覧が返ることを検証する（両変換）</summary>
    [Fact(DisplayName = "空の定義一覧は空のツール一覧になる")]
    public void EmptyDefinitions_ProduceEmptyToolLists()
    {
        var empty = new List<CodexDynamicToolDefinition>();

        ChatToolConverter.ToOpenAiTools(empty).Should().BeEmpty();
        ChatToolConverter.ToAnthropicTools(empty).Should().BeEmpty();
    }
}
