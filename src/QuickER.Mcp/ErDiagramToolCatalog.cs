namespace QuickER.Mcp;

/// <summary>
/// ER 図操作ツールの定義（スキーマ）の正本。エンティティ・カラム・リレーションの追加／削除／変更・
/// 図の要約取得（9 ツール）に加え、名前付きクエリの定義／一覧／削除（set_query / list_queries /
/// remove_query）を含む 12 ツールを、外部 AI エージェント向けの中立言語（英語）で記述する。
/// </summary>
/// <remarks>
/// 実行（VM 操作）は app 側が担う。各 LLM SDK 形式（OpenAI / Anthropic）への変換は
/// AI 層の <c>QuickER.AI.ChatToolConverter</c> が本カタログの定義を用いて行う。
/// </remarks>
public static class ErDiagramToolCatalog
{
    /// <summary>全 ER 図操作ツールの定義一覧を返す</summary>
    public static IReadOnlyList<ToolDefinition> GetDefinitions()
    {
        return
        [
            new ToolDefinition
            {
                Name = "get_diagram_summary",
                Description =
                    "Returns a text listing of the entities (tables) and relationships (foreign keys) in the current ER diagram.",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>(),
                },
            },
            new ToolDefinition
            {
                Name = "add_entity",
                Description =
                    "Adds a new entity (table) to the ER diagram. No columns are created, so after adding, first define exactly one primary key column with add_column, then define the remaining columns.",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        table_name = new { type = "string", description = "Table name" },
                        description = new
                        {
                            type = "string",
                            description = "Table description (optional)",
                        },
                    },
                    required = new[] { "table_name" },
                },
            },
            new ToolDefinition
            {
                Name = "remove_entity",
                Description =
                    "Removes the entity with the specified table name from the ER diagram.",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        table_name = new
                        {
                            type = "string",
                            description = "Name of the table to remove",
                        },
                    },
                    required = new[] { "table_name" },
                },
            },
            new ToolDefinition
            {
                Name = "add_column",
                Description =
                    "Adds a column to the specified table. Each table has exactly one primary key column (composite primary keys are not allowed). If you need multiple key-like columns, add the second and later ones with is_primary_key=false and define the references with add_relationship.",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        table_name = new { type = "string", description = "Table name" },
                        column_name = new { type = "string", description = "Column name" },
                        data_type = new
                        {
                            type = "string",
                            description = "Data type (e.g., int, nvarchar(100), datetime2)",
                        },
                        is_primary_key = new
                        {
                            type = "boolean",
                            description = "Whether the column is a primary key",
                        },
                        is_nullable = new
                        {
                            type = "boolean",
                            description = "Whether NULL is allowed",
                        },
                        description = new
                        {
                            type = "string",
                            description = "Column description (optional)",
                        },
                    },
                    required = new[] { "table_name", "column_name", "data_type" },
                },
            },
            new ToolDefinition
            {
                Name = "remove_column",
                Description = "Removes a column from the specified table.",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        table_name = new { type = "string", description = "Table name" },
                        column_name = new
                        {
                            type = "string",
                            description = "Name of the column to remove",
                        },
                    },
                    required = new[] { "table_name", "column_name" },
                },
            },
            new ToolDefinition
            {
                Name = "set_entity_property",
                Description = "Changes an entity's properties (table name, memo, description).",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        table_name = new
                        {
                            type = "string",
                            description = "Name of the table to change",
                        },
                        new_table_name = new
                        {
                            type = "string",
                            description = "New table name (if renaming)",
                        },
                        memo = new { type = "string", description = "Memo (if changing)" },
                        description = new
                        {
                            type = "string",
                            description = "Description (if changing)",
                        },
                    },
                    required = new[] { "table_name" },
                },
            },
            new ToolDefinition
            {
                Name = "set_column_property",
                Description =
                    "Changes a column's properties (description, data type, nullability) in the specified table. Specify at least one of description, data_type, or is_nullable.",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        table_name = new
                        {
                            type = "string",
                            description = "Name of the table to change",
                        },
                        column_name = new
                        {
                            type = "string",
                            description = "Name of the column to change",
                        },
                        description = new
                        {
                            type = "string",
                            description = "Column description (specify to change)",
                        },
                        data_type = new
                        {
                            type = "string",
                            description = "Data type (specify to change; e.g., int, nvarchar(100), datetime2)",
                        },
                        is_nullable = new
                        {
                            type = "boolean",
                            description = "Nullability flag (specify to change)",
                        },
                    },
                    required = new[] { "table_name", "column_name" },
                },
            },
            new ToolDefinition
            {
                Name = "add_relationship",
                Description =
                    "Adds a relationship (foreign key) between two tables. To specify the referenced columns reliably, also provide source_column (the primary key column name of the parent table) and target_column (the foreign key column name of the child table). A relationship references exactly one column to one column (composite foreign keys are not allowed). Add multiple foreign keys with different roles as separate relationships.",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        source_table = new
                        {
                            type = "string",
                            description = "Referenced (parent) table name",
                        },
                        source_column = new
                        {
                            type = "string",
                            description = "Referenced column name in the parent table. Usually the primary key column (if omitted, the primary key column is used).",
                        },
                        target_table = new
                        {
                            type = "string",
                            description = "Referencing (child) table name",
                        },
                        target_column = new
                        {
                            type = "string",
                            description = "Foreign key column name in the child table (if omitted, it is inferred from the column names, and left unassigned if it cannot be inferred).",
                        },
                        relationship_type = new
                        {
                            type = "string",
                            @enum = new[] { "OneToOne", "OneToMany", "ManyToMany" },
                            description = "Relationship type",
                        },
                    },
                    required = new[] { "source_table", "target_table", "relationship_type" },
                },
            },
            new ToolDefinition
            {
                Name = "remove_relationship",
                Description = "Removes the relationship between two tables.",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        source_table = new
                        {
                            type = "string",
                            description = "Referenced table name",
                        },
                        target_table = new
                        {
                            type = "string",
                            description = "Referencing table name",
                        },
                    },
                    required = new[] { "source_table", "target_table" },
                },
            },
            new ToolDefinition
            {
                Name = "set_query",
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
            },
            new ToolDefinition
            {
                Name = "list_queries",
                Description =
                    "Lists the named queries in the diagram, grouped by table, with each query's return shape, implementation kind, condition/SQL summary, and parameters.",
                DeferLoading = false,
                InputSchema = new { type = "object", properties = new { } },
            },
            new ToolDefinition
            {
                Name = "remove_query",
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
            },
        ];
    }
}
