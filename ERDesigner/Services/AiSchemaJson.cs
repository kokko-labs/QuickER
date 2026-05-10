using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ERDesigner.Models;

namespace ERDesigner.Services;

/// <summary>
/// LLM から受け取る JSON 形式のスキーマ定義 (POCO)。
/// </summary>
public class AiSchemaJson
{
    /// <summary>テーブル一覧。</summary>
    [JsonPropertyName("tables")]
    public List<AiTable> Tables { get; set; } = new();

    /// <summary>テーブル間のリレーション一覧。</summary>
    [JsonPropertyName("relationships")]
    public List<AiRelationship> Relationships { get; set; } = new();

    /// <summary>既存 ER 図を AI 入力用の簡潔な JSON 表現へ変換します。</summary>
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
                    TargetTable = entityById[relationship.TargetEntityId].TableName,
                    Type = relationship.Type.ToString(),
                    ConstraintName = relationship.ConstraintName,
                    OnDelete = relationship.OnDelete.ToSqlText(),
                    OnUpdate = relationship.OnUpdate.ToSqlText(),
                })
                .ToList(),
        };
    }

    /// <summary>テーブル名を指定した単複数へ正規化します。</summary>
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

    /// <summary>テーブル名・カラム名を指定した命名規則へ正規化します。</summary>
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
        }
    }

    /// <summary>JSON 表現を ER 図ドメインモデル (<see cref="Entity"/>, <see cref="Relationship"/>) に変換します。</summary>
    public (List<Entity> Entities, List<Relationship> Relationships) ToDomain()
    {
        var entities = new List<Entity>();
        var byTable = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
        var byTableColumns = new Dictionary<string, Dictionary<string, Column>>(StringComparer.OrdinalIgnoreCase);

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
                        IsForeignKey = c.IsForeignKey,
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
            byTableColumns[entity.TableName] = columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
        }

        var relationships = new List<Relationship>();

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

            relationships.Add(
                new Relationship
                {
                    SourceEntityId = s.Id,
                    TargetEntityId = t.Id,
                    Type = ParseType(r.Type),
                    SourceColumnId = ResolveSourceColumnId(s, r),
                    TargetColumnId = ResolveTargetColumnId(t, r, byTableColumns),
                    ConstraintName = r.ConstraintName,
                    OnDelete = ForeignKeyReferentialActionHelper.Parse(r.OnDelete),
                    OnUpdate = ForeignKeyReferentialActionHelper.Parse(r.OnUpdate),
                }
            );
        }

        return (entities, relationships);
    }

    /// <summary>リレーションの参照先 PK 列を解決します。</summary>
    private static Guid? ResolveSourceColumnId(Entity sourceEntity, AiRelationship relationship)
    {
        var sourceColumnName = ExtractSourceColumnName(relationship.ConstraintName, sourceEntity.TableName);

        if (!string.IsNullOrWhiteSpace(sourceColumnName))
        {
            var matchedSourceColumn = sourceEntity.Columns.FirstOrDefault(column => string.Equals(column.Name, sourceColumnName, StringComparison.OrdinalIgnoreCase));

            if (matchedSourceColumn is not null)
            {
                return matchedSourceColumn.Id;
            }
        }

        return sourceEntity.Columns.FirstOrDefault(column => column.IsPrimaryKey)?.Id;
    }

    /// <summary>リレーションの終点側 FK 列を解決します。</summary>
    private static Guid? ResolveTargetColumnId(Entity targetEntity, AiRelationship relationship, IReadOnlyDictionary<string, Dictionary<string, Column>> byTableColumns)
    {
        if (!string.IsNullOrWhiteSpace(relationship.SourceTable) && byTableColumns.TryGetValue(targetEntity.TableName, out var targetColumns))
        {
            var preferredTargetColumn = ResolveForeignKeyColumnName(relationship.SourceTable, targetColumns.Keys);

            if (!string.IsNullOrWhiteSpace(preferredTargetColumn) && targetColumns.TryGetValue(preferredTargetColumn, out var targetColumn))
            {
                return targetColumn.Id;
            }

            var foreignKeyColumn = targetColumns.Values.FirstOrDefault(column => column.IsForeignKey);

            if (foreignKeyColumn is not null)
            {
                return foreignKeyColumn.Id;
            }
        }

        return targetEntity.Columns.FirstOrDefault(column => column.IsForeignKey)?.Id ?? targetEntity.Columns.FirstOrDefault(column => !column.IsPrimaryKey)?.Id;
    }

    /// <summary>制約名から参照先の列名候補を抽出します。</summary>
    private static string? ExtractSourceColumnName(string? constraintName, string sourceTableName)
    {
        if (string.IsNullOrWhiteSpace(constraintName))
        {
            return null;
        }

        var normalizedTableName = Regex.Replace(sourceTableName, "[^A-Za-z0-9]", string.Empty, RegexOptions.CultureInvariant);
        var normalizedConstraintName = Regex.Replace(constraintName, "[^A-Za-z0-9]", string.Empty, RegexOptions.CultureInvariant);

        return normalizedConstraintName.Contains(normalizedTableName, StringComparison.OrdinalIgnoreCase) ? sourceTableName + "Id" : null;
    }

    /// <summary>終点テーブルのカラム一覧から、参照元テーブルに対応する FK 列名候補を求めます。</summary>
    private static string? ResolveForeignKeyColumnName(string sourceTableName, IEnumerable<string> targetColumnNames)
    {
        var sourceWords = SplitIdentifierWords(sourceTableName);

        if (sourceWords.Count == 0)
        {
            return null;
        }

        var candidates = new[]
        {
            string.Concat(sourceWords.Select(ToPascalWord)) + "Id",
            string.Join("_", sourceWords.Select(static word => word.ToLowerInvariant())) + "_id",
            sourceWords[^1] + "Id",
            ToPascalWord(sourceWords[^1]) + "Id",
        };

        return candidates.FirstOrDefault(candidate => targetColumnNames.Any(columnName => string.Equals(columnName, candidate, StringComparison.OrdinalIgnoreCase)));
    }

    private static RelationshipType ParseType(string? type) =>
        type switch
        {
            "OneToOne" or "1:1" or "one-to-one" => RelationshipType.OneToOne,
            "ManyToMany" or "N:N" or "many-to-many" or "M:N" => RelationshipType.ManyToMany,
            _ => RelationshipType.OneToMany,
        };

    /// <summary>任意の識別子文字列を指定の命名規則へ変換します。</summary>
    private static string ConvertIdentifier(string value, AiIdentifierNamingStyle namingStyle)
    {
        var words = SplitIdentifierWords(value);

        if (words.Count == 0)
        {
            return namingStyle == AiIdentifierNamingStyle.SnakeCase ? "table_name" : "TableName";
        }

        return namingStyle switch
        {
            AiIdentifierNamingStyle.SnakeCase => string.Join("_", words.Select(static word => word.ToLowerInvariant())),
            _ => string.Concat(words.Select(ToPascalWord)),
        };
    }

    /// <summary>テーブル名の末尾単語を単数形または複数形へ変換します。</summary>
    private static string ConvertTableNameNumber(string value, AiTableNameNumberStyle numberStyle)
    {
        var words = SplitIdentifierWords(value);

        if (words.Count == 0)
        {
            return value;
        }

        words[^1] = numberStyle == AiTableNameNumberStyle.Plural ? PluralizeWord(words[^1]) : SingularizeWord(words[^1]);

        return string.Join("_", words);
    }

    /// <summary>スネークケースやパスカルケースを単語列へ分解します。</summary>
    private static List<string> SplitIdentifierWords(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var normalized = Regex.Replace(value.Trim(), @"[^A-Za-z0-9]+", " ");
        normalized = Regex.Replace(normalized, @"([A-Z]+)([A-Z][a-z])", "$1 $2");
        normalized = Regex.Replace(normalized, @"([a-z0-9])([A-Z])", "$1 $2");
        normalized = Regex.Replace(normalized, @"([A-Za-z])([0-9])", "$1 $2");
        normalized = Regex.Replace(normalized, @"([0-9])([A-Za-z])", "$1 $2");

        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(static word => word.Length > 0).ToList();
    }

    /// <summary>末尾単語が複数形らしいかを判定します。</summary>
    private static bool IsLikelyPlural(string word)
    {
        if (word.Length <= 1)
        {
            return false;
        }

        var lower = word.ToLowerInvariant();

        if (
            lower.EndsWith("ies", StringComparison.Ordinal)
            || lower.EndsWith("ses", StringComparison.Ordinal)
            || lower.EndsWith("xes", StringComparison.Ordinal)
            || lower.EndsWith("zes", StringComparison.Ordinal)
            || lower.EndsWith("ches", StringComparison.Ordinal)
            || lower.EndsWith("shes", StringComparison.Ordinal)
            || lower.EndsWith("oes", StringComparison.Ordinal)
        )
        {
            return true;
        }

        return lower.EndsWith('s')
            && !lower.EndsWith("ss", StringComparison.Ordinal)
            && !lower.EndsWith("us", StringComparison.Ordinal)
            && !lower.EndsWith("is", StringComparison.Ordinal);
    }

    /// <summary>単語を単数形へ寄せます。</summary>
    private static string SingularizeWord(string word)
    {
        if (!IsLikelyPlural(word))
        {
            return word;
        }

        var lower = word.ToLowerInvariant();

        if (lower.EndsWith("ies", StringComparison.Ordinal) && word.Length > 3)
        {
            return word[..^3] + "y";
        }

        if (
            lower.EndsWith("ches", StringComparison.Ordinal)
            || lower.EndsWith("shes", StringComparison.Ordinal)
            || lower.EndsWith("xes", StringComparison.Ordinal)
            || lower.EndsWith("zes", StringComparison.Ordinal)
            || lower.EndsWith("ses", StringComparison.Ordinal)
            || lower.EndsWith("oes", StringComparison.Ordinal)
        )
        {
            return word[..^2];
        }

        return word[..^1];
    }

    /// <summary>単語を複数形へ寄せます。</summary>
    private static string PluralizeWord(string word)
    {
        if (IsLikelyPlural(word))
        {
            return word;
        }

        var lower = word.ToLowerInvariant();

        if (lower.EndsWith('y') && word.Length > 1)
        {
            var beforeLast = char.ToLowerInvariant(word[^2]);

            if (beforeLast is not ('a' or 'e' or 'i' or 'o' or 'u'))
            {
                return word[..^1] + "ies";
            }
        }

        if (
            lower.EndsWith('s')
            || lower.EndsWith('x')
            || lower.EndsWith('z')
            || lower.EndsWith("ch", StringComparison.Ordinal)
            || lower.EndsWith("sh", StringComparison.Ordinal)
            || lower.EndsWith('o')
        )
        {
            return word + "es";
        }

        return word + "s";
    }

    /// <summary>単語をパスカルケース用の表記へ整えます。</summary>
    private static string ToPascalWord(string word)
    {
        if (word.Length == 0)
        {
            return string.Empty;
        }

        if (word.Length == 1)
        {
            return word.ToUpperInvariant();
        }

        return char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
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
