using System.Text;
using System.Text.Json;
using ERDesigner.Models;
using ERDesigner.ViewModels;

namespace ERDesigner.Services;

/// <summary>Codex dynamicTools として公開する ER 図操作ツールの定義と実行を担います。</summary>
public static class ErDiagramDynamicTools
{
    /// <summary>すべての dynamicTool 定義を返します。</summary>
    public static IReadOnlyList<CodexDynamicToolDefinition> GetDefinitions()
    {
        return
        [
            new CodexDynamicToolDefinition
            {
                Name = "get_diagram_summary",
                Description = "現在の ER 図のエンティティ（テーブル）とリレーション（外部キー）の一覧をテキストで返します。",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>(),
                },
            },
            new CodexDynamicToolDefinition
            {
                Name = "add_entity",
                Description = "新しいエンティティ（テーブル）を ER 図に追加します。列は作成されないので、追加後に add_column で主キー列やその他の列を定義してください。",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        table_name = new { type = "string", description = "テーブル名" },
                        description = new { type = "string", description = "テーブルの説明（省略可）" },
                    },
                    required = new[] { "table_name" },
                },
            },
            new CodexDynamicToolDefinition
            {
                Name = "remove_entity",
                Description = "指定したテーブル名のエンティティを ER 図から削除します。",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new { table_name = new { type = "string", description = "削除するテーブル名" } },
                    required = new[] { "table_name" },
                },
            },
            new CodexDynamicToolDefinition
            {
                Name = "add_column",
                Description = "指定したテーブルにカラムを追加します。",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        table_name = new { type = "string", description = "テーブル名" },
                        column_name = new { type = "string", description = "カラム名" },
                        data_type = new { type = "string", description = "データ型（例: int, nvarchar(100), datetime2）" },
                        is_primary_key = new { type = "boolean", description = "主キーかどうか" },
                        is_nullable = new { type = "boolean", description = "NULL を許可するかどうか" },
                        description = new { type = "string", description = "カラムの説明（省略可）" },
                    },
                    required = new[] { "table_name", "column_name", "data_type" },
                },
            },
            new CodexDynamicToolDefinition
            {
                Name = "remove_column",
                Description = "指定したテーブルからカラムを削除します。",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new { table_name = new { type = "string", description = "テーブル名" }, column_name = new { type = "string", description = "削除するカラム名" } },
                    required = new[] { "table_name", "column_name" },
                },
            },
            new CodexDynamicToolDefinition
            {
                Name = "set_entity_property",
                Description = "エンティティのプロパティ（テーブル名、メモ、説明）を変更します。",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        table_name = new { type = "string", description = "変更対象のテーブル名" },
                        new_table_name = new { type = "string", description = "新しいテーブル名（変更する場合）" },
                        memo = new { type = "string", description = "メモ（変更する場合）" },
                        description = new { type = "string", description = "説明（変更する場合）" },
                    },
                    required = new[] { "table_name" },
                },
            },
            new CodexDynamicToolDefinition
            {
                Name = "set_column_property",
                Description =
                    "指定したテーブルのカラムのプロパティ（説明、データ型、NULL 許容）を変更します。description / data_type / is_nullable のうち少なくとも 1 つを必ず指定してください。",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        table_name = new { type = "string", description = "変更対象のテーブル名" },
                        column_name = new { type = "string", description = "変更対象のカラム名" },
                        description = new { type = "string", description = "カラムの説明（変更する場合に指定）" },
                        data_type = new { type = "string", description = "データ型（変更する場合に指定。例: int, nvarchar(100), datetime2）" },
                        is_nullable = new { type = "boolean", description = "NULL 許容フラグ（変更する場合に指定）" },
                    },
                    required = new[] { "table_name", "column_name" },
                },
            },
            new CodexDynamicToolDefinition
            {
                Name = "add_relationship",
                Description = "2 つのテーブル間にリレーション（外部キー）を追加します。",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        source_table = new { type = "string", description = "参照元（親）テーブル名" },
                        target_table = new { type = "string", description = "参照先（子）テーブル名" },
                        relationship_type = new
                        {
                            type = "string",
                            @enum = new[] { "OneToOne", "OneToMany", "ManyToMany" },
                            description = "リレーション種別",
                        },
                    },
                    required = new[] { "source_table", "target_table", "relationship_type" },
                },
            },
            new CodexDynamicToolDefinition
            {
                Name = "remove_relationship",
                Description = "2 つのテーブル間のリレーションを削除します。",
                DeferLoading = false,
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        source_table = new { type = "string", description = "参照元テーブル名" },
                        target_table = new { type = "string", description = "参照先テーブル名" },
                    },
                    required = new[] { "source_table", "target_table" },
                },
            },
        ];
    }

    /// <summary>dynamicTool 呼び出しを受け取り、ER 図を操作してその結果を返します。</summary>
    /// <param name="toolName">ツール名</param>
    /// <param name="arguments">引数 JSON</param>
    /// <param name="viewModel">操作対象の MainViewModel</param>
    /// <returns>ツール実行結果テキストと成否のタプル</returns>
    public static (string Result, bool Success) Execute(string toolName, JsonElement arguments, MainViewModel viewModel)
    {
        try
        {
            return toolName switch
            {
                "get_diagram_summary" => (BuildDiagramSummary(viewModel), true),
                "add_entity" => AddEntity(arguments, viewModel),
                "remove_entity" => RemoveEntity(arguments, viewModel),
                "add_column" => AddColumn(arguments, viewModel),
                "remove_column" => RemoveColumn(arguments, viewModel),
                "set_entity_property" => SetEntityProperty(arguments, viewModel),
                "add_relationship" => AddRelationship(arguments, viewModel),
                "remove_relationship" => RemoveRelationship(arguments, viewModel),
                "set_column_property" => SetColumnProperty(arguments, viewModel),
                _ => ($"未対応のツール: {toolName}", false),
            };
        }
        catch (Exception ex)
        {
            return ($"エラー: {ex.Message}", false);
        }
    }

    private static string BuildDiagramSummary(MainViewModel vm)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"テーブル数: {vm.Entities.Count}");
        sb.AppendLine($"リレーション数: {vm.Relationships.Count}");
        sb.AppendLine();

        foreach (var entity in vm.Entities)
        {
            sb.AppendLine($"[{entity.TableName}]");

            if (!string.IsNullOrWhiteSpace(entity.Description))
            {
                sb.AppendLine($"  説明: {entity.Description}");
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
                var colDesc = !string.IsNullOrWhiteSpace(col.Description) ? $" // {col.Description}" : string.Empty;
                sb.AppendLine($"  - {col.Name}: {col.DataType}{flagsText}{colDesc}");
            }
        }

        if (vm.Relationships.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("リレーション:");

            foreach (var rel in vm.Relationships)
            {
                sb.AppendLine($"  {rel.Source.TableName} → {rel.Target.TableName} ({rel.Type})");
            }
        }

        return sb.ToString();
    }

    private static (string, bool) AddEntity(JsonElement args, MainViewModel vm)
    {
        var tableName = GetString(args, "table_name") ?? "NewTable";
        var desc = GetString(args, "description") ?? string.Empty;
        var model = new Entity
        {
            TableName = tableName,
            Description = desc,
            X = 60 + vm.Entities.Count * 30,
            Y = 60 + vm.Entities.Count * 30,
        };
        var vmEntity = new EntityViewModel(model);
        vm.UndoRedo.Execute(new UndoRedo.AddEntityCommand(vm, vmEntity));
        return ($"テーブル '{tableName}' を追加しました。", true);
    }

    private static (string, bool) RemoveEntity(JsonElement args, MainViewModel vm)
    {
        var tableName = GetString(args, "table_name");

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return ("table_name が指定されていません。", false);
        }

        var entity = vm.Entities.FirstOrDefault(e => string.Equals(e.TableName, tableName, StringComparison.OrdinalIgnoreCase));

        if (entity is null)
        {
            return ($"テーブル '{tableName}' が見つかりません。", false);
        }

        vm.UndoRedo.Execute(new UndoRedo.RemoveEntityCommand(vm, entity));
        return ($"テーブル '{tableName}' を削除しました。", true);
    }

    private static (string, bool) AddColumn(JsonElement args, MainViewModel vm)
    {
        var tableName = GetString(args, "table_name");
        var columnName = GetString(args, "column_name");
        var dataType = GetString(args, "data_type") ?? "nvarchar(100)";

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
        {
            return ("table_name と column_name は必須です。", false);
        }

        var entity = vm.Entities.FirstOrDefault(e => string.Equals(e.TableName, tableName, StringComparison.OrdinalIgnoreCase));

        if (entity is null)
        {
            return ($"テーブル '{tableName}' が見つかりません。", false);
        }

        var isPk = args.TryGetProperty("is_primary_key", out var isPkEl) && isPkEl.ValueKind == JsonValueKind.True;
        var isNullable = !args.TryGetProperty("is_nullable", out var isNullEl) || isNullEl.ValueKind != JsonValueKind.False;
        var desc = GetString(args, "description") ?? string.Empty;

        var column = new ColumnViewModel(
            new Column
            {
                Name = columnName,
                DataType = dataType,
                IsPrimaryKey = isPk,
                IsNullable = isNullable,
                Description = desc,
            }
        );
        vm.UndoRedo.Execute(new UndoRedo.AddColumnCommand(entity.Columns, column));
        return ($"テーブル '{tableName}' にカラム '{columnName}' を追加しました。", true);
    }

    private static (string, bool) RemoveColumn(JsonElement args, MainViewModel vm)
    {
        var tableName = GetString(args, "table_name");
        var columnName = GetString(args, "column_name");

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
        {
            return ("table_name と column_name は必須です。", false);
        }

        var entity = vm.Entities.FirstOrDefault(e => string.Equals(e.TableName, tableName, StringComparison.OrdinalIgnoreCase));

        if (entity is null)
        {
            return ($"テーブル '{tableName}' が見つかりません。", false);
        }

        var column = entity.Columns.FirstOrDefault(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));

        if (column is null)
        {
            return ($"カラム '{columnName}' が見つかりません。", false);
        }

        vm.UndoRedo.Execute(new UndoRedo.RemoveColumnCommand(entity.Columns, column, [], () => vm.ApplyRelationshipColumnRules()));
        return ($"テーブル '{tableName}' からカラム '{columnName}' を削除しました。", true);
    }

    private static (string, bool) SetEntityProperty(JsonElement args, MainViewModel vm)
    {
        var tableName = GetString(args, "table_name");

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return ("table_name は必須です。", false);
        }

        var entity = vm.Entities.FirstOrDefault(e => string.Equals(e.TableName, tableName, StringComparison.OrdinalIgnoreCase));

        if (entity is null)
        {
            return ($"テーブル '{tableName}' が見つかりません。", false);
        }

        var changed = new List<string>();

        if (args.TryGetProperty("new_table_name", out var newNameEl) && newNameEl.ValueKind == JsonValueKind.String)
        {
            entity.TableName = newNameEl.GetString()!;
            changed.Add("テーブル名");
        }

        if (args.TryGetProperty("memo", out var memoEl) && memoEl.ValueKind == JsonValueKind.String)
        {
            entity.Memo = memoEl.GetString()!;
            changed.Add("メモ");
        }

        if (args.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String)
        {
            entity.Description = descEl.GetString()!;
            changed.Add("説明");
        }

        if (changed.Count == 0)
        {
            return ("変更するプロパティが指定されていません。", false);
        }

        return ($"テーブル '{tableName}' の {string.Join("、", changed)} を更新しました。", true);
    }

    private static (string, bool) SetColumnProperty(JsonElement args, MainViewModel vm)
    {
        var tableName = GetString(args, "table_name");
        var columnName = GetString(args, "column_name");

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
        {
            return ("table_name と column_name は必須です。", false);
        }

        var entity = vm.Entities.FirstOrDefault(e => string.Equals(e.TableName, tableName, StringComparison.OrdinalIgnoreCase));

        if (entity is null)
        {
            return ($"テーブル '{tableName}' が見つかりません。", false);
        }

        var column = entity.Columns.FirstOrDefault(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));

        if (column is null)
        {
            return ($"カラム '{columnName}' が見つかりません。", false);
        }

        var changed = new List<string>();

        if (args.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String)
        {
            column.Description = descEl.GetString()!;
            changed.Add("説明");
        }

        if (args.TryGetProperty("data_type", out var dataTypeEl) && dataTypeEl.ValueKind == JsonValueKind.String)
        {
            column.DataType = dataTypeEl.GetString()!;
            changed.Add("データ型");
        }

        if (args.TryGetProperty("is_nullable", out var isNullEl) && isNullEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            column.IsNullable = isNullEl.GetBoolean();
            changed.Add("NULL 許容");
        }

        if (changed.Count == 0)
        {
            return ("変更するプロパティが指定されていません。description / data_type / is_nullable のいずれか 1 つ以上を指定してください。", false);
        }

        return ($"テーブル '{tableName}' のカラム '{columnName}' の {string.Join("、", changed)} を更新しました。", true);
    }

    private static (string, bool) AddRelationship(JsonElement args, MainViewModel vm)
    {
        var sourceTable = GetString(args, "source_table");
        var targetTable = GetString(args, "target_table");
        var typeStr = GetString(args, "relationship_type") ?? "OneToMany";

        if (string.IsNullOrWhiteSpace(sourceTable) || string.IsNullOrWhiteSpace(targetTable))
        {
            return ("source_table と target_table は必須です。", false);
        }

        var source = vm.Entities.FirstOrDefault(e => string.Equals(e.TableName, sourceTable, StringComparison.OrdinalIgnoreCase));
        var target = vm.Entities.FirstOrDefault(e => string.Equals(e.TableName, targetTable, StringComparison.OrdinalIgnoreCase));

        if (source is null)
        {
            return ($"テーブル '{sourceTable}' が見つかりません。", false);
        }

        if (target is null)
        {
            return ($"テーブル '{targetTable}' が見つかりません。", false);
        }

        var relType = typeStr switch
        {
            "OneToOne" => RelationshipType.OneToOne,
            "ManyToMany" => RelationshipType.ManyToMany,
            _ => RelationshipType.OneToMany,
        };

        var sourcePk = source.Columns.FirstOrDefault(c => c.IsPrimaryKey);
        var targetColumn = ResolveTargetForeignKeyColumn(source, target);

        var rel = new RelationshipViewModel(
            new Relationship
            {
                SourceEntityId = source.Id,
                TargetEntityId = target.Id,
                Type = relType,
                SourceColumnId = sourcePk?.Id,
                TargetColumnId = targetColumn?.Id,
                ConstraintName = $"FK_{target.TableName}_{source.TableName}",
            },
            source,
            target
        );
        vm.UndoRedo.Execute(new UndoRedo.AddRelationshipCommand(vm, rel));
        return ($"'{sourceTable}' → '{targetTable}' のリレーションを追加しました。", true);
    }

    private static (string, bool) RemoveRelationship(JsonElement args, MainViewModel vm)
    {
        var sourceTable = GetString(args, "source_table");
        var targetTable = GetString(args, "target_table");

        if (string.IsNullOrWhiteSpace(sourceTable) || string.IsNullOrWhiteSpace(targetTable))
        {
            return ("source_table と target_table は必須です。", false);
        }

        var rel = vm.Relationships.FirstOrDefault(r =>
            string.Equals(r.Source.TableName, sourceTable, StringComparison.OrdinalIgnoreCase) && string.Equals(r.Target.TableName, targetTable, StringComparison.OrdinalIgnoreCase)
        );

        if (rel is null)
        {
            return ($"'{sourceTable}' → '{targetTable}' のリレーションが見つかりません。", false);
        }

        vm.UndoRedo.Execute(new UndoRedo.RemoveRelationshipCommand(vm, rel));
        return ($"'{sourceTable}' → '{targetTable}' のリレーションを削除しました。", true);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var val) && val.ValueKind == JsonValueKind.String ? val.GetString() : null;
    }

    /// <summary>source の PK 名に対応する target 側の FK 列候補を解決します。</summary>
    private static ColumnViewModel? ResolveTargetForeignKeyColumn(EntityViewModel source, EntityViewModel target)
    {
        var sourcePk = source.Columns.FirstOrDefault(c => c.IsPrimaryKey);

        if (sourcePk is not null)
        {
            // ソーステーブル名 + "Id" のパターン（例: CustomerId）で検索する
            var fkNameBySuffix = source.TableName + "Id";
            var byName = target.Columns.FirstOrDefault(c => string.Equals(c.Name, fkNameBySuffix, StringComparison.OrdinalIgnoreCase));

            if (byName is not null)
            {
                return byName;
            }

            // PK と同名のカラムを検索する
            var sameName = target.Columns.FirstOrDefault(c => string.Equals(c.Name, sourcePk.Name, StringComparison.OrdinalIgnoreCase));

            if (sameName is not null)
            {
                return sameName;
            }
        }

        // 非 PK 列の先頭を使う
        return target.Columns.FirstOrDefault(c => !c.IsPrimaryKey);
    }
}
