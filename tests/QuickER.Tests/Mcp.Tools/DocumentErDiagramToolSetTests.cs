using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using QuickER.Mcp;
using QuickER.Mcp.Tools;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// <see cref="DocumentErDiagramToolSet"/> の組み立て（file 注入・ディスパッチ・file 欠落エラー）を検証する。
/// </summary>
public sealed class DocumentErDiagramToolSetTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "quicker-mcp-set-" + Guid.NewGuid().ToString("N")
    );

    public DocumentErDiagramToolSetTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // クリーンアップ失敗は無視
        }
    }

    [Fact(
        DisplayName = "Create はカタログ 12 ツール（クエリ定義 3 含む）＋create_diagram を公開する"
    )]
    public void Create_ExposesAllTools()
    {
        var toolSet = DocumentErDiagramToolSet.Create();

        // 名前付きクエリ 3 ツール（set_query / list_queries / remove_query）はカタログへ統合済みのため、
        // 公開集合はカタログ全体＋ファイルモード専用の create_diagram で構成される。
        var expected = ErDiagramToolCatalog
            .GetDefinitions()
            .Select(d => d.Name)
            .Append(DocumentErDiagramToolHost.CreateDiagramToolName)
            .ToList();

        toolSet.Tools.Select(t => t.Name).Should().BeEquivalentTo(expected);

        // クエリ定義 3 ツールがカタログ経由で公開されていることを明示的に確認する
        toolSet
            .Tools.Select(t => t.Name)
            .Should()
            .Contain(
                new[]
                {
                    DocumentErDiagramToolHost.SetQueryToolName,
                    DocumentErDiagramToolHost.ListQueriesToolName,
                    DocumentErDiagramToolHost.RemoveQueryToolName,
                }
            );
    }

    [Fact(DisplayName = "全ツール定義に file パラメータが注入されている")]
    public void Create_InjectsFileParameterIntoEveryTool()
    {
        var toolSet = DocumentErDiagramToolSet.Create();

        foreach (var tool in toolSet.Tools)
        {
            var schema =
                JsonSerializer.SerializeToNode(tool.InputSchema) as JsonObject
                ?? throw new InvalidOperationException("schema is not an object");

            var properties = schema["properties"] as JsonObject;
            properties.Should().NotBeNull($"{tool.Name} must expose properties");
            properties!.ContainsKey("file").Should().BeTrue($"{tool.Name} must expose file");

            var required = schema["required"] as JsonArray;
            required.Should().NotBeNull($"{tool.Name} must declare required");
            required!
                .Select(n => n!.GetValue<string>())
                .Should()
                .Contain("file", $"{tool.Name} must require file");
        }
    }

    [Fact(DisplayName = "Execute は file 引数が無い場合エラーを返す")]
    public void Execute_MissingFile_ReturnsError()
    {
        var toolSet = DocumentErDiagramToolSet.Create();

        var (result, success) = toolSet.Execute(
            "add_entity",
            JsonSerializer.Serialize(new { table_name = "X" })
        );

        success.Should().BeFalse();
        result.Should().Contain("file");
    }

    [Fact(DisplayName = "Execute は file を取り出してホストへディスパッチする")]
    public void Execute_DispatchesToHost()
    {
        var toolSet = DocumentErDiagramToolSet.Create();
        var file = Path.Combine(_dir, "dispatched.json");

        var (result, success) = toolSet.Execute(
            DocumentErDiagramToolHost.CreateDiagramToolName,
            JsonSerializer.Serialize(new { file, target_dbms = "sqlite" })
        );

        success.Should().BeTrue();
        result.Should().Contain("sqlite");
        File.Exists(file).Should().BeTrue();
    }

    [Fact(DisplayName = "Execute は不正な引数 JSON をエラーにする")]
    public void Execute_InvalidJson_ReturnsError()
    {
        var toolSet = DocumentErDiagramToolSet.Create();

        var (_, success) = toolSet.Execute("add_entity", "not json");

        success.Should().BeFalse();
    }
}
