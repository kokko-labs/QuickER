using ERDesigner.Models;

namespace ERDesigner.Services;

/// <summary>
/// 現在の ER 図と AI が返した更新後 ER 図を比較し、差分プレビュー用の情報を作成します。
/// </summary>
public sealed class AiUpdateDiffService
{
    /// <summary>
    /// 差分を計算します。
    /// </summary>
    public AiUpdateDiffResult Compute(ErDiagram currentDiagram, ErDiagram updatedDiagram)
    {
        var result = new AiUpdateDiffResult();
        var tableGroup = new AiUpdateDiffGroup { Title = "テーブル" };
        var columnGroup = new AiUpdateDiffGroup { Title = "カラム" };
        var relationshipGroup = new AiUpdateDiffGroup { Title = "リレーション" };

        var currentTables = currentDiagram.Entities.ToDictionary(entity => entity.TableName, StringComparer.OrdinalIgnoreCase);
        var updatedTables = updatedDiagram.Entities.ToDictionary(entity => entity.TableName, StringComparer.OrdinalIgnoreCase);

        foreach (var table in updatedDiagram.Entities)
        {
            if (!currentTables.TryGetValue(table.TableName, out var currentTable))
            {
                tableGroup.Items.Add(
                    CreateItem(
                        AiUpdateDiffCategory.Table,
                        AiUpdateDiffChangeType.Add,
                        $"[追加] {table.TableName}",
                        $"テーブル {table.TableName} を追加",
                        new AiUpdateDiffDetailRow
                        {
                            Name = "テーブル名",
                            Before = "-",
                            After = table.TableName,
                        },
                        new AiUpdateDiffDetailRow
                        {
                            Name = "説明",
                            Before = "-",
                            After = ValueOrHyphen(table.Description),
                        },
                        new AiUpdateDiffDetailRow
                        {
                            Name = "メモ",
                            Before = "-",
                            After = ValueOrHyphen(table.Memo),
                        },
                        new AiUpdateDiffDetailRow
                        {
                            Name = "カラム数",
                            Before = "-",
                            After = table.Columns.Count.ToString(),
                        }
                    )
                );

                continue;
            }

            var tableDetails = new List<AiUpdateDiffDetailRow>();
            AddDetailIfChanged(tableDetails, "説明", currentTable.Description, table.Description);
            AddDetailIfChanged(tableDetails, "メモ", currentTable.Memo, table.Memo);

            if (tableDetails.Count > 0)
            {
                tableGroup.Items.Add(
                    CreateItem(AiUpdateDiffCategory.Table, AiUpdateDiffChangeType.Modify, $"[変更] {table.TableName}", $"テーブル {table.TableName} の変更", tableDetails.ToArray())
                );
            }

            AddColumnDiffs(columnGroup, currentTable, table);
        }

        foreach (var table in currentDiagram.Entities.Where(table => !updatedTables.ContainsKey(table.TableName)))
        {
            tableGroup.Items.Add(
                CreateItem(
                    AiUpdateDiffCategory.Table,
                    AiUpdateDiffChangeType.Remove,
                    $"[削除] {table.TableName}",
                    $"テーブル {table.TableName} を削除",
                    new AiUpdateDiffDetailRow
                    {
                        Name = "テーブル名",
                        Before = table.TableName,
                        After = "-",
                    },
                    new AiUpdateDiffDetailRow
                    {
                        Name = "説明",
                        Before = ValueOrHyphen(table.Description),
                        After = "-",
                    },
                    new AiUpdateDiffDetailRow
                    {
                        Name = "カラム数",
                        Before = table.Columns.Count.ToString(),
                        After = "-",
                    }
                )
            );
        }

        AddRelationshipDiffs(relationshipGroup, currentDiagram, updatedDiagram);

        if (tableGroup.Items.Count > 0)
        {
            result.Groups.Add(tableGroup);
        }

        if (columnGroup.Items.Count > 0)
        {
            result.Groups.Add(columnGroup);
        }

        if (relationshipGroup.Items.Count > 0)
        {
            result.Groups.Add(relationshipGroup);
        }

        return result;
    }

    /// <summary>同名テーブル内のカラム差分を追加します。</summary>
    private static void AddColumnDiffs(AiUpdateDiffGroup columnGroup, Entity currentTable, Entity updatedTable)
    {
        var currentColumns = currentTable.Columns.ToDictionary(column => NormalizeName(column.Name), StringComparer.OrdinalIgnoreCase);
        var updatedColumns = updatedTable.Columns.ToDictionary(column => NormalizeName(column.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var column in updatedTable.Columns)
        {
            if (!currentColumns.TryGetValue(NormalizeName(column.Name), out var currentColumn))
            {
                columnGroup.Items.Add(
                    CreateItem(
                        AiUpdateDiffCategory.Column,
                        AiUpdateDiffChangeType.Add,
                        $"[追加] {updatedTable.TableName}.{column.Name}",
                        $"カラム {updatedTable.TableName}.{column.Name} を追加",
                        new AiUpdateDiffDetailRow
                        {
                            Name = "テーブル",
                            Before = "-",
                            After = updatedTable.TableName,
                        },
                        new AiUpdateDiffDetailRow
                        {
                            Name = "カラム名",
                            Before = "-",
                            After = column.Name,
                        },
                        new AiUpdateDiffDetailRow
                        {
                            Name = "型",
                            Before = "-",
                            After = column.DataType,
                        },
                        new AiUpdateDiffDetailRow
                        {
                            Name = "NULL",
                            Before = "-",
                            After = ToNullableText(column.IsNullable),
                        },
                        new AiUpdateDiffDetailRow
                        {
                            Name = "PK",
                            Before = "-",
                            After = ToEnabledText(column.IsPrimaryKey),
                        },
                        new AiUpdateDiffDetailRow
                        {
                            Name = "FK",
                            Before = "-",
                            After = ToEnabledText(column.IsForeignKey),
                        }
                    )
                );

                continue;
            }

            var details = new List<AiUpdateDiffDetailRow>();
            AddDetailIfChanged(details, "型", currentColumn.DataType, column.DataType);
            AddDetailIfChanged(details, "NULL", ToNullableText(currentColumn.IsNullable), ToNullableText(column.IsNullable));
            AddDetailIfChanged(details, "PK", ToEnabledText(currentColumn.IsPrimaryKey), ToEnabledText(column.IsPrimaryKey));
            AddDetailIfChanged(details, "FK", ToEnabledText(currentColumn.IsForeignKey), ToEnabledText(column.IsForeignKey));
            AddDetailIfChanged(details, "説明", currentColumn.Description, column.Description);

            if (details.Count == 0)
            {
                continue;
            }

            columnGroup.Items.Add(
                CreateItem(
                    AiUpdateDiffCategory.Column,
                    AiUpdateDiffChangeType.Modify,
                    $"[変更] {updatedTable.TableName}.{column.Name}",
                    $"カラム {updatedTable.TableName}.{column.Name} の変更",
                    details.ToArray()
                )
            );
        }

        foreach (var column in currentTable.Columns.Where(column => !updatedColumns.ContainsKey(NormalizeName(column.Name))))
        {
            columnGroup.Items.Add(
                CreateItem(
                    AiUpdateDiffCategory.Column,
                    AiUpdateDiffChangeType.Remove,
                    $"[削除] {currentTable.TableName}.{column.Name}",
                    $"カラム {currentTable.TableName}.{column.Name} を削除",
                    new AiUpdateDiffDetailRow
                    {
                        Name = "テーブル",
                        Before = currentTable.TableName,
                        After = "-",
                    },
                    new AiUpdateDiffDetailRow
                    {
                        Name = "カラム名",
                        Before = column.Name,
                        After = "-",
                    },
                    new AiUpdateDiffDetailRow
                    {
                        Name = "型",
                        Before = column.DataType,
                        After = "-",
                    }
                )
            );
        }
    }

    /// <summary>リレーション差分を追加します。</summary>
    private static void AddRelationshipDiffs(AiUpdateDiffGroup relationshipGroup, ErDiagram currentDiagram, ErDiagram updatedDiagram)
    {
        var currentRelationships = BuildRelationshipInfo(currentDiagram);
        var updatedRelationships = BuildRelationshipInfo(updatedDiagram);
        var remainingCurrentRelationships = currentRelationships.ToList();

        foreach (var relationship in updatedRelationships)
        {
            var currentRelationship = FindRelationshipMatch(remainingCurrentRelationships, relationship);

            if (currentRelationship is null)
            {
                relationshipGroup.Items.Add(
                    CreateItem(
                        AiUpdateDiffCategory.Relationship,
                        AiUpdateDiffChangeType.Add,
                        $"[追加] {relationship.Title}",
                        $"リレーション {relationship.Title} を追加",
                        relationship.ToDetailRows("-", includeBefore: false)
                    )
                );

                continue;
            }

            remainingCurrentRelationships.Remove(currentRelationship);

            var details = new List<AiUpdateDiffDetailRow>();
            AddDetailIfChanged(details, "種別", currentRelationship.Type, relationship.Type);
            AddDetailIfChanged(details, "参照元テーブル", currentRelationship.SourceTable, relationship.SourceTable);
            AddDetailIfChanged(details, "参照元列", currentRelationship.SourceColumn, relationship.SourceColumn);
            AddDetailIfChanged(details, "参照先テーブル", currentRelationship.TargetTable, relationship.TargetTable);
            AddDetailIfChanged(details, "参照先列", currentRelationship.TargetColumn, relationship.TargetColumn);
            AddDetailIfChanged(details, "制約名", currentRelationship.ConstraintName, relationship.ConstraintName);
            AddDetailIfChanged(details, "ON DELETE", currentRelationship.OnDelete, relationship.OnDelete);
            AddDetailIfChanged(details, "ON UPDATE", currentRelationship.OnUpdate, relationship.OnUpdate);

            if (details.Count == 0)
            {
                continue;
            }

            relationshipGroup.Items.Add(
                CreateItem(
                    AiUpdateDiffCategory.Relationship,
                    AiUpdateDiffChangeType.Modify,
                    $"[変更] {relationship.Title}",
                    $"リレーション {relationship.Title} の変更",
                    details.ToArray()
                )
            );
        }

        foreach (var relationship in remainingCurrentRelationships)
        {
            relationshipGroup.Items.Add(
                CreateItem(
                    AiUpdateDiffCategory.Relationship,
                    AiUpdateDiffChangeType.Remove,
                    $"[削除] {relationship.Title}",
                    $"リレーション {relationship.Title} を削除",
                    relationship.ToDetailRows("-", includeAfter: false)
                )
            );
        }
    }

    /// <summary>比較用のリレーション情報を組み立てます。</summary>
    private static List<RelationshipInfo> BuildRelationshipInfo(ErDiagram diagram)
    {
        var entitiesById = diagram.Entities.ToDictionary(entity => entity.Id);
        var infoList = new List<RelationshipInfo>();

        foreach (var relationship in diagram.Relationships)
        {
            if (!entitiesById.TryGetValue(relationship.SourceEntityId, out var sourceEntity))
            {
                continue;
            }

            if (!entitiesById.TryGetValue(relationship.TargetEntityId, out var targetEntity))
            {
                continue;
            }

            var sourceColumn = relationship.SourceColumnId is null ? null : sourceEntity.Columns.FirstOrDefault(column => column.Id == relationship.SourceColumnId)?.Name;
            var targetColumn = relationship.TargetColumnId is null ? null : targetEntity.Columns.FirstOrDefault(column => column.Id == relationship.TargetColumnId)?.Name;
            var title = $"{sourceEntity.TableName} → {targetEntity.TableName}";
            var key = !string.IsNullOrWhiteSpace(relationship.ConstraintName)
                ? relationship.ConstraintName!
                : $"{sourceEntity.TableName}:{sourceColumn}->{targetEntity.TableName}:{targetColumn}:{relationship.Type}";

            infoList.Add(
                new RelationshipInfo(
                    NormalizeName(key),
                    title,
                    NormalizeName(sourceEntity.TableName),
                    NormalizeName(sourceColumn),
                    NormalizeName(targetEntity.TableName),
                    NormalizeName(targetColumn),
                    relationship.Type.ToString(),
                    relationship.ConstraintName,
                    relationship.OnDelete.ToSqlText(),
                    relationship.OnUpdate.ToSqlText()
                )
            );
        }

        return infoList;
    }

    /// <summary>詳細行へ差分がある項目だけ追加します。</summary>
    private static void AddDetailIfChanged(List<AiUpdateDiffDetailRow> details, string name, string? before, string? after)
    {
        if (string.Equals(before ?? string.Empty, after ?? string.Empty, StringComparison.Ordinal))
        {
            return;
        }

        details.Add(
            new AiUpdateDiffDetailRow
            {
                Name = name,
                Before = ValueOrHyphen(before),
                After = ValueOrHyphen(after),
            }
        );
    }

    /// <summary>差分項目を作成します。</summary>
    private static AiUpdateDiffItem CreateItem(
        AiUpdateDiffCategory category,
        AiUpdateDiffChangeType changeType,
        string summary,
        string title,
        params AiUpdateDiffDetailRow[] details
    )
    {
        return new AiUpdateDiffItem
        {
            Category = category,
            ChangeType = changeType,
            Summary = summary,
            Title = title,
            Details = details.ToList(),
        };
    }

    /// <summary>空文字をハイフンへ変換します。</summary>
    private static string ValueOrHyphen(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

    /// <summary>比較用に名前を正規化します。</summary>
    private static string NormalizeName(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    /// <summary>NULL 許容の表示文字列です。</summary>
    private static string ToNullableText(bool isNullable) => isNullable ? "許可" : "禁止";

    /// <summary>フラグの表示文字列です。</summary>
    private static string ToEnabledText(bool enabled) => enabled ? "あり" : "なし";

    /// <summary>比較用のリレーション情報です。</summary>
    private sealed record RelationshipInfo(
        string Key,
        string Title,
        string SourceTable,
        string? SourceColumn,
        string TargetTable,
        string? TargetColumn,
        string Type,
        string? ConstraintName,
        string OnDelete,
        string OnUpdate
    )
    {
        /// <summary>詳細比較用の行を返します。</summary>
        public AiUpdateDiffDetailRow[] ToDetailRows(string placeholder, bool includeBefore = true, bool includeAfter = true)
        {
            return
            [
                new AiUpdateDiffDetailRow
                {
                    Name = "種別",
                    Before = includeBefore ? Type : placeholder,
                    After = includeAfter ? Type : placeholder,
                },
                new AiUpdateDiffDetailRow
                {
                    Name = "参照元テーブル",
                    Before = includeBefore ? SourceTable : placeholder,
                    After = includeAfter ? SourceTable : placeholder,
                },
                new AiUpdateDiffDetailRow
                {
                    Name = "参照元列",
                    Before = includeBefore ? ValueOrHyphen(SourceColumn) : placeholder,
                    After = includeAfter ? ValueOrHyphen(SourceColumn) : placeholder,
                },
                new AiUpdateDiffDetailRow
                {
                    Name = "参照先テーブル",
                    Before = includeBefore ? TargetTable : placeholder,
                    After = includeAfter ? TargetTable : placeholder,
                },
                new AiUpdateDiffDetailRow
                {
                    Name = "参照先列",
                    Before = includeBefore ? ValueOrHyphen(TargetColumn) : placeholder,
                    After = includeAfter ? ValueOrHyphen(TargetColumn) : placeholder,
                },
                new AiUpdateDiffDetailRow
                {
                    Name = "制約名",
                    Before = includeBefore ? ValueOrHyphen(ConstraintName) : placeholder,
                    After = includeAfter ? ValueOrHyphen(ConstraintName) : placeholder,
                },
                new AiUpdateDiffDetailRow
                {
                    Name = "ON DELETE",
                    Before = includeBefore ? OnDelete : placeholder,
                    After = includeAfter ? OnDelete : placeholder,
                },
                new AiUpdateDiffDetailRow
                {
                    Name = "ON UPDATE",
                    Before = includeBefore ? OnUpdate : placeholder,
                    After = includeAfter ? OnUpdate : placeholder,
                },
            ];
        }

        /// <summary>変更判定用の構造キーです。</summary>
        public string StructuralKey => $"{SourceTable}:{SourceColumn}->{TargetTable}:{TargetColumn}";

        /// <summary>同一テーブル組み合わせ判定用のキーです。</summary>
        public string TablePairKey => $"{SourceTable}->{TargetTable}";
    }

    /// <summary>削除/追加ではなく変更として扱える既存リレーションを探します。</summary>
    private static RelationshipInfo? FindRelationshipMatch(List<RelationshipInfo> currentRelationships, RelationshipInfo updatedRelationship)
    {
        var exactStructureMatch = currentRelationships.FirstOrDefault(currentRelationship =>
            string.Equals(currentRelationship.StructuralKey, updatedRelationship.StructuralKey, StringComparison.OrdinalIgnoreCase)
        );

        if (exactStructureMatch is not null)
        {
            return exactStructureMatch;
        }

        if (!string.IsNullOrWhiteSpace(updatedRelationship.Key))
        {
            var constraintMatch = currentRelationships.FirstOrDefault(currentRelationship =>
                !string.IsNullOrWhiteSpace(currentRelationship.Key) && string.Equals(currentRelationship.Key, updatedRelationship.Key, StringComparison.OrdinalIgnoreCase)
            );

            if (constraintMatch is not null)
            {
                return constraintMatch;
            }
        }

        var sameTablePairRelationships = currentRelationships
            .Where(currentRelationship => string.Equals(currentRelationship.TablePairKey, updatedRelationship.TablePairKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sameTablePairRelationships.Count == 1)
        {
            return sameTablePairRelationships[0];
        }

        return sameTablePairRelationships
            .OrderByDescending(currentRelationship => CalculateRelationshipSimilarity(currentRelationship, updatedRelationship))
            .FirstOrDefault(currentRelationship => CalculateRelationshipSimilarity(currentRelationship, updatedRelationship) > 0);
    }

    /// <summary>リレーションの類似度を計算します。</summary>
    private static int CalculateRelationshipSimilarity(RelationshipInfo currentRelationship, RelationshipInfo updatedRelationship)
    {
        var score = 0;

        if (string.Equals(currentRelationship.SourceColumn, updatedRelationship.SourceColumn, StringComparison.OrdinalIgnoreCase))
        {
            score += 4;
        }

        if (string.Equals(currentRelationship.TargetColumn, updatedRelationship.TargetColumn, StringComparison.OrdinalIgnoreCase))
        {
            score += 4;
        }

        if (string.Equals(currentRelationship.Type, updatedRelationship.Type, StringComparison.OrdinalIgnoreCase))
        {
            score += 2;
        }

        if (string.Equals(currentRelationship.ConstraintName, updatedRelationship.ConstraintName, StringComparison.OrdinalIgnoreCase))
        {
            score += 1;
        }

        return score;
    }
}
