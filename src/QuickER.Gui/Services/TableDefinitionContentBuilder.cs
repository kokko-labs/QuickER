using QuickER.Model;

namespace QuickER.Services;

/// <summary>テーブル定義書（Excel / HTML）で共用する表記ロジックを提供する共有ヘルパー</summary>
/// <remarks>
/// キー表記（PK / FK1…）・関係表記（1:1 / N:1 / N:N）・参照先文字列などは言語中立で、
/// 出力形式に依らず同一表記であるべきなので、ここに集約して表記のドリフトを防ぐ。
/// </remarks>
internal static class TableDefinitionContentBuilder
{
    /// <summary>定義書のテーブル掲載順（テーブル名の大文字小文字無視昇順＝No. 連番の基準）で並べ替える</summary>
    public static List<Entity> OrderEntities(IEnumerable<Entity> entities) =>
        entities.OrderBy(entity => entity.TableName, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>リレーション一覧の掲載順（参照先＝Source テーブル名 → 参照元＝Target テーブル名）で並べ替える</summary>
    public static List<Relationship> OrderRelationships(
        IEnumerable<Relationship> relationships,
        IReadOnlyDictionary<Guid, Entity> entitiesById
    ) =>
        relationships
            .OrderBy(
                relationship => TableNameOf(entitiesById, relationship.SourceEntityId),
                StringComparer.OrdinalIgnoreCase
            )
            .ThenBy(
                relationship => TableNameOf(entitiesById, relationship.TargetEntityId),
                StringComparer.OrdinalIgnoreCase
            )
            .ToList();

    /// <summary>リレーション種別を定義書向けの表記（1:1 / N:1 / N:N）へ変換する</summary>
    public static string GetRelationshipTypeLabel(RelationshipType type) =>
        type switch
        {
            RelationshipType.OneToOne => "1:1",
            RelationshipType.OneToMany => "N:1",
            RelationshipType.ManyToMany => "N:N",
            _ => type.ToString(),
        };

    /// <summary>テーブル内の外部キーに連番（FK1, FK2…）を振った列 ID ごとの表示ラベルを構築する</summary>
    public static IReadOnlyDictionary<Guid, string> BuildForeignKeyLabels(
        Entity entity,
        IReadOnlyList<Relationship> relationships,
        IReadOnlyDictionary<Guid, Entity> entitiesById
    )
    {
        var columnIndexes = entity
            .Columns.Select((column, index) => new { column.Id, index })
            .ToDictionary(item => item.Id, item => item.index);
        var foreignKeyLabels = new Dictionary<Guid, List<string>>();
        var targetRelationships = relationships
            .Where(relationship =>
                relationship.TargetEntityId == entity.Id && relationship.TargetColumnId is not null
            )
            .OrderBy(relationship =>
                columnIndexes.GetValueOrDefault(relationship.TargetColumnId!.Value, int.MaxValue)
            )
            .ThenBy(
                relationship => TableNameOf(entitiesById, relationship.SourceEntityId),
                StringComparer.OrdinalIgnoreCase
            )
            .ThenBy(
                relationship =>
                    ColumnNameOf(
                        entitiesById,
                        relationship.SourceEntityId,
                        relationship.SourceColumnId
                    ),
                StringComparer.OrdinalIgnoreCase
            )
            .ToList();

        for (var i = 0; i < targetRelationships.Count; i++)
        {
            var relationship = targetRelationships[i];
            var targetColumnId = relationship.TargetColumnId!.Value;

            if (!foreignKeyLabels.TryGetValue(targetColumnId, out var labels))
            {
                labels = [];
                foreignKeyLabels[targetColumnId] = labels;
            }

            labels.Add($"FK{i + 1}");
        }

        return foreignKeyLabels.ToDictionary(
            pair => pair.Key,
            pair => string.Join(",", pair.Value)
        );
    }

    /// <summary>キー列の表示ラベルを返す（PK / FK / PK/FK の組み合わせを表現する）</summary>
    public static string GetKeyLabel(Column column, string? foreignKeyLabel)
    {
        if (column.IsPrimaryKey && !string.IsNullOrWhiteSpace(foreignKeyLabel))
        {
            return $"PK/{foreignKeyLabel}";
        }

        if (column.IsPrimaryKey)
        {
            return "PK";
        }

        if (!string.IsNullOrWhiteSpace(foreignKeyLabel))
        {
            return foreignKeyLabel;
        }

        if (column.IsForeignKey)
        {
            return "FK";
        }

        return string.Empty;
    }

    /// <summary>外部キー列の参照先（テーブル.カラム）を重複なく連結した文字列を返す</summary>
    public static string GetReferenceText(
        Entity entity,
        Column column,
        IReadOnlyList<Relationship> relationships,
        IReadOnlyDictionary<Guid, Entity> entitiesById
    )
    {
        var references = relationships
            .Where(relationship =>
                relationship.TargetEntityId == entity.Id && relationship.TargetColumnId == column.Id
            )
            .Select(relationship =>
                $"{TableNameOf(entitiesById, relationship.SourceEntityId)}.{ColumnNameOf(entitiesById, relationship.SourceEntityId, relationship.SourceColumnId)}"
            )
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return string.Join(", ", references);
    }

    /// <summary>外部キー列が参照する参照先エンティティ ID を重複なく返す（詳細シートへのリンク化判定に用いる）</summary>
    public static IReadOnlyList<Guid> GetReferencedEntityIds(
        Entity entity,
        Column column,
        IReadOnlyList<Relationship> relationships
    )
    {
        return relationships
            .Where(relationship =>
                relationship.TargetEntityId == entity.Id && relationship.TargetColumnId == column.Id
            )
            .Select(relationship => relationship.SourceEntityId)
            .Distinct()
            .ToList();
    }

    /// <summary>エンティティ ID からテーブル名を解決する（未解決時は空文字）</summary>
    public static string TableNameOf(
        IReadOnlyDictionary<Guid, Entity> entitiesById,
        Guid entityId
    ) => entitiesById.TryGetValue(entityId, out var entity) ? entity.TableName : string.Empty;

    /// <summary>エンティティ ID・列 ID からカラム名を解決する（未指定・不一致時は空文字）</summary>
    public static string ColumnNameOf(
        IReadOnlyDictionary<Guid, Entity> entitiesById,
        Guid entityId,
        Guid? columnId
    )
    {
        if (columnId is null || !entitiesById.TryGetValue(entityId, out var entity))
        {
            return string.Empty;
        }

        return entity.Columns.FirstOrDefault(column => column.Id == columnId)?.Name ?? string.Empty;
    }
}
