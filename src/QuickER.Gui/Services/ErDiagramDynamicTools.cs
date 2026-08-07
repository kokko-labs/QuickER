using System.Text;
using System.Text.Json;
using QuickER.Model;
using QuickER.Resources;
using QuickER.ViewModels;

namespace QuickER.Services;

/// <summary>ER 図操作ツールの実行（VM 操作）を担うクラス。ツール定義の正本は QuickER.Mcp.ErDiagramToolCatalog が持つ</summary>
/// <remarks>各ツールの操作は Undo / Redo マネージャー経由で実行し、取り消し可能とする。結果文言は resx（表示言語に追従）で解決する</remarks>
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
                "set_unique_constraint" => SetUniqueConstraint(arguments, viewModel),
                "remove_unique_constraint" => RemoveUniqueConstraint(arguments, viewModel),
                _ => (string.Format(Strings.Tool_Unsupported, toolName), false),
            };
        }
        catch (Exception ex)
        {
            // ツール実行中の例外は AI 側へエラーテキストとして返し、アプリを落とさない
            return (string.Format(Strings.Tool_Error, ex.Message), false);
        }
    }

    /// <summary>現在の ER 図の概要（テーブル・カラム・リレーション）をテキスト化する</summary>
    private static string BuildDiagramSummary(MainViewModel vm)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(Strings.Tool_Summary_TableCount, vm.Entities.Count));
        sb.AppendLine(
            string.Format(Strings.Tool_Summary_RelationshipCount, vm.Relationships.Count)
        );
        sb.AppendLine();

        foreach (var entity in vm.Entities)
        {
            sb.AppendLine($"[{entity.TableName}]");

            if (!string.IsNullOrWhiteSpace(entity.Description))
            {
                sb.AppendLine(
                    $"  {string.Format(Strings.Tool_Summary_EntityDescription, entity.Description)}"
                );
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

            AppendUniqueConstraints(sb, entity);
        }

        if (vm.Relationships.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Strings.Tool_Summary_RelationshipsHeader);

            foreach (var rel in vm.Relationships)
            {
                sb.AppendLine(
                    $"  {rel.Source.TableName} → {rel.Target.TableName} ({rel.Type}{DescribeColumnPairs(rel)}){DescribeConstraintName(rel)}"
                );
            }
        }

        return sb.ToString();
    }

    /// <summary>エンティティを追加する（初期座標は重なり回避のため件数に応じてずらす）</summary>
    private static (string, bool) AddEntity(JsonElement args, MainViewModel vm)
    {
        var tableName = GetString(args, "table_name") ?? "NewTable";
        var desc = GetString(args, "description") ?? string.Empty;
        var model = new Entity { TableName = tableName, Description = desc };
        var layout = new Documents.EntityLayout
        {
            X = 60 + vm.Entities.Count * 30,
            Y = 60 + vm.Entities.Count * 30,
        };
        var vmEntity = new EntityViewModel(model, layout);
        vm.UndoRedo.Execute(new UndoRedo.AddEntityCommand(vm, vmEntity));
        return (string.Format(Strings.Tool_EntityAdded, tableName), true);
    }

    /// <summary>指定テーブル名のエンティティを削除する</summary>
    private static (string, bool) RemoveEntity(JsonElement args, MainViewModel vm)
    {
        var tableName = GetString(args, "table_name");

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return (Strings.Tool_TableNameRequired, false);
        }

        var entity = vm.Entities.FirstOrDefault(e =>
            string.Equals(e.TableName, tableName, StringComparison.OrdinalIgnoreCase)
        );

        if (entity is null)
        {
            return (string.Format(Strings.Tool_TableNotFound, tableName), false);
        }

        vm.UndoRedo.Execute(new UndoRedo.RemoveEntityCommand(vm, entity));
        return (string.Format(Strings.Tool_EntityRemoved, tableName), true);
    }

    /// <summary>指定テーブルへカラムを追加する</summary>
    private static (string, bool) AddColumn(JsonElement args, MainViewModel vm)
    {
        var tableName = GetString(args, "table_name");
        var columnName = GetString(args, "column_name");
        var dataType = GetString(args, "data_type") ?? "nvarchar(100)";

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
        {
            return (Strings.Tool_TableAndColumnNameRequired, false);
        }

        var entity = vm.Entities.FirstOrDefault(e =>
            string.Equals(e.TableName, tableName, StringComparison.OrdinalIgnoreCase)
        );

        if (entity is null)
        {
            return (string.Format(Strings.Tool_TableNotFound, tableName), false);
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
        return (string.Format(Strings.Tool_ColumnAdded, tableName, columnName), true);
    }

    /// <summary>指定テーブルからカラムを削除し、外部キー列ルールを再適用する</summary>
    private static (string, bool) RemoveColumn(JsonElement args, MainViewModel vm)
    {
        var tableName = GetString(args, "table_name");
        var columnName = GetString(args, "column_name");

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
        {
            return (Strings.Tool_TableAndColumnNameRequired, false);
        }

        var entity = vm.Entities.FirstOrDefault(e =>
            string.Equals(e.TableName, tableName, StringComparison.OrdinalIgnoreCase)
        );

        if (entity is null)
        {
            return (string.Format(Strings.Tool_TableNotFound, tableName), false);
        }

        var column = entity.Columns.FirstOrDefault(c =>
            string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)
        );

        if (column is null)
        {
            return (string.Format(Strings.Tool_ColumnNotFound, columnName), false);
        }

        // 削除カラムを参照する FK リレーションを収集し、Undo 時に参照を復元できるようコマンドへ渡す
        var affected = vm.FindRelationshipsUsingColumn(column);
        vm.UndoRedo.Execute(
            new UndoRedo.RemoveColumnCommand(
                entity,
                column,
                affected,
                () => vm.ApplyRelationshipColumnRules()
            )
        );
        return (string.Format(Strings.Tool_ColumnRemoved, tableName, columnName), true);
    }

    /// <summary>エンティティのテーブル名・メモ・説明のうち、指定されたものを更新する</summary>
    private static (string, bool) SetEntityProperty(JsonElement args, MainViewModel vm)
    {
        var tableName = GetString(args, "table_name");

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return (Strings.Tool_TableNameRequired, false);
        }

        var entity = vm.Entities.FirstOrDefault(e =>
            string.Equals(e.TableName, tableName, StringComparison.OrdinalIgnoreCase)
        );

        if (entity is null)
        {
            return (string.Format(Strings.Tool_TableNotFound, tableName), false);
        }

        var changed = new List<string>();

        if (
            args.TryGetProperty("new_table_name", out var newNameEl)
            && newNameEl.ValueKind == JsonValueKind.String
        )
        {
            entity.TableName = newNameEl.GetString()!;
            changed.Add(Strings.Tool_Field_TableName);
        }

        if (args.TryGetProperty("memo", out var memoEl) && memoEl.ValueKind == JsonValueKind.String)
        {
            entity.Memo = memoEl.GetString()!;
            changed.Add(Strings.Tool_Field_Memo);
        }

        if (
            args.TryGetProperty("description", out var descEl)
            && descEl.ValueKind == JsonValueKind.String
        )
        {
            entity.Description = descEl.GetString()!;
            changed.Add(Strings.Tool_Field_Description);
        }

        if (changed.Count == 0)
        {
            return (Strings.Tool_NoPropertiesToChange, false);
        }

        return (
            string.Format(
                Strings.Tool_EntityUpdated,
                tableName,
                string.Join(Strings.Tool_ListSeparator, changed)
            ),
            true
        );
    }

    /// <summary>カラムの説明・データ型・NULL 許容のうち、指定されたものを更新する</summary>
    private static (string, bool) SetColumnProperty(JsonElement args, MainViewModel vm)
    {
        var tableName = GetString(args, "table_name");
        var columnName = GetString(args, "column_name");

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
        {
            return (Strings.Tool_TableAndColumnNameRequired, false);
        }

        var entity = vm.Entities.FirstOrDefault(e =>
            string.Equals(e.TableName, tableName, StringComparison.OrdinalIgnoreCase)
        );

        if (entity is null)
        {
            return (string.Format(Strings.Tool_TableNotFound, tableName), false);
        }

        var column = entity.Columns.FirstOrDefault(c =>
            string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)
        );

        if (column is null)
        {
            return (string.Format(Strings.Tool_ColumnNotFound, columnName), false);
        }

        var changed = new List<string>();

        if (
            args.TryGetProperty("description", out var descEl)
            && descEl.ValueKind == JsonValueKind.String
        )
        {
            column.Description = descEl.GetString()!;
            changed.Add(Strings.Tool_Field_Description);
        }

        if (
            args.TryGetProperty("data_type", out var dataTypeEl)
            && dataTypeEl.ValueKind == JsonValueKind.String
        )
        {
            column.DataType = dataTypeEl.GetString()!;
            changed.Add(Strings.Tool_Field_DataType);
        }

        if (
            args.TryGetProperty("is_nullable", out var isNullEl)
            && isNullEl.ValueKind is JsonValueKind.True or JsonValueKind.False
        )
        {
            column.IsNullable = isNullEl.GetBoolean();
            changed.Add(Strings.Tool_Field_Nullable);
        }

        if (changed.Count == 0)
        {
            return (Strings.Tool_NoColumnPropertiesToChange, false);
        }

        return (
            string.Format(
                Strings.Tool_ColumnUpdated,
                tableName,
                columnName,
                string.Join(Strings.Tool_ListSeparator, changed)
            ),
            true
        );
    }

    /// <summary>2 テーブル間にリレーションを追加する</summary>
    /// <remarks>
    /// AI が source_columns / target_columns で明示した列をそのまま使用し、両方の省略時のみ
    /// 「親 PK 全列の自動ペア化」で解決する（手動作成フローと同じ意味論）
    /// </remarks>
    private static (string, bool) AddRelationship(JsonElement args, MainViewModel vm)
    {
        var sourceTable = GetString(args, "source_table");
        var targetTable = GetString(args, "target_table");
        var typeStr = GetString(args, "relationship_type") ?? "OneToMany";

        if (string.IsNullOrWhiteSpace(sourceTable) || string.IsNullOrWhiteSpace(targetTable))
        {
            return (Strings.Tool_SourceAndTargetTableRequired, false);
        }

        var source = vm.Entities.FirstOrDefault(e =>
            string.Equals(e.TableName, sourceTable, StringComparison.OrdinalIgnoreCase)
        );
        var target = vm.Entities.FirstOrDefault(e =>
            string.Equals(e.TableName, targetTable, StringComparison.OrdinalIgnoreCase)
        );

        if (source is null)
        {
            return (string.Format(Strings.Tool_TableNotFound, sourceTable), false);
        }

        if (target is null)
        {
            return (string.Format(Strings.Tool_TableNotFound, targetTable), false);
        }

        var relType = typeStr switch
        {
            "OneToOne" => RelationshipType.OneToOne,
            "ManyToMany" => RelationshipType.ManyToMany,
            _ => RelationshipType.OneToMany,
        };

        var (columnPairs, pairError) = ResolveRelationshipColumnPairs(source, target, args, vm);

        if (pairError is not null)
        {
            return (pairError, false);
        }

        var rel = new RelationshipViewModel(
            new Relationship
            {
                SourceEntityId = source.Id,
                TargetEntityId = target.Id,
                Type = relType,
                // 多対多では VM 側の整合処理が列ペアを落とす（中間テーブルを介する概念表現のため）
                ColumnPairs = columnPairs!,
                ConstraintName = $"FK_{target.TableName}_{source.TableName}",
            },
            source,
            target
        );
        vm.UndoRedo.Execute(new UndoRedo.AddRelationshipCommand(vm, rel));
        return (string.Format(Strings.Tool_RelationshipAdded, sourceTable, targetTable), true);
    }

    /// <summary>
    /// <c>source_columns</c> / <c>target_columns</c>（並行配列）から列ペアを解決する。
    /// 両方が省略された場合のみ親 PK 全列の自動ペア化へフォールバックする
    /// </summary>
    private static (
        List<RelationshipColumnPair>? Pairs,
        string? Error
    ) ResolveRelationshipColumnPairs(
        EntityViewModel source,
        EntityViewModel target,
        JsonElement args,
        MainViewModel vm
    )
    {
        var (sourceNames, sourceError) = GetColumnNames(args, "source_columns");

        if (sourceError is not null)
        {
            return (null, sourceError);
        }

        var (targetNames, targetError) = GetColumnNames(args, "target_columns");

        if (targetError is not null)
        {
            return (null, targetError);
        }

        if (sourceNames is null && targetNames is null)
        {
            return (
                ForeignKeyColumnResolver.ResolveColumnPairs(source, target, vm.Relationships),
                null
            );
        }

        if (sourceNames is null || targetNames is null)
        {
            return (null, Strings.Tool_RelationshipColumnListsRequiredTogether);
        }

        if (sourceNames.Count != targetNames.Count)
        {
            return (
                null,
                string.Format(
                    Strings.Tool_RelationshipColumnListsLengthMismatch,
                    sourceNames.Count,
                    targetNames.Count
                )
            );
        }

        var pairs = new List<RelationshipColumnPair>();
        var usedSourceIds = new HashSet<Guid>();
        var usedTargetIds = new HashSet<Guid>();

        for (var i = 0; i < sourceNames.Count; i++)
        {
            var sourceColumn = FindColumn(source, sourceNames[i]);

            if (sourceColumn is null)
            {
                return (
                    null,
                    string.Format(
                        Strings.Tool_ColumnNotFoundInTable,
                        source.TableName,
                        sourceNames[i]
                    )
                );
            }

            if (!usedSourceIds.Add(sourceColumn.Id))
            {
                return (
                    null,
                    string.Format(
                        Strings.Tool_RelationshipDuplicateColumn,
                        sourceColumn.Name,
                        "source_columns"
                    )
                );
            }

            var targetColumn = FindColumn(target, targetNames[i]);

            if (targetColumn is null)
            {
                return (
                    null,
                    string.Format(
                        Strings.Tool_ColumnNotFoundInTable,
                        target.TableName,
                        targetNames[i]
                    )
                );
            }

            if (!usedTargetIds.Add(targetColumn.Id))
            {
                return (
                    null,
                    string.Format(
                        Strings.Tool_RelationshipDuplicateColumn,
                        targetColumn.Name,
                        "target_columns"
                    )
                );
            }

            pairs.Add(new RelationshipColumnPair(sourceColumn.Id, targetColumn.Id));
        }

        return (pairs, null);
    }

    /// <summary>列名配列の引数を取り出す（未指定は <c>null</c>・型不正や空配列はエラー）</summary>
    private static (List<string>? Names, string? Error) GetColumnNames(
        JsonElement args,
        string propertyName
    )
    {
        if (
            !args.TryGetProperty(propertyName, out var element)
            || element.ValueKind == JsonValueKind.Null
        )
        {
            return (null, null);
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return (null, string.Format(Strings.Tool_RelationshipColumnListNotArray, propertyName));
        }

        var names = new List<string>();

        foreach (var item in element.EnumerateArray())
        {
            if (
                item.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(item.GetString())
            )
            {
                return (
                    null,
                    string.Format(Strings.Tool_RelationshipColumnListInvalid, propertyName)
                );
            }

            names.Add(item.GetString()!);
        }

        if (names.Count == 0)
        {
            return (null, string.Format(Strings.Tool_RelationshipColumnListEmpty, propertyName));
        }

        return (names, null);
    }

    /// <summary>指定テーブルの列を名前で検索する（大文字小文字を区別しない）</summary>
    private static ColumnViewModel? FindColumn(EntityViewModel entity, string columnName) =>
        entity.Columns.FirstOrDefault(column =>
            string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>指定した参照元・参照先テーブル間のリレーションを削除する</summary>
    /// <remarks>
    /// 同じ向きのテーブル対に複数のリレーションがある場合は <c>constraint_name</c> で特定する。
    /// 無指定で複数一致したときは黙って先頭を消さず、候補の制約名を挙げてエラーにする
    /// </remarks>
    private static (string, bool) RemoveRelationship(JsonElement args, MainViewModel vm)
    {
        var sourceTable = GetString(args, "source_table");
        var targetTable = GetString(args, "target_table");

        if (string.IsNullOrWhiteSpace(sourceTable) || string.IsNullOrWhiteSpace(targetTable))
        {
            return (Strings.Tool_SourceAndTargetTableRequired, false);
        }

        var matches = vm
            .Relationships.Where(r =>
                string.Equals(r.Source.TableName, sourceTable, StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    r.Target.TableName,
                    targetTable,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .ToList();

        if (matches.Count == 0)
        {
            return (
                string.Format(Strings.Tool_RelationshipNotFound, sourceTable, targetTable),
                false
            );
        }

        var constraintName = GetString(args, "constraint_name");

        if (!string.IsNullOrWhiteSpace(constraintName))
        {
            var byName = matches
                .Where(r =>
                    string.Equals(
                        r.ConstraintName,
                        constraintName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .ToList();

            if (byName.Count == 0)
            {
                return (
                    string.Format(
                        Strings.Tool_RelationshipConstraintNotFound,
                        sourceTable,
                        targetTable,
                        constraintName,
                        DescribeConstraintNames(matches)
                    ),
                    false
                );
            }

            matches = byName;
        }

        if (matches.Count > 1)
        {
            return (
                string.Format(
                    Strings.Tool_RelationshipAmbiguous,
                    sourceTable,
                    targetTable,
                    DescribeConstraintNames(matches)
                ),
                false
            );
        }

        vm.UndoRedo.Execute(new UndoRedo.RemoveRelationshipCommand(vm, matches[0]));
        return (string.Format(Strings.Tool_RelationshipRemoved, sourceTable, targetTable), true);
    }

    /// <summary>候補リレーションの制約名を列挙する（名前なしは「名前なし」表記）</summary>
    private static string DescribeConstraintNames(
        IEnumerable<RelationshipViewModel> relationships
    ) =>
        string.Join(
            Strings.Tool_ListSeparator,
            relationships.Select(r =>
                string.IsNullOrWhiteSpace(r.ConstraintName)
                    ? Strings.Tool_RelationshipUnnamedConstraint
                    : r.ConstraintName!
            )
        );

    /// <summary>一意制約を定義する（同じ列集合の制約があれば名前・列順を差し替え、無ければ追加する）</summary>
    /// <remarks>
    /// 照合キーは (テーブル, 列集合) で、列の順序・大文字小文字は問わない（UNIQUE の意味論が列の並びに
    /// 依存しないため）。既存が見つかった場合は同じ制約を再定義する（<c>set_query</c> と同じ upsert 流儀）。
    /// 名前の変更は変更追跡が、構成列の差し替えは専用コマンドが履歴化する
    /// </remarks>
    private static (string, bool) SetUniqueConstraint(JsonElement args, MainViewModel vm)
    {
        var tableName = GetString(args, "table_name");

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return (Strings.Tool_TableNameRequired, false);
        }

        var entity = vm.Entities.FirstOrDefault(e =>
            string.Equals(e.TableName, tableName, StringComparison.OrdinalIgnoreCase)
        );

        if (entity is null)
        {
            return (string.Format(Strings.Tool_TableNotFound, tableName), false);
        }

        var (columns, error) = ResolveConstraintColumns(entity, args);

        if (error is not null)
        {
            return (error, false);
        }

        var columnIds = columns!.Select(column => column.Id).ToList();
        var name = GetString(args, "name");
        var normalizedName = string.IsNullOrWhiteSpace(name) ? string.Empty : name!;
        var columnText = string.Join(", ", columns!.Select(column => column.Name));
        var existing = FindUniqueConstraintByColumnSet(entity, columnIds);

        if (existing is not null)
        {
            existing.Name = normalizedName;

            // 列集合が同じでも宣言順は指定に合わせる（順序が変わらないなら履歴を積まない）
            if (!existing.ColumnIds.SequenceEqual(columnIds))
            {
                vm.UndoRedo.Execute(
                    new UndoRedo.ChangeUniqueConstraintColumnsCommand(
                        existing,
                        existing.ColumnIds.ToList(),
                        columnIds
                    )
                );
            }

            return (
                string.Format(Strings.Tool_UniqueConstraintUpdated, entity.TableName, columnText),
                true
            );
        }

        var constraint = new UniqueConstraintViewModel(
            entity,
            new UniqueConstraint
            {
                Name = string.IsNullOrEmpty(normalizedName) ? null : normalizedName,
                ColumnIds = columnIds,
            }
        );
        vm.UndoRedo.Execute(
            new UndoRedo.AddUniqueConstraintCommand(entity.UniqueConstraints, constraint)
        );

        return (
            string.Format(Strings.Tool_UniqueConstraintAdded, entity.TableName, columnText),
            true
        );
    }

    /// <summary>列集合で特定した一意制約を削除する</summary>
    private static (string, bool) RemoveUniqueConstraint(JsonElement args, MainViewModel vm)
    {
        var tableName = GetString(args, "table_name");

        if (string.IsNullOrWhiteSpace(tableName))
        {
            return (Strings.Tool_TableNameRequired, false);
        }

        var entity = vm.Entities.FirstOrDefault(e =>
            string.Equals(e.TableName, tableName, StringComparison.OrdinalIgnoreCase)
        );

        if (entity is null)
        {
            return (string.Format(Strings.Tool_TableNotFound, tableName), false);
        }

        var (columns, error) = ResolveConstraintColumns(entity, args);

        if (error is not null)
        {
            return (error, false);
        }

        var columnText = string.Join(", ", columns!.Select(column => column.Name));
        var existing = FindUniqueConstraintByColumnSet(
            entity,
            columns!.Select(column => column.Id).ToList()
        );

        if (existing is null)
        {
            return (
                string.Format(Strings.Tool_UniqueConstraintNotFound, entity.TableName, columnText),
                false
            );
        }

        vm.UndoRedo.Execute(
            new UndoRedo.RemoveUniqueConstraintCommand(entity.UniqueConstraints, existing)
        );

        return (
            string.Format(Strings.Tool_UniqueConstraintRemoved, entity.TableName, columnText),
            true
        );
    }

    /// <summary><c>columns</c> 引数（列名の配列）をエンティティのカラムへ解決する</summary>
    /// <returns>解決したカラム（宣言順）と、失敗時のエラーテキスト</returns>
    private static (List<ColumnViewModel>? Columns, string? Error) ResolveConstraintColumns(
        EntityViewModel entity,
        JsonElement args
    )
    {
        if (
            !args.TryGetProperty("columns", out var columnsEl)
            || columnsEl.ValueKind != JsonValueKind.Array
        )
        {
            return (null, Strings.Tool_UniqueConstraintColumnsRequired);
        }

        var resolved = new List<ColumnViewModel>();

        foreach (var item in columnsEl.EnumerateArray())
        {
            if (
                item.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(item.GetString())
            )
            {
                return (null, Strings.Tool_UniqueConstraintColumnsInvalid);
            }

            var columnName = item.GetString()!;
            var column = entity.Columns.FirstOrDefault(c =>
                string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)
            );

            if (column is null)
            {
                return (
                    null,
                    string.Format(Strings.Tool_ColumnNotFoundInTable, entity.TableName, columnName)
                );
            }

            // 同じ列を 2 回並べた制約は意味を持たないため拒否する（DB 側でもエラーになる）
            if (resolved.Any(existing => existing.Id == column.Id))
            {
                return (
                    null,
                    string.Format(Strings.Tool_UniqueConstraintDuplicateColumn, column.Name)
                );
            }

            resolved.Add(column);
        }

        if (resolved.Count == 0)
        {
            return (null, Strings.Tool_UniqueConstraintColumnsEmpty);
        }

        return (resolved, null);
    }

    /// <summary>構成列の集合（順序を問わない）が一致する一意制約を探す</summary>
    private static UniqueConstraintViewModel? FindUniqueConstraintByColumnSet(
        EntityViewModel entity,
        IReadOnlyList<Guid> columnIds
    )
    {
        var target = new HashSet<Guid>(columnIds);

        return entity.UniqueConstraints.FirstOrDefault(constraint =>
            target.SetEquals(constraint.ColumnIds)
        );
    }

    /// <summary>要約テキストへエンティティの一意制約（解決済み名＋構成列）を追記する</summary>
    /// <remarks>構成列を解決できない制約（空・壊れた参照）は DDL 生成と同じ規則で読み飛ばす</remarks>
    private static void AppendUniqueConstraints(StringBuilder sb, EntityViewModel entity)
    {
        var lines = new List<string>();

        foreach (var constraint in entity.UniqueConstraints)
        {
            var columnNames = constraint
                .ColumnIds.Select(columnId =>
                    entity.Columns.FirstOrDefault(column => column.Id == columnId)
                )
                .Where(column => column is not null)
                .Select(column => column!.Name)
                .ToList();

            if (columnNames.Count == 0 || columnNames.Count != constraint.ColumnIds.Count)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(constraint.Name)
                ? UniqueConstraint.SynthesizeName(entity.TableName, columnNames)
                : constraint.Name;
            lines.Add($"    - {name} ({string.Join(", ", columnNames)})");
        }

        if (lines.Count == 0)
        {
            return;
        }

        sb.AppendLine($"  {Strings.Tool_Summary_UniqueConstraintsHeader}");

        foreach (var line in lines)
        {
            sb.AppendLine(line);
        }
    }

    /// <summary>要約テキスト用に外部キーの列ペアを <c>, FK: (親列 → 子列, …)</c> 形式で表す</summary>
    /// <remarks>列ペアなし（多対多・未割当）や解決できない参照を含む場合は空文字を返す</remarks>
    private static string DescribeColumnPairs(RelationshipViewModel relationship)
    {
        if (relationship.ColumnPairs.Count == 0)
        {
            return string.Empty;
        }

        var texts = new List<string>();

        foreach (var pair in relationship.ColumnPairs)
        {
            var sourceColumn = relationship.Source.Columns.FirstOrDefault(column =>
                column.Id == pair.SourceColumnId
            );
            var targetColumn = relationship.Target.Columns.FirstOrDefault(column =>
                column.Id == pair.TargetColumnId
            );

            if (sourceColumn is null || targetColumn is null)
            {
                return string.Empty;
            }

            texts.Add($"{sourceColumn.Name} → {targetColumn.Name}");
        }

        return $", FK: ({string.Join(", ", texts)})";
    }

    /// <summary>要約テキスト用に外部キー制約名を <c> [名前]</c> 形式で表す（未設定は空文字）</summary>
    private static string DescribeConstraintName(RelationshipViewModel relationship) =>
        string.IsNullOrWhiteSpace(relationship.ConstraintName)
            ? string.Empty
            : $" [{relationship.ConstraintName}]";

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
