using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickER.Documents;
using QuickER.Model;

namespace QuickER.Mcp.Tools;

/// <summary>
/// ファイルベースの ER 図ツール実行ホスト。各ツール呼び出しを
/// 「<see cref="JsonStorageService.Load"/>（DiagramDocument 読込）→ 意味モデル（<see cref="ErDiagram"/>）変更 →
/// <see cref="JsonStorageService.Save"/>（保存）」で完結させるステートレスな実行器。
/// </summary>
/// <remarks>
/// 意味論は GUI 側 <c>QuickER.Services.ErDiagramDynamicTools</c>（ViewModel 操作）を忠実にミラーするが、
/// 対象は ViewModel ではなく <see cref="ErDiagram"/> / <see cref="Entity"/> / <see cref="Column"/> /
/// <see cref="Relationship"/>（意味モデル）。Undo/Redo は概念ごと持たない。結果テキストはすべて英語。
/// レイアウト（<see cref="DiagramDocument.Layout"/>）は原則温存し、削除エンティティの孤児レイアウトのみ除去する。
/// </remarks>
public static partial class DocumentErDiagramToolHost
{
    /// <summary>作成専用ツール（既存ファイルを変更しない）の名前</summary>
    public const string CreateDiagramToolName = "create_diagram";

    /// <summary>読み取り系ツール（新しいフォーマットでも警告付きで続行する）の名前</summary>
    private const string GetSummaryToolName = "get_diagram_summary";

    /// <summary><c>target_dbms</c> に指定できるプロバイダ識別名（tool 定義の enum と一致させる）</summary>
    private static readonly string[] SupportedDbms =
    [
        "sqlserver",
        "postgresql",
        "mysql",
        "oracle",
        "sqlite",
    ];

    /// <summary>ツール名でディスパッチし、対象ファイルに対してツールを実行する</summary>
    /// <param name="toolName">ツール名</param>
    /// <param name="file">対象図の JSON ファイルパス</param>
    /// <param name="arguments">ツール固有の引数（<c>file</c> を含んでいてもよい・未使用）</param>
    /// <returns>実行結果テキスト（英語）と成否のタプル</returns>
    public static (string Result, bool Success) Execute(
        string toolName,
        string file,
        JsonElement arguments
    )
    {
        try
        {
            return toolName switch
            {
                CreateDiagramToolName => CreateDiagram(file, arguments),
                GetSummaryToolName => GetDiagramSummary(file),
                ListQueriesToolName => ListQueries(file),
                SetQueryToolName => Mutate(file, doc => SetQuery(doc, arguments)),
                RemoveQueryToolName => Mutate(file, doc => RemoveQuery(doc, arguments)),
                "add_entity" => Mutate(file, doc => AddEntity(doc, arguments)),
                "remove_entity" => Mutate(file, doc => RemoveEntity(doc, arguments)),
                "add_column" => Mutate(file, doc => AddColumn(doc, arguments)),
                "remove_column" => Mutate(file, doc => RemoveColumn(doc, arguments)),
                "set_entity_property" => Mutate(file, doc => SetEntityProperty(doc, arguments)),
                "set_column_property" => Mutate(file, doc => SetColumnProperty(doc, arguments)),
                "add_relationship" => Mutate(file, doc => AddRelationship(doc, arguments)),
                "remove_relationship" => Mutate(file, doc => RemoveRelationship(doc, arguments)),
                _ => ($"Unsupported tool: {toolName}", false),
            };
        }
        catch (Exception ex)
        {
            // ツール実行中の例外はエラーテキストとして返し、プロセスを落とさない（GUI 側と同方針）
            return ($"Error: {ex.Message}", false);
        }
    }

    // ---------------- load / save orchestration ----------------

    /// <summary>変更系ツールの共通処理。読込・ガードを行い、変更が成功したときのみ保存する</summary>
    /// <param name="file">対象ファイル</param>
    /// <param name="operation">読み込んだ文書を変更し結果を返す処理（成功時のみ保存される）</param>
    private static (string, bool) Mutate(
        string file,
        Func<DiagramDocument, (string Result, bool Success)> operation
    )
    {
        var (document, error) = LoadForMutation(file);

        if (error is not null)
        {
            return (error, false);
        }

        var (result, success) = operation(document!);

        if (success)
        {
            JsonStorageService.Save(file, document!);
        }

        return (result, success);
    }

    /// <summary>変更系ツール向けに文書を読み込む（不在・非 DiagramDocument・新フォーマットは拒否）</summary>
    private static (DiagramDocument? Document, string? Error) LoadForMutation(string file)
    {
        if (!File.Exists(file))
        {
            return (
                null,
                $"Diagram file not found: {file}. To create a new diagram, call {CreateDiagramToolName} first."
            );
        }

        var (document, error) = TryReadDocument(file);

        if (error is not null)
        {
            return (null, error);
        }

        if (document!.IsNewerFormat)
        {
            return (
                null,
                $"Diagram file '{file}' was saved in a newer format (version {document.Version} > supported {DiagramDocument.CurrentVersion}); refusing to modify to avoid discarding unknown data."
            );
        }

        return (document, null);
    }

    /// <summary>ファイルを読み、DiagramDocument として妥当か検証したうえで読み込む</summary>
    /// <remarks>
    /// <see cref="JsonStorageService.Load"/> は既定値を補うため、無関係な JSON も「空図」として
    /// 読めてしまう。上書き・誤解釈を防ぐため、ルートが JSON オブジェクトで <c>Version</c>・<c>Schema</c>
    /// キーを持つことを（<see cref="JsonStorageService"/> の読込仕様に合わせ大文字小文字を区別して）検証する。
    /// </remarks>
    private static (DiagramDocument? Document, string? Error) TryReadDocument(string file)
    {
        string text;

        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception ex)
        {
            return (null, $"Failed to read diagram file '{file}': {ex.Message}");
        }

        JsonNode? root;

        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            return (null, $"Diagram file '{file}' is not valid JSON: {ex.Message}");
        }

        if (root is not JsonObject obj || obj["Version"] is null || obj["Schema"] is not JsonObject)
        {
            return (
                null,
                $"Diagram file '{file}' is not a DiagramDocument (expected an object with 'Version' and 'Schema'). Refusing to treat unrelated JSON as a diagram."
            );
        }

        return (JsonStorageService.Load(file), null);
    }

    // ---------------- create_diagram ----------------

    /// <summary>新規のスキーマのみ文書（レイアウトなし）を作成する。既存ファイルは上書きしない</summary>
    private static (string, bool) CreateDiagram(string file, JsonElement args)
    {
        var dbmsInput = GetString(args, "target_dbms");

        if (string.IsNullOrWhiteSpace(dbmsInput))
        {
            return (
                $"target_dbms is required (one of: {string.Join(", ", SupportedDbms)}).",
                false
            );
        }

        var dbms = SupportedDbms.FirstOrDefault(d =>
            string.Equals(d, dbmsInput, StringComparison.OrdinalIgnoreCase)
        );

        if (dbms is null)
        {
            return (
                $"Invalid target_dbms '{dbmsInput}'. Must be one of: {string.Join(", ", SupportedDbms)}.",
                false
            );
        }

        if (File.Exists(file))
        {
            return (
                $"Diagram file already exists: {file}. {CreateDiagramToolName} only creates new files; use the other tools to modify an existing diagram.",
                false
            );
        }

        // 親ディレクトリを黙って掘らない（存在しなければエラー）
        var directory = Path.GetDirectoryName(file);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            return (
                $"Parent directory does not exist: {directory}. Create the directory first.",
                false
            );
        }

        // Layout=null によりスキーマのみ文書となり、GUI で開くと全体自動整列される
        var document = new DiagramDocument
        {
            Version = DiagramDocument.CurrentVersion,
            Schema = new ErDiagram { TargetDbms = dbms },
            Layout = null,
        };
        JsonStorageService.Save(file, document);

        return ($"Created diagram '{file}' (target DBMS: {dbms}).", true);
    }

    // ---------------- get_diagram_summary ----------------

    /// <summary>ER 図の概要（テーブル・カラム・リレーション）を英語テキスト化する</summary>
    private static (string, bool) GetDiagramSummary(string file)
    {
        if (!File.Exists(file))
        {
            return (
                $"Diagram file not found: {file}. To create a new diagram, call {CreateDiagramToolName} first.",
                false
            );
        }

        var (document, error) = TryReadDocument(file);

        if (error is not null)
        {
            return (error, false);
        }

        var sb = new StringBuilder();

        // 読み取り系は新フォーマットでも警告付きで続行する（CLI と同方針）
        if (document!.IsNewerFormat)
        {
            sb.AppendLine(
                $"Warning: this diagram was saved in a newer format (version {document.Version} > supported {DiagramDocument.CurrentVersion}); unknown data may be omitted. Showing a best-effort summary."
            );
            sb.AppendLine();
        }

        var schema = document.Schema;
        sb.AppendLine($"Tables: {schema.Entities.Count}");
        sb.AppendLine($"Relationships: {schema.Relationships.Count}");
        sb.AppendLine();

        foreach (var entity in schema.Entities)
        {
            sb.AppendLine($"[{entity.TableName}]");

            if (!string.IsNullOrWhiteSpace(entity.Description))
            {
                sb.AppendLine($"  Description: {entity.Description}");
            }

            foreach (var col in entity.Columns)
            {
                var flags = new List<string>();

                if (col.IsPrimaryKey)
                {
                    flags.Add("PK");
                }

                if (col.IsForeignKey)
                {
                    flags.Add("FK");
                }

                if (!col.IsNullable)
                {
                    flags.Add("NOT NULL");
                }

                var flagsText = flags.Count > 0 ? $" ({string.Join(", ", flags)})" : string.Empty;
                var colDesc = !string.IsNullOrWhiteSpace(col.Description)
                    ? $" // {col.Description}"
                    : string.Empty;
                sb.AppendLine($"  - {col.Name}: {col.DataType}{flagsText}{colDesc}");
            }
        }

        if (schema.Relationships.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Relationships:");

            foreach (var rel in schema.Relationships)
            {
                var source = FindEntityById(schema, rel.SourceEntityId);
                var target = FindEntityById(schema, rel.TargetEntityId);
                var sourceName = source?.TableName ?? "(unknown)";
                var targetName = target?.TableName ?? "(unknown)";
                sb.AppendLine($"  {sourceName} → {targetName} ({rel.Type})");
            }
        }

        return (sb.ToString(), true);
    }

    // ---------------- entity / column / relationship operations ----------------

    /// <summary>エンティティを追加する（列は自動生成しない・レイアウトも作らない）</summary>
    private static (string, bool) AddEntity(DiagramDocument document, JsonElement args)
    {
        var tableName = GetString(args, "table_name") ?? "NewTable";
        var desc = GetString(args, "description") ?? string.Empty;

        document.Schema.Entities.Add(new Entity { TableName = tableName, Description = desc });

        return ($"Added table '{tableName}'.", true);
    }

    /// <summary>指定テーブル名のエンティティと、接続する全リレーション・孤児レイアウトを削除する</summary>
    private static (string, bool) RemoveEntity(DiagramDocument document, JsonElement args)
    {
        var tableName = GetString(args, "table_name");

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return ("table_name is required.", false);
        }

        var schema = document.Schema;
        var entity = FindEntity(schema, tableName);

        if (entity is null)
        {
            return ($"Table '{tableName}' not found.", false);
        }

        // 削除エンティティを端点に持つリレーションも併せて除去する（孤立した線を残さない）
        schema.Relationships.RemoveAll(r =>
            r.SourceEntityId == entity.Id || r.TargetEntityId == entity.Id
        );
        schema.Entities.Remove(entity);

        // 孤児になったレイアウトエントリを削除する（ファイルを清潔に保つ）
        document.Layout?.Remove(entity.Id);

        return ($"Removed table '{tableName}'.", true);
    }

    /// <summary>指定テーブルへカラムを追加する</summary>
    private static (string, bool) AddColumn(DiagramDocument document, JsonElement args)
    {
        var tableName = GetString(args, "table_name");
        var columnName = GetString(args, "column_name");
        var dataType = GetString(args, "data_type") ?? "nvarchar(100)";

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
        {
            return ("table_name and column_name are required.", false);
        }

        var entity = FindEntity(document.Schema, tableName);

        if (entity is null)
        {
            return ($"Table '{tableName}' not found.", false);
        }

        // is_primary_key は明示 true のときのみ真、is_nullable は明示 false のときのみ偽（GUI と同一）
        var isPk =
            args.TryGetProperty("is_primary_key", out var isPkEl)
            && isPkEl.ValueKind == JsonValueKind.True;
        var isNullable =
            !args.TryGetProperty("is_nullable", out var isNullEl)
            || isNullEl.ValueKind != JsonValueKind.False;
        var desc = GetString(args, "description") ?? string.Empty;

        entity.Columns.Add(
            new Column
            {
                Name = columnName,
                DataType = dataType,
                IsPrimaryKey = isPk,
                IsNullable = isNullable,
                Description = desc,
            }
        );

        return ($"Added column '{columnName}' to table '{tableName}'.", true);
    }

    /// <summary>指定テーブルからカラムを削除し、そのカラムを参照するリレーションの参照をクリアする</summary>
    private static (string, bool) RemoveColumn(DiagramDocument document, JsonElement args)
    {
        var tableName = GetString(args, "table_name");
        var columnName = GetString(args, "column_name");

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
        {
            return ("table_name and column_name are required.", false);
        }

        var entity = FindEntity(document.Schema, tableName);

        if (entity is null)
        {
            return ($"Table '{tableName}' not found.", false);
        }

        var column = entity.Columns.FirstOrDefault(c =>
            string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)
        );

        if (column is null)
        {
            return ($"Column '{columnName}' not found.", false);
        }

        entity.Columns.Remove(column);

        // 削除カラムを参照するリレーションの外部キー参照をクリアする（GUI の削除後挙動をミラー）
        foreach (var relationship in document.Schema.Relationships)
        {
            if (relationship.SourceColumnId == column.Id)
            {
                relationship.SourceColumnId = null;
            }

            if (relationship.TargetColumnId == column.Id)
            {
                relationship.TargetColumnId = null;
            }
        }

        return ($"Removed column '{columnName}' from table '{tableName}'.", true);
    }

    /// <summary>エンティティのテーブル名・メモ・説明のうち、指定されたものを更新する</summary>
    private static (string, bool) SetEntityProperty(DiagramDocument document, JsonElement args)
    {
        var tableName = GetString(args, "table_name");

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return ("table_name is required.", false);
        }

        var entity = FindEntity(document.Schema, tableName);

        if (entity is null)
        {
            return ($"Table '{tableName}' not found.", false);
        }

        var changed = new List<string>();

        if (
            args.TryGetProperty("new_table_name", out var newNameEl)
            && newNameEl.ValueKind == JsonValueKind.String
        )
        {
            entity.TableName = newNameEl.GetString()!;
            changed.Add("table name");
        }

        if (args.TryGetProperty("memo", out var memoEl) && memoEl.ValueKind == JsonValueKind.String)
        {
            entity.Memo = memoEl.GetString()!;
            changed.Add("memo");
        }

        if (
            args.TryGetProperty("description", out var descEl)
            && descEl.ValueKind == JsonValueKind.String
        )
        {
            entity.Description = descEl.GetString()!;
            changed.Add("description");
        }

        if (changed.Count == 0)
        {
            return ("No properties specified to change.", false);
        }

        return ($"Updated {string.Join(", ", changed)} of table '{tableName}'.", true);
    }

    /// <summary>カラムの説明・データ型・NULL 許容のうち、指定されたものを更新する</summary>
    private static (string, bool) SetColumnProperty(DiagramDocument document, JsonElement args)
    {
        var tableName = GetString(args, "table_name");
        var columnName = GetString(args, "column_name");

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
        {
            return ("table_name and column_name are required.", false);
        }

        var entity = FindEntity(document.Schema, tableName);

        if (entity is null)
        {
            return ($"Table '{tableName}' not found.", false);
        }

        var column = entity.Columns.FirstOrDefault(c =>
            string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)
        );

        if (column is null)
        {
            return ($"Column '{columnName}' not found.", false);
        }

        var changed = new List<string>();

        if (
            args.TryGetProperty("description", out var descEl)
            && descEl.ValueKind == JsonValueKind.String
        )
        {
            column.Description = descEl.GetString()!;
            changed.Add("description");
        }

        if (
            args.TryGetProperty("data_type", out var dataTypeEl)
            && dataTypeEl.ValueKind == JsonValueKind.String
        )
        {
            column.DataType = dataTypeEl.GetString()!;
            changed.Add("data type");
        }

        if (
            args.TryGetProperty("is_nullable", out var isNullEl)
            && isNullEl.ValueKind is JsonValueKind.True or JsonValueKind.False
        )
        {
            column.IsNullable = isNullEl.GetBoolean();
            changed.Add("nullability");
        }

        if (changed.Count == 0)
        {
            return (
                "No properties specified to change. Specify at least one of description, data_type, or is_nullable.",
                false
            );
        }

        return (
            $"Updated {string.Join(", ", changed)} of column '{columnName}' in table '{tableName}'.",
            true
        );
    }

    /// <summary>2 テーブル間にリレーションを追加する</summary>
    /// <remarks>source_column / target_column が明示された列をそのまま使用し、省略時のみ既定解決する</remarks>
    private static (string, bool) AddRelationship(DiagramDocument document, JsonElement args)
    {
        var sourceTable = GetString(args, "source_table");
        var targetTable = GetString(args, "target_table");
        var typeStr = GetString(args, "relationship_type") ?? "OneToMany";

        if (string.IsNullOrWhiteSpace(sourceTable) || string.IsNullOrWhiteSpace(targetTable))
        {
            return ("source_table and target_table are required.", false);
        }

        var schema = document.Schema;
        var source = FindEntity(schema, sourceTable);
        var target = FindEntity(schema, targetTable);

        if (source is null)
        {
            return ($"Table '{sourceTable}' not found.", false);
        }

        if (target is null)
        {
            return ($"Table '{targetTable}' not found.", false);
        }

        var relType = typeStr switch
        {
            "OneToOne" => RelationshipType.OneToOne,
            "ManyToMany" => RelationshipType.ManyToMany,
            _ => RelationshipType.OneToMany,
        };

        // 明示された参照元列を最優先で使用する（存在しない列名はエラーとして返す）
        var sourceColumnName = GetString(args, "source_column");
        Column? sourcePk;

        if (!string.IsNullOrWhiteSpace(sourceColumnName))
        {
            sourcePk = source.Columns.FirstOrDefault(c =>
                string.Equals(c.Name, sourceColumnName, StringComparison.OrdinalIgnoreCase)
            );

            if (sourcePk is null)
            {
                return ($"Column '{sourceColumnName}' not found in table '{sourceTable}'.", false);
            }
        }
        else
        {
            sourcePk = source.Columns.FirstOrDefault(c => c.IsPrimaryKey);
        }

        var targetColumnName = GetString(args, "target_column");
        Column? targetColumn;

        if (!string.IsNullOrWhiteSpace(targetColumnName))
        {
            targetColumn = target.Columns.FirstOrDefault(c =>
                string.Equals(c.Name, targetColumnName, StringComparison.OrdinalIgnoreCase)
            );

            if (targetColumn is null)
            {
                return ($"Column '{targetColumnName}' not found in table '{targetTable}'.", false);
            }
        }
        else
        {
            targetColumn = ForeignKeyColumnResolver.ResolveTargetColumn(
                source,
                target,
                sourcePk,
                schema.Relationships
            );
        }

        schema.Relationships.Add(
            new Relationship
            {
                SourceEntityId = source.Id,
                TargetEntityId = target.Id,
                Type = relType,
                SourceColumnId = sourcePk?.Id,
                TargetColumnId = targetColumn?.Id,
                ConstraintName = $"FK_{target.TableName}_{source.TableName}",
            }
        );

        // 参照先の外部キー列へ FK フラグを付与する（GUI の LockRelationshipColumns をミラー）
        if (targetColumn is not null)
        {
            targetColumn.IsForeignKey = true;
        }

        return ($"Added relationship '{sourceTable}' → '{targetTable}'.", true);
    }

    /// <summary>指定した参照元・参照先テーブル間のリレーションを削除する</summary>
    private static (string, bool) RemoveRelationship(DiagramDocument document, JsonElement args)
    {
        var sourceTable = GetString(args, "source_table");
        var targetTable = GetString(args, "target_table");

        if (string.IsNullOrWhiteSpace(sourceTable) || string.IsNullOrWhiteSpace(targetTable))
        {
            return ("source_table and target_table are required.", false);
        }

        var schema = document.Schema;

        var rel = schema.Relationships.FirstOrDefault(r =>
        {
            var source = FindEntityById(schema, r.SourceEntityId);
            var target = FindEntityById(schema, r.TargetEntityId);

            return source is not null
                && target is not null
                && string.Equals(source.TableName, sourceTable, StringComparison.OrdinalIgnoreCase)
                && string.Equals(target.TableName, targetTable, StringComparison.OrdinalIgnoreCase);
        });

        if (rel is null)
        {
            return ($"Relationship '{sourceTable}' → '{targetTable}' not found.", false);
        }

        schema.Relationships.Remove(rel);

        return ($"Removed relationship '{sourceTable}' → '{targetTable}'.", true);
    }

    // ---------------- helpers ----------------

    /// <summary>テーブル名でエンティティを検索する（大文字小文字を区別しない・最初の一致）</summary>
    private static Entity? FindEntity(ErDiagram schema, string tableName) =>
        schema.Entities.FirstOrDefault(e =>
            string.Equals(e.TableName, tableName, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>ID でエンティティを検索する</summary>
    private static Entity? FindEntityById(ErDiagram schema, Guid id) =>
        schema.Entities.FirstOrDefault(e => e.Id == id);

    /// <summary>JSON 引数から文字列プロパティを取得する（無い・型不一致なら null）</summary>
    private static string? GetString(JsonElement element, string propertyName)
    {
        return
            element.TryGetProperty(propertyName, out var val)
            && val.ValueKind == JsonValueKind.String
            ? val.GetString()
            : null;
    }
}
