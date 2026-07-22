using System.Text.Json;
using FluentAssertions;
using QuickER.Mcp;

namespace QuickER.Tests.Mcp;

/// <summary>
/// <see cref="FileParameterInjector"/> がツール定義の入力スキーマへ <c>file</c> パラメータを
/// 追加すること・既存プロパティを温存すること・元定義を破壊しないことを検証するテストクラス。
/// </summary>
public sealed class FileParameterInjectorTests
{
    /// <summary>既存プロパティと required を持つツール定義を組み立てる</summary>
    private static ToolDefinition MakeDefinition() =>
        new()
        {
            Name = "add_entity",
            Description = "Adds a new entity to the diagram.",
            DeferLoading = false,
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    table_name = new { type = "string", description = "Table name" },
                },
                required = new[] { "table_name" },
            },
        };

    /// <summary>注入後のスキーマを JsonDocument として解析する</summary>
    private static JsonElement SchemaOf(ToolDefinition definition)
    {
        var json = JsonSerializer.Serialize(definition.InputSchema);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>file プロパティが type:string・所定の説明付きで properties へ追加されることを検証する</summary>
    [Fact(DisplayName = "Inject は file プロパティ（string）を properties へ追加する")]
    public void Inject_AddsFilePropertyToProperties()
    {
        var injected = FileParameterInjector.Inject(MakeDefinition());

        var schema = SchemaOf(injected);
        var file = schema.GetProperty("properties").GetProperty("file");
        file.GetProperty("type").GetString().Should().Be("string");
        file.GetProperty("description")
            .GetString()
            .Should()
            .Be("Path to the diagram JSON file (DiagramDocument format).");
    }

    /// <summary>file が required 配列へ追加され、既存の required 要素が保持されることを検証する</summary>
    [Fact(DisplayName = "Inject は file を required へ追加し既存 required を保持する")]
    public void Inject_AddsFileToRequiredAndKeepsExisting()
    {
        var injected = FileParameterInjector.Inject(MakeDefinition());

        var required = SchemaOf(injected)
            .GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        required.Should().Contain("table_name");
        required.Should().Contain("file");
    }

    /// <summary>既存プロパティ（table_name）が注入後も温存されることを検証する</summary>
    [Fact(DisplayName = "Inject は既存プロパティを温存する")]
    public void Inject_PreservesExistingProperties()
    {
        var injected = FileParameterInjector.Inject(MakeDefinition());

        var properties = SchemaOf(injected).GetProperty("properties");
        properties.TryGetProperty("table_name", out var tableName).Should().BeTrue();
        tableName.GetProperty("type").GetString().Should().Be("string");
    }

    /// <summary>ツール名・説明・DeferLoading が引き継がれることを検証する</summary>
    [Fact(DisplayName = "Inject は名前・説明・DeferLoading を引き継ぐ")]
    public void Inject_CarriesOverScalarMetadata()
    {
        var original = MakeDefinition();

        var injected = FileParameterInjector.Inject(original);

        injected.Name.Should().Be(original.Name);
        injected.Description.Should().Be(original.Description);
        injected.DeferLoading.Should().Be(original.DeferLoading);
    }

    /// <summary>元のツール定義が変更されない（非破壊）ことを検証する</summary>
    [Fact(DisplayName = "Inject は元定義を破壊しない")]
    public void Inject_DoesNotMutateOriginal()
    {
        var original = MakeDefinition();

        FileParameterInjector.Inject(original);

        // 元定義の InputSchema には file が現れない
        var originalSchema = SchemaOf(original);
        originalSchema
            .GetProperty("properties")
            .TryGetProperty("file", out _)
            .Should()
            .BeFalse("元定義のスキーマは注入で変更されてはならない");
        originalSchema
            .GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString())
            .Should()
            .NotContain("file");
    }

    /// <summary>properties / required を持たない最小スキーマでも file が追加されることを検証する</summary>
    [Fact(DisplayName = "Inject は properties/required 欠落スキーマにも file を追加する")]
    public void Inject_AddsFileWhenSchemaLacksPropertiesAndRequired()
    {
        var definition = new ToolDefinition
        {
            Name = "noop",
            Description = "No arguments.",
            InputSchema = new { type = "object" },
        };

        var injected = FileParameterInjector.Inject(definition);

        var schema = SchemaOf(injected);
        schema
            .GetProperty("properties")
            .GetProperty("file")
            .GetProperty("type")
            .GetString()
            .Should()
            .Be("string");
        schema
            .GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString())
            .Should()
            .Contain("file");
    }
}
