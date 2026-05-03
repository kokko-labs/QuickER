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
    [JsonPropertyName("entities")]
    public List<AiEntity> Entities { get; set; } = new();

    /// <summary>Ollama などが返す tables 形式との互換用。</summary>
    [JsonPropertyName("tables")]
    public List<AiEntity>? Tables
    {
        get => Entities;
        set => Entities = value ?? new();
    }

    /// <summary>テーブル間のリレーション一覧。</summary>
    [JsonPropertyName("relationships")]
    public List<AiRelationship> Relationships { get; set; } = new();

    /// <summary>JSON 表現を ER 図ドメインモデル (<see cref="Entity"/>, <see cref="Relationship"/>) に変換します。</summary>
    public (List<Entity> Entities, List<Relationship> Relationships) ToDomain()
    {
        var entities = new List<Entity>();
        var byTable = new Dictionary<string, Entity>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var e in Entities)
        {
            if (string.IsNullOrWhiteSpace(e.TableName)) continue;
            var entity = new Entity
            {
                TableName = e.TableName,
                Description = string.IsNullOrWhiteSpace(e.DisplayName) ? string.Empty : e.DisplayName,
                Memo = e.Memo ?? string.Empty,
                Columns = e.Columns?.Select(c => new Column
                {
                    Name = c.Name ?? "Column",
                    DataType = string.IsNullOrWhiteSpace(c.DataType) ? "int" : c.DataType,
                    IsPrimaryKey = c.IsPrimaryKey,
                    IsForeignKey = c.IsForeignKey
                }).ToList() ?? new List<Column>()
            };
            entities.Add(entity);
            byTable[entity.TableName] = entity;
        }

        var relationships = new List<Relationship>();
        foreach (var r in Relationships)
        {
            if (r.SourceTable is null || r.TargetTable is null) continue;
            if (!byTable.TryGetValue(r.SourceTable, out var s)) continue;
            if (!byTable.TryGetValue(r.TargetTable, out var t)) continue;
            relationships.Add(new Relationship
            {
                SourceEntityId = s.Id,
                TargetEntityId = t.Id,
                Type = ParseType(r.Type)
            });
        }

        return (entities, relationships);
    }

    private static RelationshipType ParseType(string? type) => type switch
    {
        "OneToOne" or "1:1" or "one-to-one" => RelationshipType.OneToOne,
        "ManyToMany" or "N:N" or "many-to-many" or "M:N" => RelationshipType.ManyToMany,
        _ => RelationshipType.OneToMany
    };
}

/// <summary>AI が返すエンティティ。</summary>
public class AiEntity
{
    /// <summary>論理名 (AI 応答の JSON 互換のため残存。インポート時に Description へマップ).</summary>
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    /// <summary>物理テーブル名 (英数字)。</summary>
    [JsonPropertyName("tableName")] public string? TableName { get; set; }
    /// <summary>備考。</summary>
    [JsonPropertyName("memo")] public string? Memo { get; set; }
    /// <summary>カラム一覧。</summary>
    [JsonPropertyName("columns")] public List<AiColumn>? Columns { get; set; }
}

/// <summary>AI が返すカラム。</summary>
public class AiColumn
{
    /// <summary>カラム名。</summary>
    [JsonPropertyName("name")] public string? Name { get; set; }
    /// <summary>columnName 形式との互換用。</summary>
    [JsonPropertyName("columnName")]
    public string? ColumnName
    {
        get => Name;
        set
        {
            if (!string.IsNullOrWhiteSpace(value)) Name = value;
        }
    }
    /// <summary>SQL Server のデータ型。</summary>
    [JsonPropertyName("dataType")] public string? DataType { get; set; }
    /// <summary>主キーかどうか。</summary>
    [JsonPropertyName("isPrimaryKey")] public bool IsPrimaryKey { get; set; }
    /// <summary>外部キーかどうか。</summary>
    [JsonPropertyName("isForeignKey")] public bool IsForeignKey { get; set; }
}

/// <summary>AI が返すリレーション。</summary>
public class AiRelationship
{
    /// <summary>起点テーブル名。</summary>
    [JsonPropertyName("sourceTable")] public string? SourceTable { get; set; }
    /// <summary>fromTable 形式との互換用。</summary>
    [JsonPropertyName("fromTable")]
    public string? FromTable
    {
        get => SourceTable;
        set
        {
            if (!string.IsNullOrWhiteSpace(value)) SourceTable = value;
        }
    }
    /// <summary>終点テーブル名。</summary>
    [JsonPropertyName("targetTable")] public string? TargetTable { get; set; }
    /// <summary>toTable 形式との互換用。</summary>
    [JsonPropertyName("toTable")]
    public string? ToTable
    {
        get => TargetTable;
        set
        {
            if (!string.IsNullOrWhiteSpace(value)) TargetTable = value;
        }
    }
    /// <summary>関連の種類 (OneToOne / OneToMany / ManyToMany)。</summary>
    [JsonPropertyName("type")] public string? Type { get; set; }
}
