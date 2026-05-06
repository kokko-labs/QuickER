using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
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

    /// <summary>JSON 表現を ER 図ドメインモデル (<see cref="Entity"/>, <see cref="Relationship"/>) に変換します。</summary>
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

            var entity = new Entity
            {
                TableName = table.Name,
                Description = table.Description ?? string.Empty,
                Memo = table.Memo ?? string.Empty,
                Columns =
                    table
                        .Columns?.Select(c => new Column
                        {
                            Name = c.Name ?? "Column",
                            DataType = string.IsNullOrWhiteSpace(c.DataType) ? "int" : c.DataType,
                            IsPrimaryKey = c.IsPrimaryKey,
                            IsForeignKey = c.IsForeignKey,
                            Description = c.Description ?? string.Empty,
                        })
                        .ToList()
                    ?? new List<Column>(),
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
                }
            );
        }

        return (entities, relationships);
    }

    private static RelationshipType ParseType(string? type) =>
        type switch
        {
            "OneToOne" or "1:1" or "one-to-one" => RelationshipType.OneToOne,
            "ManyToMany" or "N:N" or "many-to-many" or "M:N" => RelationshipType.ManyToMany,
            _ => RelationshipType.OneToMany,
        };
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
}
