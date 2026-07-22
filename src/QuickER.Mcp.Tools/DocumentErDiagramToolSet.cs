using System.Text.Json;

namespace QuickER.Mcp.Tools;

/// <summary>
/// ファイルベースの ER 図操作ツール群を <see cref="McpToolSet"/> として組み立てるファクトリ。
/// 定義は <see cref="ErDiagramToolCatalog"/> の 9 ツール（GUI チャットと共有）＋ファイルモード専用の
/// <c>create_diagram</c> ・名前付きクエリ定義ツール（<c>set_query</c> / <c>list_queries</c> /
/// <c>remove_query</c>）に、<see cref="FileParameterInjector"/> で <c>file</c> 引数を注入したもの。
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

    /// <summary>
    /// 名前付きクエリ定義を upsert する <c>set_query</c> ツールの定義。QueryDefinition の全機能を
    /// ネスト構造で受ける。ファイルモード専用のためカタログ（GUI チャット共有）ではなく本プロジェクトに置く。
    /// </summary>
    public static ToolDefinition SetQueryDefinition { get; } =
        new()
        {
            Name = DocumentErDiagramToolHost.SetQueryToolName,
            Description =
                "Defines or replaces (upsert) a named query on a table. Queries become repository methods when C# code is generated. Matched by (table_name, query_name): if one already exists it is replaced wholesale (its id is preserved), otherwise it is added. Before saving, the definition is validated (structure, mini-DSL condition syntax and references, and raw-SQL static checks); on any error the file is left unchanged. An undeclared @parameter in raw SQL is an error (save refused); an unused or multi-statement SQL, or an unused DSL parameter, is a warning (save proceeds).",
            DeferLoading = false,
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    table_name = new
                    {
                        type = "string",
                        description = "Name of the table the query belongs to.",
                    },
                    query_name = new
                    {
                        type = "string",
                        description = "The meaningful part of the method name (e.g. GetByCustomer); an Async suffix is added at generation time.",
                    },
                    description = new
                    {
                        type = "string",
                        description = "Description of the query (emitted as the method's XML doc summary).",
                    },
                    returns = new
                    {
                        type = "string",
                        @enum = new[] { "list", "single", "count", "scalar", "projection" },
                        description = "Return shape: list of entities, single entity, row count, a scalar value, or a projection DTO list.",
                    },
                    scalar_type = new
                    {
                        type = "string",
                        description = "Required when returns=scalar. Dialect-neutral type token (e.g. decimal(12,2), int32).",
                    },
                    implementation = new
                    {
                        type = "string",
                        @enum = new[] { "dsl", "sql", "manual" },
                        description = "Implementation kind (default dsl): mini-DSL condition, raw per-dialect SQL, or manual (contract only; you write the body in a partial class).",
                    },
                    condition = new
                    {
                        type = "string",
                        description = "Mini-DSL search condition (used when implementation=dsl; omit for no filter). Supports comparisons, AND/OR/NOT, parentheses, IS [NOT] NULL, [NOT] LIKE, [NOT] IN @param, and CONTAINS/STARTSWITH/ENDSWITH; column names refer to the table's columns and @names refer to declared parameters.",
                    },
                    sql = new
                    {
                        type = "object",
                        description = "Per-dialect raw SQL (used when implementation=sql). Keys must be dialect names: sqlserver, postgresql, mysql, oracle, sqlite.",
                        additionalProperties = new { type = "string" },
                    },
                    parameters = new
                    {
                        type = "array",
                        description = "Method parameters. Each has a name, exactly one of type (a dialect-neutral token) or source_column (a column of this table, whose generated type is used), and optional is_list (IN parameter).",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                name = new { type = "string" },
                                type = new
                                {
                                    type = "string",
                                    description = "Dialect-neutral type token (e.g. int32). Mutually exclusive with source_column.",
                                },
                                source_column = new
                                {
                                    type = "string",
                                    description = "Column of this table to derive the parameter type from. Mutually exclusive with type.",
                                },
                                is_list = new
                                {
                                    type = "boolean",
                                    description = "Whether the parameter is a list (for IN conditions).",
                                },
                            },
                            required = new[] { "name" },
                        },
                    },
                    order_by = new
                    {
                        type = "array",
                        description = "Ordering (valid only when returns is list, single, or projection; with single it selects the first row). Each entry has a column (a column of this table) and optional descending.",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                column = new { type = "string" },
                                descending = new { type = "boolean" },
                            },
                            required = new[] { "column" },
                        },
                    },
                    paging = new
                    {
                        type = "boolean",
                        description = "Enable paging (adds take/skip parameters). Applies to list/projection.",
                    },
                    result_type_name = new
                    {
                        type = "string",
                        description = "DTO type name for the projection (required when returns=projection).",
                    },
                    fields = new
                    {
                        type = "array",
                        description = "Projection output fields (required when returns=projection). Each has a name, exactly one of type or source_column, and optional is_nullable.",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                name = new { type = "string" },
                                type = new
                                {
                                    type = "string",
                                    description = "Dialect-neutral type token. Mutually exclusive with source_column.",
                                },
                                source_column = new
                                {
                                    type = "string",
                                    description = "Column of this table to project. Mutually exclusive with type.",
                                },
                                is_nullable = new
                                {
                                    type = "boolean",
                                    description = "Override nullability of the generated DTO property (omit for automatic).",
                                },
                            },
                            required = new[] { "name" },
                        },
                    },
                },
                required = new[] { "table_name", "query_name", "returns" },
            },
        };

    /// <summary>図の名前付きクエリをエンティティ別に一覧する <c>list_queries</c> ツールの定義（読み取り系）</summary>
    public static ToolDefinition ListQueriesDefinition { get; } =
        new()
        {
            Name = DocumentErDiagramToolHost.ListQueriesToolName,
            Description =
                "Lists the named queries in the diagram, grouped by table, with each query's return shape, implementation kind, condition/SQL summary, and parameters.",
            DeferLoading = false,
            InputSchema = new { type = "object", properties = new { } },
        };

    /// <summary>テーブル名＋クエリ名で名前付きクエリを 1 件削除する <c>remove_query</c> ツールの定義</summary>
    public static ToolDefinition RemoveQueryDefinition { get; } =
        new()
        {
            Name = DocumentErDiagramToolHost.RemoveQueryToolName,
            Description =
                "Removes a single named query identified by table_name and query_name. Fails if no such query exists.",
            DeferLoading = false,
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    table_name = new
                    {
                        type = "string",
                        description = "Name of the table the query belongs to.",
                    },
                    query_name = new
                    {
                        type = "string",
                        description = "Name of the query to remove.",
                    },
                },
                required = new[] { "table_name", "query_name" },
            },
        };

    /// <summary>ファイルベースの ER 図操作ツールセットを生成する</summary>
    /// <returns>公開ツール定義（<c>file</c> 注入済み）と実行デリゲートを対にした <see cref="McpToolSet"/></returns>
    public static McpToolSet Create()
    {
        var definitions = ErDiagramToolCatalog
            .GetDefinitions()
            .Append(CreateDiagramDefinition)
            .Append(SetQueryDefinition)
            .Append(ListQueriesDefinition)
            .Append(RemoveQueryDefinition)
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
