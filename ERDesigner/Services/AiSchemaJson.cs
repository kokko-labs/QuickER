using System.Text.Json.Serialization;
using ERDesigner.Models;

namespace ERDesigner.Services;

/// <summary>
/// LLM とやり取りする JSON 形式のスキーマ定義 (POCO)
/// </summary>
public class AiSchemaJson
{
    /// <summary>テーブル一覧</summary>
    [JsonPropertyName("tables")]
    public List<AiTable> Tables { get; set; } = new();

    /// <summary>テーブル間のリレーション一覧</summary>
    [JsonPropertyName("relationships")]
    public List<AiRelationship> Relationships { get; set; } = new();

    /// <summary>既存 ER 図を AI 入力用の簡潔な JSON 表現へ変換する</summary>
    /// <remarks>参照元・参照先エンティティのどちらかが存在しないリレーションは除外する</remarks>
    public static AiSchemaJson FromDiagram(ErDiagram diagram)
    {
        var entities = diagram.Entities ?? [];
        var relationships = diagram.Relationships ?? [];
        var entityById = entities.ToDictionary(entity => entity.Id);

        return new AiSchemaJson
        {
            Tables = entities
                .Select(entity => new AiTable
                {
                    Name = entity.TableName,
                    Description = entity.Description,
                    Memo = entity.Memo,
                    Columns = entity
                        .Columns.Select(column => new AiColumn
                        {
                            Name = column.Name,
                            DataType = column.DataType,
                            IsPrimaryKey = column.IsPrimaryKey,
                            IsForeignKey = column.IsForeignKey,
                            IsNullable = column.IsPrimaryKey ? false : column.IsNullable,
                            Description = column.Description,
                        })
                        .ToList(),
                })
                .ToList(),
            Relationships = relationships
                .Where(relationship => entityById.ContainsKey(relationship.SourceEntityId) && entityById.ContainsKey(relationship.TargetEntityId))
                .Select(relationship => new AiRelationship
                {
                    SourceTable = entityById[relationship.SourceEntityId].TableName,
                    SourceColumn = FindColumnNameById(entityById[relationship.SourceEntityId], relationship.SourceColumnId),
                    TargetTable = entityById[relationship.TargetEntityId].TableName,
                    TargetColumn = FindColumnNameById(entityById[relationship.TargetEntityId], relationship.TargetColumnId),
                    Type = relationship.Type.ToString(),
                    ConstraintName = relationship.ConstraintName,
                    OnDelete = relationship.OnDelete.ToSqlText(),
                    OnUpdate = relationship.OnUpdate.ToSqlText(),
                })
                .ToList(),
        };
    }

    /// <summary>全テーブル名を指定した単数形・複数形へ正規化する</summary>
    /// <remarks>リレーションが参照するテーブル名も追従して書き換える</remarks>
    public void NormalizeTableNames(AiTableNameNumberStyle numberStyle)
    {
        var tableNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in Tables)
        {
            if (string.IsNullOrWhiteSpace(table.Name))
            {
                continue;
            }

            var normalizedTableName = ConvertTableNameNumber(table.Name, numberStyle);
            tableNameMap[table.Name] = normalizedTableName;
            table.Name = normalizedTableName;
        }

        foreach (var relationship in Relationships)
        {
            if (!string.IsNullOrWhiteSpace(relationship.SourceTable))
            {
                relationship.SourceTable = tableNameMap.TryGetValue(relationship.SourceTable, out var sourceTableName)
                    ? sourceTableName
                    : ConvertTableNameNumber(relationship.SourceTable, numberStyle);
            }

            if (!string.IsNullOrWhiteSpace(relationship.TargetTable))
            {
                relationship.TargetTable = tableNameMap.TryGetValue(relationship.TargetTable, out var targetTableName)
                    ? targetTableName
                    : ConvertTableNameNumber(relationship.TargetTable, numberStyle);
            }
        }
    }

    /// <summary>全テーブル名・カラム名を指定した命名規則へ正規化する</summary>
    /// <remarks>リレーションが参照するテーブル名も追従して書き換える</remarks>
    public void NormalizeIdentifiers(AiIdentifierNamingStyle namingStyle)
    {
        var tableNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in Tables)
        {
            if (string.IsNullOrWhiteSpace(table.Name))
            {
                continue;
            }

            var normalizedTableName = ConvertIdentifier(table.Name, namingStyle);
            tableNameMap[table.Name] = normalizedTableName;
            table.Name = normalizedTableName;

            if (table.Columns is null)
            {
                continue;
            }

            foreach (var column in table.Columns)
            {
                if (string.IsNullOrWhiteSpace(column.Name))
                {
                    continue;
                }

                column.Name = ConvertIdentifier(column.Name, namingStyle);
            }
        }

        foreach (var relationship in Relationships)
        {
            if (!string.IsNullOrWhiteSpace(relationship.SourceTable))
            {
                relationship.SourceTable = tableNameMap.TryGetValue(relationship.SourceTable, out var sourceTableName)
                    ? sourceTableName
                    : ConvertIdentifier(relationship.SourceTable, namingStyle);
            }

            if (!string.IsNullOrWhiteSpace(relationship.TargetTable))
            {
                relationship.TargetTable = tableNameMap.TryGetValue(relationship.TargetTable, out var targetTableName)
                    ? targetTableName
                    : ConvertIdentifier(relationship.TargetTable, namingStyle);
            }

            // カラム名も命名規則変換に追従させ、AI が明示した列参照が解決できなくなるのを防ぐ
            if (!string.IsNullOrWhiteSpace(relationship.SourceColumn))
            {
                relationship.SourceColumn = ConvertIdentifier(relationship.SourceColumn, namingStyle);
            }

            if (!string.IsNullOrWhiteSpace(relationship.TargetColumn))
            {
                relationship.TargetColumn = ConvertIdentifier(relationship.TargetColumn, namingStyle);
            }
        }
    }

    /// <summary>JSON 表現を ER 図のドメインモデル (<see cref="Entity"/>, <see cref="Relationship"/>) へ変換する</summary>
    /// <remarks>AI 出力の揺れ (PK と FK の重複指定、isForeignKey の付け忘れ等) はこの変換時に矯正する</remarks>
    public (List<Entity> Entities, List<Relationship> Relationships) ToDomain()
    {
        var entities = new List<Entity>();
        var byTable = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in Tables)
        {
            if (string.IsNullOrWhiteSpace(table.Name))
            {
                continue;
            }

            var columns =
                table
                    .Columns?.Where(static column => !string.IsNullOrWhiteSpace(column.Name))
                    .Select(static c => new Column
                    {
                        Name = c.Name ?? "Column",
                        DataType = string.IsNullOrWhiteSpace(c.DataType) ? "int" : c.DataType,
                        IsPrimaryKey = c.IsPrimaryKey,
                        // isPrimaryKey=true かつ isForeignKey=true は AI の誤出力なので FK を無効化する
                        IsForeignKey = c.IsForeignKey && !c.IsPrimaryKey,
                        IsNullable = c.IsPrimaryKey ? false : c.IsNullable,
                        Description = c.Description ?? string.Empty,
                    })
                    .ToList()
                ?? new List<Column>();

            var entity = new Entity
            {
                TableName = table.Name,
                Description = table.Description ?? string.Empty,
                Memo = table.Memo ?? string.Empty,
                Columns = columns,
            };

            entities.Add(entity);
            byTable[entity.TableName] = entity;
        }

        var relationships = new List<Relationship>();
        var usedTargetColumnIds = new HashSet<Guid>();

        foreach (var r in Relationships)
        {
            if (r.SourceTable is null || r.TargetTable is null)
            {
                continue;
            }

            if (!byTable.TryGetValue(r.SourceTable, out var s))
            {
                continue;
            }

            if (!byTable.TryGetValue(r.TargetTable, out var t))
            {
                continue;
            }

            // 参照元は AI が sourceColumn で明示した列を最優先し、無ければ PK 列とする
            var sourceColumn = FindColumnByName(s, r.SourceColumn) ?? s.Columns.FirstOrDefault(column => column.IsPrimaryKey);

            // 参照先は AI が targetColumn で明示した列を設定を書き換えずそのまま採用し、無ければ共通リゾルバで解決する
            var targetColumnId = FindColumnByName(t, r.TargetColumn)?.Id ?? ResolveTargetColumnByHeuristic(s, t, sourceColumn, usedTargetColumnIds);

            // 後続リレーションの解決で同じ列を重複割当しないよう記録する
            if (targetColumnId is not null)
            {
                usedTargetColumnIds.Add(targetColumnId.Value);
            }

            relationships.Add(
                new Relationship
                {
                    SourceEntityId = s.Id,
                    TargetEntityId = t.Id,
                    Type = ParseType(r.Type),
                    SourceColumnId = sourceColumn?.Id,
                    TargetColumnId = targetColumnId,
                    ConstraintName = r.ConstraintName,
                    OnDelete = ForeignKeyReferentialActionHelper.Parse(r.OnDelete),
                    OnUpdate = ForeignKeyReferentialActionHelper.Parse(r.OnUpdate),
                }
            );
        }

        return (entities, relationships);
    }

    /// <summary>リレーションの参照先 (子) テーブルの FK 列を共通リゾルバで解決し、採用した列に FK フラグを設定する</summary>
    /// <remarks>
    /// AI の誤出力（FK 名の列への isPrimaryKey 付与）を矯正してから <see cref="ForeignKeyColumnResolver"/> へ委譲する。
    /// 該当列が無ければ列未割当（null）とし、無関係な列を FK へ書き換えない
    /// </remarks>
    private static Guid? ResolveTargetColumnByHeuristic(Entity sourceEntity, Entity targetEntity, Column? sourceKeyColumn, HashSet<Guid> usedTargetColumnIds)
    {
        DemoteMisflaggedPrimaryKey(sourceEntity, targetEntity);

        var candidates = targetEntity
            .Columns.Select(column => new ForeignKeyColumnResolver.CandidateColumn(
                column.Name,
                column.IsPrimaryKey,
                column.IsForeignKey,
                column.DataType,
                usedTargetColumnIds.Contains(column.Id)
            ))
            .ToList();

        var index = ForeignKeyColumnResolver.ResolveTargetColumnIndex(
            sourceEntity.TableName,
            sourceKeyColumn?.Name,
            sourceKeyColumn?.DataType,
            candidates,
            ReferenceEquals(sourceEntity, targetEntity)
        );

        if (index is null)
        {
            return null;
        }

        var column = targetEntity.Columns[index.Value];
        column.IsForeignKey = true;
        return column.Id;
    }

    /// <summary>FK 名の列へ AI が誤って付けた isPrimaryKey を矯正する</summary>
    /// <remarks>他に PK 列が存在する場合のみ「AI の誤付与」とみなして PK を降ろす（唯一の PK は維持する）</remarks>
    private static void DemoteMisflaggedPrimaryKey(Entity sourceEntity, Entity targetEntity)
    {
        var expectedNames = ForeignKeyColumnResolver.BuildExpectedForeignKeyNames(sourceEntity.TableName);
        var misflagged = targetEntity.Columns.FirstOrDefault(column => column.IsPrimaryKey && expectedNames.Contains(column.Name, StringComparer.OrdinalIgnoreCase));

        if (misflagged is not null && targetEntity.Columns.Any(column => column.Id != misflagged.Id && column.IsPrimaryKey))
        {
            misflagged.IsPrimaryKey = false;
        }
    }

    /// <summary>エンティティから指定名のカラムを大文字小文字を無視して検索する（未指定・未存在なら null）</summary>
    private static Column? FindColumnByName(Entity entity, string? columnName)
    {
        return string.IsNullOrWhiteSpace(columnName) ? null : entity.Columns.FirstOrDefault(column => string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>カラム ID からカラム名を逆引きする（未割当・未存在なら null）</summary>
    private static string? FindColumnNameById(Entity entity, Guid? columnId)
    {
        return columnId is null ? null : entity.Columns.FirstOrDefault(column => column.Id == columnId)?.Name;
    }

    /// <summary>AI が返すリレーション種別文字列を <see cref="RelationshipType"/> へ変換する (表記揺れを許容し、不明値は OneToMany 扱い)</summary>
    private static RelationshipType ParseType(string? type) =>
        type switch
        {
            "OneToOne" or "1:1" or "one-to-one" => RelationshipType.OneToOne,
            "ManyToMany" or "N:N" or "many-to-many" or "M:N" => RelationshipType.ManyToMany,
            _ => RelationshipType.OneToMany,
        };

    /// <summary>任意の識別子文字列を指定の命名規則へ変換する</summary>
    private static string ConvertIdentifier(string value, AiIdentifierNamingStyle namingStyle)
    {
        var words = IdentifierNameHelper.SplitIdentifierWords(value);

        if (words.Count == 0)
        {
            return namingStyle == AiIdentifierNamingStyle.SnakeCase ? "table_name" : "TableName";
        }

        return namingStyle switch
        {
            AiIdentifierNamingStyle.SnakeCase => string.Join("_", words.Select(static word => word.ToLowerInvariant())),
            _ => string.Concat(words.Select(IdentifierNameHelper.ToPascalWord)),
        };
    }

    /// <summary>テーブル名の末尾単語を指定の単数形・複数形へ変換する</summary>
    /// <remarks>単語の区切り直しによりアンダースコア結合へ正規化されるため、命名規則の変換 (<see cref="ConvertIdentifier"/>) と併用する前提</remarks>
    private static string ConvertTableNameNumber(string value, AiTableNameNumberStyle numberStyle)
    {
        var words = IdentifierNameHelper.SplitIdentifierWords(value);

        if (words.Count == 0)
        {
            return value;
        }

        words[^1] = numberStyle == AiTableNameNumberStyle.Plural ? IdentifierNameHelper.PluralizeWord(words[^1]) : IdentifierNameHelper.SingularizeWord(words[^1]);

        return string.Join("_", words);
    }
}

/// <summary>AI が返すテーブル。</summary>
public class AiTable
{
    /// <summary>テーブル名。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>テーブルの説明。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>tableDescription 形式との互換用。</summary>
    [JsonPropertyName("tableDescription")]
    public string? TableDescription
    {
        get => Description;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Description = value;
            }
        }
    }

    /// <summary>備考。</summary>
    [JsonPropertyName("memo")]
    public string? Memo { get; set; }

    /// <summary>カラム一覧。</summary>
    [JsonPropertyName("columns")]
    public List<AiColumn>? Columns { get; set; }
}

/// <summary>AI が返すカラム。</summary>
public class AiColumn
{
    /// <summary>カラム名。</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>SQL Server のデータ型。</summary>
    [JsonPropertyName("dataType")]
    public string? DataType { get; set; }

    /// <summary>主キーかどうか。</summary>
    [JsonPropertyName("isPrimaryKey")]
    public bool IsPrimaryKey { get; set; }

    /// <summary>外部キーかどうか。</summary>
    [JsonPropertyName("isForeignKey")]
    public bool IsForeignKey { get; set; }

    /// <summary>NULL を許容するかどうか。</summary>
    [JsonPropertyName("isNullable")]
    public bool IsNullable { get; set; } = true;

    /// <summary>nullable 形式との互換用。</summary>
    [JsonPropertyName("nullable")]
    public bool? Nullable
    {
        get => null;
        set
        {
            if (value.HasValue)
            {
                IsNullable = value.Value;
            }
        }
    }

    /// <summary>allowNull 形式との互換用。</summary>
    [JsonPropertyName("allowNull")]
    public bool? AllowNull
    {
        get => null;
        set
        {
            if (value.HasValue)
            {
                IsNullable = value.Value;
            }
        }
    }

    /// <summary>required 形式との互換用。</summary>
    [JsonPropertyName("required")]
    public bool? Required
    {
        get => null;
        set
        {
            if (value.HasValue)
            {
                IsNullable = !value.Value;
            }
        }
    }

    /// <summary>カラムの説明。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>columnDescription 形式との互換用。</summary>
    [JsonPropertyName("columnDescription")]
    public string? ColumnDescription
    {
        get => Description;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Description = value;
            }
        }
    }
}

/// <summary>AI が返すリレーション。</summary>
public class AiRelationship
{
    /// <summary>起点テーブル名。</summary>
    [JsonPropertyName("sourceTable")]
    public string? SourceTable { get; set; }

    /// <summary>fromTable 形式との互換用。</summary>
    [JsonPropertyName("fromTable")]
    public string? FromTable
    {
        get => SourceTable;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                SourceTable = value;
            }
        }
    }

    /// <summary>起点（親）テーブルの参照列名。通常は主キー列。</summary>
    [JsonPropertyName("sourceColumn")]
    public string? SourceColumn { get; set; }

    /// <summary>終点テーブル名。</summary>
    [JsonPropertyName("targetTable")]
    public string? TargetTable { get; set; }

    /// <summary>toTable 形式との互換用。</summary>
    [JsonPropertyName("toTable")]
    public string? ToTable
    {
        get => TargetTable;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                TargetTable = value;
            }
        }
    }

    /// <summary>終点（子）テーブルの外部キー列名。</summary>
    [JsonPropertyName("targetColumn")]
    public string? TargetColumn { get; set; }

    /// <summary>関連の種類 (OneToOne / OneToMany / ManyToMany)。</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>制約名です。</summary>
    [JsonPropertyName("constraintName")]
    public string? ConstraintName { get; set; }

    /// <summary>削除時の参照アクションです。</summary>
    [JsonPropertyName("onDelete")]
    public string? OnDelete { get; set; }

    /// <summary>更新時の参照アクションです。</summary>
    [JsonPropertyName("onUpdate")]
    public string? OnUpdate { get; set; }
}
