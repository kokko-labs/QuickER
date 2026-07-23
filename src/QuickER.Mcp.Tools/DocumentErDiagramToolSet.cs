using System.Text.Json;

namespace QuickER.Mcp.Tools;

/// <summary>
/// ファイルベースの ER 図操作ツール群を <see cref="McpToolSet"/> として組み立てるファクトリ。
/// 定義は <see cref="ErDiagramToolCatalog"/> の 12 ツール（GUI チャットと共有＝ER 図操作 9＋名前付きクエリ
/// <c>set_query</c> / <c>list_queries</c> / <c>remove_query</c>）＋ファイルモード専用の <c>create_diagram</c> に、
/// <see cref="FileParameterInjector"/> で <c>file</c> 引数を注入したもの。
/// 実行は引数 JSON から <c>file</c> を取り出して <see cref="DocumentErDiagramToolHost"/> へ委譲する。
/// </summary>
public static class DocumentErDiagramToolSet
{
    /// <summary>注入される <c>file</c> パラメータ名</summary>
    private const string FileParameterName = "file";

    /// <summary>
    /// 新規図を作成する <c>create_diagram</c> ツールの定義。GUI 常駐図を持たないファイルモード専用のため
    /// カタログ（GUI チャット共有）ではなく本プロジェクトに置く。<c>file</c> は他ツール同様に注入で付与する。
    /// </summary>
    public static ToolDefinition CreateDiagramDefinition { get; } =
        new()
        {
            Name = DocumentErDiagramToolHost.CreateDiagramToolName,
            Description =
                "Creates a new, empty ER diagram file for the given target DBMS. Fails if the file already exists (this tool only creates new diagrams; use the other tools to modify an existing one). The new diagram has no layout, so opening it in the GUI auto-arranges all tables.",
            DeferLoading = false,
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    target_dbms = new
                    {
                        type = "string",
                        @enum = new[] { "sqlserver", "postgresql", "mysql", "oracle", "sqlite" },
                        description = "Target DBMS (database dialect) of the new diagram.",
                    },
                },
                required = new[] { "target_dbms" },
            },
        };

    /// <summary>ファイルベースの ER 図操作ツールセットを生成する</summary>
    /// <returns>公開ツール定義（<c>file</c> 注入済み）と実行デリゲートを対にした <see cref="McpToolSet"/></returns>
    public static McpToolSet Create()
    {
        // 名前付きクエリ 3 ツールはカタログ（GUI チャット共有）へ統合済み。ファイルモード専用の
        // create_diagram だけを追加し、全ツールへ file 引数を注入する。
        var definitions = ErDiagramToolCatalog
            .GetDefinitions()
            .Append(CreateDiagramDefinition)
            .Select(FileParameterInjector.Inject)
            .ToList();

        return new McpToolSet(definitions, Dispatch);
    }

    /// <summary>ツール名・引数 JSON を受け取り、<c>file</c> を取り出してホストへディスパッチする</summary>
    private static (string Result, bool Success) Dispatch(string toolName, string argumentsJson)
    {
        JsonElement args;

        try
        {
            args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
        }
        catch (JsonException ex)
        {
            return ($"Invalid tool arguments (not valid JSON): {ex.Message}", false);
        }

        if (
            args.ValueKind != JsonValueKind.Object
            || !args.TryGetProperty(FileParameterName, out var fileEl)
            || fileEl.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(fileEl.GetString())
        )
        {
            return (
                $"The '{FileParameterName}' argument (path to the diagram JSON file) is required.",
                false
            );
        }

        return DocumentErDiagramToolHost.Execute(toolName, fileEl.GetString()!, args);
    }
}
