using System.Text.Json;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Mcp;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockFolderDesignTools"/> のツール定義（英語・スキーマ形状・遅延ロード無効）を検証するテストクラス。
/// </summary>
public class MockFolderDesignToolsTests
{
    private static ToolDefinition Tool(string name) =>
        MockFolderDesignTools.GetDefinitions().Single(t => t.Name == name);

    /// <summary>4 ツールが揃い、すべて即時ロード（DeferLoading=false）であることを検証する</summary>
    [Fact(DisplayName = "4 ツールが揃い DeferLoading=false")]
    public void GetDefinitions_ContainsFourToolsImmediatelyLoaded()
    {
        var tools = MockFolderDesignTools.GetDefinitions();

        tools
            .Select(t => t.Name)
            .Should()
            .BeEquivalentTo(
                new[]
                {
                    MockFolderDesignTools.SaveScreenToolName,
                    MockFolderDesignTools.RemoveScreenToolName,
                    MockFolderDesignTools.SaveStylesheetToolName,
                    MockFolderDesignTools.GetScreenToolName,
                }
            );

        tools.Should().OnlyContain(t => t.DeferLoading == false);
    }

    /// <summary>ツール名定数が期待どおりであることを検証する</summary>
    [Fact(DisplayName = "ツール名定数が正しい")]
    public void ToolNameConstants_AreExpected()
    {
        MockFolderDesignTools.SaveScreenToolName.Should().Be("save_screen");
        MockFolderDesignTools.RemoveScreenToolName.Should().Be("remove_screen");
        MockFolderDesignTools.SaveStylesheetToolName.Should().Be("save_stylesheet");
        MockFolderDesignTools.GetScreenToolName.Should().Be("get_screen");
    }

    /// <summary>説明文が中立言語（英語・非 CJK）であることを検証する</summary>
    [Fact(DisplayName = "ツール説明は英語（非 CJK）")]
    public void Definitions_DescriptionsAreEnglish()
    {
        foreach (var tool in MockFolderDesignTools.GetDefinitions())
        {
            tool.Description.Should().NotBeNullOrWhiteSpace();
            tool.Description.Should().NotContainAny("画面", "保存", "スタイル");
        }
    }

    /// <summary>save_screen の入力スキーマ形状（必須項目・transitions 配列）を検証する</summary>
    [Fact(DisplayName = "save_screen のスキーマ形状（必須・transitions 配列）")]
    public void SaveScreen_SchemaShape_IsCorrect()
    {
        var json = SchemaJson(Tool(MockFolderDesignTools.SaveScreenToolName));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("type").GetString().Should().Be("object");

        var required = root.GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();
        required.Should().BeEquivalentTo(new[] { "file", "name", "html" });

        var properties = root.GetProperty("properties");
        properties.TryGetProperty("file", out _).Should().BeTrue();
        properties.TryGetProperty("html", out _).Should().BeTrue();
        properties.TryGetProperty("revision_note", out _).Should().BeTrue();

        // transitions は配列で、要素は to/trigger を持つオブジェクト
        var transitions = properties.GetProperty("transitions");
        transitions.GetProperty("type").GetString().Should().Be("array");
        var itemProps = transitions.GetProperty("items").GetProperty("properties");
        itemProps.TryGetProperty("to", out _).Should().BeTrue();
        itemProps.TryGetProperty("trigger", out _).Should().BeTrue();
    }

    /// <summary>save_screen の entities 引数（任意・配列・要素は name/operations）を検証する</summary>
    [Fact(DisplayName = "save_screen の entities スキーマ形状（任意・name/operations）")]
    public void SaveScreen_EntitiesSchemaShape_IsCorrect()
    {
        var tool = Tool(MockFolderDesignTools.SaveScreenToolName);

        using var doc = JsonDocument.Parse(SchemaJson(tool));
        var root = doc.RootElement;

        // entities は任意（required には入らない）
        var required = root.GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();
        required.Should().BeEquivalentTo(new[] { "file", "name", "html" });
        required.Should().NotContain("entities");

        var entities = root.GetProperty("properties").GetProperty("entities");
        entities.GetProperty("type").GetString().Should().Be("array");

        var itemProps = entities.GetProperty("items").GetProperty("properties");
        itemProps.TryGetProperty("name", out _).Should().BeTrue();
        itemProps.TryGetProperty("operations", out _).Should().BeTrue();

        // entities 要素の必須は name のみ
        entities
            .GetProperty("items")
            .GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString())
            .Should()
            .BeEquivalentTo(new[] { "name" });

        // upsert 意味論が英語で説明されている（omitted=維持 / empty=消去 / non-empty=置換）
        var entitiesDescription = entities.GetProperty("description").GetString();
        entitiesDescription.Should().NotBeNullOrWhiteSpace();
        entitiesDescription!.ToLowerInvariant().Should().ContainAll("omit", "empty", "replace");
    }

    /// <summary>save_stylesheet / get_screen / remove_screen の必須項目を検証する</summary>
    [Fact(DisplayName = "各ツールの必須項目が正しい")]
    public void OtherTools_RequiredFields_AreCorrect()
    {
        RequiredOf(MockFolderDesignTools.SaveStylesheetToolName)
            .Should()
            .BeEquivalentTo(new[] { "css" });
        RequiredOf(MockFolderDesignTools.GetScreenToolName)
            .Should()
            .BeEquivalentTo(new[] { "file" });
        RequiredOf(MockFolderDesignTools.RemoveScreenToolName)
            .Should()
            .BeEquivalentTo(new[] { "file" });
    }

    private static IReadOnlyList<string?> RequiredOf(string toolName)
    {
        using var doc = JsonDocument.Parse(SchemaJson(Tool(toolName)));

        return doc
            .RootElement.GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();
    }

    /// <summary>匿名型の InputSchema を JSON へ直列化する（プロパティ名は camel のまま）</summary>
    private static string SchemaJson(ToolDefinition tool) =>
        JsonSerializer.Serialize(tool.InputSchema);
}
