namespace QuickER.Mcp;

/// <summary>
/// ER 図操作ツールの定義（スキーマ）の正本。エンティティ・カラム・リレーションの追加／削除／変更と
/// 図の要約取得を提供する 9 ツールを、外部 AI エージェント向けの中立言語（英語）で記述する。
/// </summary>
/// <remarks>
/// 実行（VM 操作）は app 側が担う。各 LLM SDK 形式（OpenAI / Anthropic）への変換は
/// 機能側 <c>QuickER.AI.Chat.ErDiagramToolDefinitions</c> が本カタログの定義を用いて行う。
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
        ];
    }
}
