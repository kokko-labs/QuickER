using System.Text;
using System.Text.Json;
using QuickER.Model;
using QuickER.ViewModels;

using QuickER.AI;

namespace QuickER.Services;

/// <summary>ER 図操作ツールの実行（VM 操作）を担うクラス。ツール定義は QuickER.AI.ErDiagramToolDefinitions が持つ</summary>
/// <remarks>各ツールの操作は Undo / Redo マネージャー経由で実行し、取り消し可能とする</remarks>
public static class ErDiagramDynamicTools
{

    /// <summary>dynamicTool 呼び出しを受け取り、ツール名でディスパッチして ER 図を操作する</summary>
    /// <param name="toolName">ツール名</param>
    /// <param name="arguments">引数 JSON</param>
    /// <param name="viewModel">操作対象の MainViewModel</param>
    /// <returns>実行結果テキストと成否のタプル</returns>
    public static (string Result, bool Success) Execute(
        string toolName,
        JsonElement arguments,
        MainViewModel viewModel
    )
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
            // ツール実行中の例外は AI 側へエラーテキストとして返し、アプリを落とさない
            return ($"エラー: {ex.Message}", false);
        }
    }

    /// <summary>現在の ER 図の概要（テーブル・カラム・リレーション）をテキスト化する</summary>
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
                var colDesc = !string.IsNullOrWhiteSpace(col.Description)
                    ? $" // {col.Description}"
                    : string.Empty;
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

    /// <summary>エンティティを追加する（初期座標は重なり回避のため件数に応じてずらす）</summary>
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

    /// <summary>指定テーブル名のエンティティを削除する</summary>
    private static (string, bool) RemoveEntity(JsonElement args, MainViewModel vm)
    {
        var tableName = GetString(args, "table_name");

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return ("table_name が指定されていません。", false);
        }

        var entity = vm.Entities.FirstOrDefault(e =>
            string.Equals(e.TableName, tableName, StringComparison.OrdinalIgnoreCase)
        );

        if (entity is null)
        {
            return ($"テーブル '{tableName}' が見つかりません。", false);
        }

        vm.UndoRedo.Execute(new UndoRedo.RemoveEntityCommand(vm, entity));
        return ($"テーブル '{tableName}' を削除しました。", true);
    }

    /// <summary>指定テーブルへカラムを追加する</summary>
    private static (string, bool) AddColumn(JsonElement args, MainViewModel vm)
    {
        var tableName = GetString(args, "table_name");
        var columnName = GetString(args, "column_name");
        var dataType = GetString(args, "data_type") ?? "nvarchar(100)";

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
        {
            return ("table_name と column_name は必須です。", false);
        }

        var entity = vm.Entities.FirstOrDefault(e =>
            string.Equals(e.TableName, tableName, StringComparison.OrdinalIgnoreCase)
        );

        if (entity is null)
        {
            return ($"テーブル '{tableName}' が見つかりません。", false);
        }

        var isPk =
            args.TryGetProperty("is_primary_key", out var isPkEl)
            && isPkEl.ValueKind == JsonValueKind.True;
        var isNullable =
            !args.TryGetProperty("is_nullable", out var isNullEl)
            || isNullEl.ValueKind != JsonValueKind.False;
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

    /// <summary>指定テーブルからカラムを削除し、外部キー列ルールを再適用する</summary>
    private static (string, bool) RemoveColumn(JsonElement args, MainViewModel vm)
    {
        var tableName = GetString(args, "table_name");
        var columnName = GetString(args, "column_name");

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
        {
            return ("table_name と column_name は必須です。", false);
        }

        var entity = vm.Entities.FirstOrDefault(e =>
            string.Equals(e.TableName, tableName, StringComparison.OrdinalIgnoreCase)
        );

        if (entity is null)
        {
            return ($"テーブル '{tableName}' が見つかりません。", false);
        }

        var column = entity.Columns.FirstOrDefault(c =>
            string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)
        );

        if (column is null)
        {
            return ($"カラム '{columnName}' が見つかりません。", false);
        }

        // 削除カラムを参照する FK リレーションを収集し、Undo 時に参照を復元できるようコマンドへ渡す
        var affected = vm.FindRelationshipsUsingColumn(column);
        vm.UndoRedo.Execute(
            new UndoRedo.RemoveColumnCommand(
                entity.Columns,
                column,
                affected,
                () => vm.ApplyRelationshipColumnRules()
            )
        );
        return ($"テーブル '{tableName}' からカラム '{columnName}' を削除しました。", true);
    }

    /// <summary>エンティティのテーブル名・メモ・説明のうち、指定されたものを更新する</summary>
    private static (string, bool) SetEntityProperty(JsonElement args, MainViewModel vm)
    {
        var tableName = GetString(args, "table_name");

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return ("table_name は必須です。", false);
        }

        var entity = vm.Entities.FirstOrDefault(e =>
            string.Equals(e.TableName, tableName, StringComparison.OrdinalIgnoreCase)
        );

        if (entity is null)
        {
            return ($"テーブル '{tableName}' が見つかりません。", false);
        }

        var changed = new List<string>();

        if (
            args.TryGetProperty("new_table_name", out var newNameEl)
            && newNameEl.ValueKind == JsonValueKind.String
        )
        {
            entity.TableName = newNameEl.GetString()!;
            changed.Add("テーブル名");
        }

        if (args.TryGetProperty("memo", out var memoEl) && memoEl.ValueKind == JsonValueKind.String)
        {
            entity.Memo = memoEl.GetString()!;
            changed.Add("メモ");
        }

        if (
            args.TryGetProperty("description", out var descEl)
            && descEl.ValueKind == JsonValueKind.String
        )
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

    /// <summary>カラムの説明・データ型・NULL 許容のうち、指定されたものを更新する</summary>
    private static (string, bool) SetColumnProperty(JsonElement args, MainViewModel vm)
    {
        var tableName = GetString(args, "table_name");
        var columnName = GetString(args, "column_name");

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
        {
            return ("table_name と column_name は必須です。", false);
        }

        var entity = vm.Entities.FirstOrDefault(e =>
            string.Equals(e.TableName, tableName, StringComparison.OrdinalIgnoreCase)
        );

        if (entity is null)
        {
            return ($"テーブル '{tableName}' が見つかりません。", false);
        }

        var column = entity.Columns.FirstOrDefault(c =>
            string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)
        );

        if (column is null)
        {
            return ($"カラム '{columnName}' が見つかりません。", false);
        }

        var changed = new List<string>();

        if (
            args.TryGetProperty("description", out var descEl)
            && descEl.ValueKind == JsonValueKind.String
        )
        {
            column.Description = descEl.GetString()!;
            changed.Add("説明");
        }

        if (
            args.TryGetProperty("data_type", out var dataTypeEl)
            && dataTypeEl.ValueKind == JsonValueKind.String
        )
        {
            column.DataType = dataTypeEl.GetString()!;
            changed.Add("データ型");
        }

        if (
            args.TryGetProperty("is_nullable", out var isNullEl)
            && isNullEl.ValueKind is JsonValueKind.True or JsonValueKind.False
        )
        {
            column.IsNullable = isNullEl.GetBoolean();
            changed.Add("NULL 許容");
        }

        if (changed.Count == 0)
        {
            return (
                "変更するプロパティが指定されていません。description / data_type / is_nullable のいずれか 1 つ以上を指定してください。",
                false
            );
        }

        return (
            $"テーブル '{tableName}' のカラム '{columnName}' の {string.Join("、", changed)} を更新しました。",
            true
        );
    }

    /// <summary>2 テーブル間にリレーションを追加する</summary>
    /// <remarks>AI が source_column / target_column で明示した列をそのまま使用し、省略時のみ名前ベースで自動解決する</remarks>
    private static (string, bool) AddRelationship(JsonElement args, MainViewModel vm)
    {
        var sourceTable = GetString(args, "source_table");
        var targetTable = GetString(args, "target_table");
        var typeStr = GetString(args, "relationship_type") ?? "OneToMany";

        if (string.IsNullOrWhiteSpace(sourceTable) || string.IsNullOrWhiteSpace(targetTable))
        {
            return ("source_table と target_table は必須です。", false);
        }

        var source = vm.Entities.FirstOrDefault(e =>
            string.Equals(e.TableName, sourceTable, StringComparison.OrdinalIgnoreCase)
        );
        var target = vm.Entities.FirstOrDefault(e =>
            string.Equals(e.TableName, targetTable, StringComparison.OrdinalIgnoreCase)
        );

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

        // AI が明示した列を最優先で使用する（存在しない列名はエラーとして返し、AI に修正を促す）
        var sourceColumnName = GetString(args, "source_column");
        ColumnViewModel? sourcePk;

        if (!string.IsNullOrWhiteSpace(sourceColumnName))
        {
            sourcePk = source.Columns.FirstOrDefault(c =>
                string.Equals(c.Name, sourceColumnName, StringComparison.OrdinalIgnoreCase)
            );

            if (sourcePk is null)
            {
                return (
                    $"テーブル '{sourceTable}' にカラム '{sourceColumnName}' が見つかりません。",
                    false
                );
            }
        }
        else
        {
            sourcePk = source.Columns.FirstOrDefault(c => c.IsPrimaryKey);
        }

        var targetColumnName = GetString(args, "target_column");
        ColumnViewModel? targetColumn;

        if (!string.IsNullOrWhiteSpace(targetColumnName))
        {
            targetColumn = target.Columns.FirstOrDefault(c =>
                string.Equals(c.Name, targetColumnName, StringComparison.OrdinalIgnoreCase)
            );

            if (targetColumn is null)
            {
                return (
                    $"テーブル '{targetTable}' にカラム '{targetColumnName}' が見つかりません。",
                    false
                );
            }
        }
        else
        {
            targetColumn = ForeignKeyColumnResolver.ResolveTargetColumn(
                source,
                target,
                sourcePk,
                vm.Relationships
            );
        }

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

    /// <summary>指定した参照元・参照先テーブル間のリレーションを削除する</summary>
    private static (string, bool) RemoveRelationship(JsonElement args, MainViewModel vm)
    {
        var sourceTable = GetString(args, "source_table");
        var targetTable = GetString(args, "target_table");

        if (string.IsNullOrWhiteSpace(sourceTable) || string.IsNullOrWhiteSpace(targetTable))
        {
            return ("source_table と target_table は必須です。", false);
        }

        var rel = vm.Relationships.FirstOrDefault(r =>
            string.Equals(r.Source.TableName, sourceTable, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Target.TableName, targetTable, StringComparison.OrdinalIgnoreCase)
        );

        if (rel is null)
        {
            return ($"'{sourceTable}' → '{targetTable}' のリレーションが見つかりません。", false);
        }

        vm.UndoRedo.Execute(new UndoRedo.RemoveRelationshipCommand(vm, rel));
        return ($"'{sourceTable}' → '{targetTable}' のリレーションを削除しました。", true);
    }

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

