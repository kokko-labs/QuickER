using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
                    TargetTable = entityById[relationship.TargetEntityId].TableName,
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
                    SourceColumnId = ResolveSourceColumnId(s),
                    TargetColumnId = ResolveTargetColumnId(s, t),
                    ConstraintName = r.ConstraintName,
                    OnDelete = ForeignKeyReferentialActionHelper.Parse(r.OnDelete),
                    OnUpdate = ForeignKeyReferentialActionHelper.Parse(r.OnUpdate),
                }
            );
        }

        return (entities, relationships);
    }

    /// <summary>リレーションの参照元 (親) テーブルの PK 列を解決する</summary>
    private static Guid? ResolveSourceColumnId(Entity sourceEntity)
    {
        return sourceEntity.Columns.FirstOrDefault(column => column.IsPrimaryKey)?.Id;
    }

    /// <summary>リレーションの参照先 (子) テーブルの FK 列を解決する</summary>
    /// <remarks>①命名規則一致 → ②isForeignKey 指定 → ③FK らしい名前 → ④先頭の非 PK 列、の優先順で探索し、採用した列には FK フラグと NOT NULL を設定する</remarks>
    private static Guid? ResolveTargetColumnId(Entity sourceEntity, Entity targetEntity)
    {
        var preferredColumnName = ResolveForeignKeyColumnName(sourceEntity.TableName);

        // ① preferredColumnName（例: CustomerId）と完全一致する列を優先的に FK として採用する
        //    AI が誤って isPrimaryKey=true を付けた場合も、他に PK 列が存在すれば PK フラグを降ろして矯正する
        var preferredColumn = targetEntity.Columns.FirstOrDefault(column => string.Equals(column.Name, preferredColumnName, StringComparison.OrdinalIgnoreCase));

        if (preferredColumn is not null)
        {
            if (preferredColumn.IsPrimaryKey)
            {
                // 他に PK 列があれば「AI の誤付与」とみなして PK を降ろす
                var hasPkOtherThanPreferred = targetEntity.Columns.Any(column => column.Id != preferredColumn.Id && column.IsPrimaryKey);

                if (hasPkOtherThanPreferred)
                {
                    preferredColumn.IsPrimaryKey = false;
                }
                else
                {
                    // 唯一の PK 列なので FK として扱わない（次の検索へ委ねる）
                    preferredColumn = null;
                }
            }

            if (preferredColumn is not null)
            {
                preferredColumn.IsForeignKey = true;
                preferredColumn.IsNullable = false;
                return preferredColumn.Id;
            }
        }

        // ② isForeignKey=true かつ非 PK 列を FK 候補とする（AI が正しく設定した場合）
        var foreignKeyColumn = targetEntity.Columns.FirstOrDefault(column => column.IsForeignKey && !column.IsPrimaryKey);

        if (foreignKeyColumn is not null)
        {
            return foreignKeyColumn.Id;
        }

        // ③ 「他テーブル名+Id」形式の非 PK 列を FK 候補とみなす（AI が isForeignKey を付け忘れたケース）
        var fkPatternColumn = targetEntity.Columns.FirstOrDefault(column => !column.IsPrimaryKey && IsLikelyForeignKeyName(column.Name));

        if (fkPatternColumn is not null)
        {
            fkPatternColumn.IsForeignKey = true;
            fkPatternColumn.IsNullable = false;
            return fkPatternColumn.Id;
        }

        // ④ 最終フォールバック: 最初の非 PK 列を FK に設定する
        var fallbackColumn = targetEntity.Columns.FirstOrDefault(column => !column.IsPrimaryKey);

        if (fallbackColumn is null)
        {
            return null;
        }

        fallbackColumn.IsForeignKey = true;
        fallbackColumn.IsNullable = false;
        return fallbackColumn.Id;
    }

    /// <summary>列名が「テーブル名+Id」形式の外部キーらしい名前かどうかを判定する</summary>
    private static bool IsLikelyForeignKeyName(string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return false;
        }

        // パスカルケース: XxxId / スネークケース: xxx_id
        return Regex.IsMatch(columnName, @"^[A-Z][A-Za-z0-9]*Id$", RegexOptions.None) || Regex.IsMatch(columnName, @"^[a-z][a-z0-9_]*_id$", RegexOptions.None);
    }

    /// <summary>参照元テーブル名から参照先テーブルに期待する FK 列名 (例: <c>CustomerId</c> / <c>customer_id</c>) を求める</summary>
    /// <remarks>参照元テーブル名にアンダースコアが含まれる場合はスネークケース、それ以外はパスカルケースで組み立てる</remarks>
    private static string ResolveForeignKeyColumnName(string sourceTableName)
    {
        var sourceWords = SplitIdentifierWords(sourceTableName);

        if (sourceWords.Count == 0)
        {
            return "ParentId";
        }

        return sourceTableName.Contains('_', StringComparison.Ordinal)
            ? string.Join("_", sourceWords.Select(static word => word.ToLowerInvariant())) + "_id"
            : string.Concat(sourceWords.Select(ToPascalWord)) + "Id";
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

    /// <summary>テーブル名の末尾単語を指定の単数形・複数形へ変換する</summary>
    /// <remarks>単語の区切り直しによりアンダースコア結合へ正規化されるため、命名規則の変換 (<see cref="ConvertIdentifier"/>) と併用する前提</remarks>
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

    /// <summary>スネークケース・パスカルケース等の識別子を単語のリストへ分解する</summary>
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

    /// <summary>単語が英語の複数形らしいかどうかを語尾の簡易ルールで判定する</summary>
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

    /// <summary>単語を語尾の簡易ルールで単数形へ変換する (不規則変化は非対応)</summary>
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

    /// <summary>単語を語尾の簡易ルールで複数形へ変換する (不規則変化は非対応)</summary>
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

    /// <summary>単語を先頭大文字・以降小文字のパスカルケース表記へ整える</summary>
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
