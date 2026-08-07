using QuickER.Model;
using QuickER.Provider;

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

    /// <summary>リレーション一覧シートの参照元列・参照先列セルの表記（カンマ区切りの列名）を返す</summary>
    /// <remarks>
    /// 1 行 1 リレーションを保ったまま複合外部キーを表すため、構成列を宣言順にカンマ区切りで並べる
    /// （単一列なら従来どおり列名 1 つ）。解決できない参照を含む列ペアは読み飛ばす（DDL 生成と同じ規則）
    /// </remarks>
    public static (string SourceColumns, string TargetColumns) GetRelationshipColumnTexts(
        Relationship relationship,
        IReadOnlyDictionary<Guid, Entity> entitiesById
    )
    {
        if (
            !entitiesById.TryGetValue(relationship.SourceEntityId, out var source)
            || !entitiesById.TryGetValue(relationship.TargetEntityId, out var target)
        )
        {
            return (string.Empty, string.Empty);
        }

        // 解決は DDL 生成と同じ共通ヘルパーに委ねる（1 列でも解決できないペアを含む外部キーは丸ごと空欄）
        var pairs = ForeignKeyColumnPairResolver.Resolve(relationship, source, target);

        if (pairs is null)
        {
            return (string.Empty, string.Empty);
        }

        return (
            string.Join(", ", ForeignKeyColumnPairResolver.ParentColumns(pairs)),
            string.Join(", ", ForeignKeyColumnPairResolver.ChildColumns(pairs))
        );
    }

    /// <summary>テーブル内の外部キーに連番（FK1, FK2…）を振った列 ID ごとの表示ラベルを構築する</summary>
    /// <remarks>
    /// 連番は 1 リレーション（外部キー制約）につき 1 つで、<b>同じ番号は同じ外部キー</b>を意味する
    /// （複合外部キーは構成する子列すべてに同じ <c>FK{n}</c> が並ぶ＝一意制約の <c>UQ{n}</c> と同じ流儀）。
    /// 1 列が複数の外部キーに参加する場合はカンマ連結する
    /// </remarks>
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
                relationship.TargetEntityId == entity.Id && relationship.ColumnPairs.Count > 0
            )
            // 先頭に来る構成列の位置で並べる（複合外部キーは最も上にある子列が代表）
            .OrderBy(relationship =>
                relationship.ColumnPairs.Min(pair =>
                    columnIndexes.GetValueOrDefault(pair.TargetColumnId, int.MaxValue)
                )
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
                        relationship.ColumnPairs[0].SourceColumnId
                    ),
                StringComparer.OrdinalIgnoreCase
            )
            .ToList();

        for (var i = 0; i < targetRelationships.Count; i++)
        {
            foreach (var pair in targetRelationships[i].ColumnPairs)
            {
                if (!foreignKeyLabels.TryGetValue(pair.TargetColumnId, out var labels))
                {
                    labels = [];
                    foreignKeyLabels[pair.TargetColumnId] = labels;
                }

                labels.Add($"FK{i + 1}");
            }
        }

        return foreignKeyLabels.ToDictionary(
            pair => pair.Key,
            pair => string.Join(",", pair.Value)
        );
    }

    /// <summary>テーブル内の一意制約に連番（UQ1, UQ2…）を振った列 ID ごとの表示ラベルを構築する</summary>
    /// <remarks>
    /// 連番はテーブルが持つ一意制約の登場順で、<b>同じ番号は同じ制約</b>を意味する（複合制約は構成列すべてに
    /// 同じ <c>UQ{n}</c> が並ぶ＝1 列 1 セルの定義書でも制約の広がりが読み取れる）。1 列が複数の制約に
    /// 参加する場合は外部キーの連番と同じ流儀でカンマ連結する。構成列が空、または解決できないカラム ID を
    /// 含む制約は連番を消費せずに読み飛ばす（DDL 生成と同じ規則）
    /// </remarks>
    public static IReadOnlyDictionary<Guid, string> BuildUniqueConstraintLabels(Entity entity)
    {
        var labels = new Dictionary<Guid, List<string>>();
        var number = 0;

        foreach (var constraint in entity.UniqueConstraints)
        {
            if (!UniqueConstraintNaming.TryResolveColumnNames(entity, constraint, out _))
            {
                continue;
            }

            number++;

            foreach (var columnId in constraint.ColumnIds)
            {
                if (!labels.TryGetValue(columnId, out var columnLabels))
                {
                    columnLabels = [];
                    labels[columnId] = columnLabels;
                }

                columnLabels.Add($"UQ{number}");
            }
        }

        return labels.ToDictionary(pair => pair.Key, pair => string.Join(",", pair.Value));
    }

    /// <summary>キー列の表示ラベルを返す（PK / FK / UQ の組み合わせを <c>/</c> 区切りで表現する）</summary>
    /// <remarks>例: <c>PK</c> / <c>FK1</c> / <c>PK/FK1</c> / <c>UQ1</c> / <c>PK/UQ1</c> / <c>FK1/UQ2</c></remarks>
    public static string GetKeyLabel(
        Column column,
        string? foreignKeyLabel,
        string? uniqueConstraintLabel = null
    )
    {
        var parts = new List<string>();

        if (column.IsPrimaryKey)
        {
            parts.Add("PK");
        }

        if (!string.IsNullOrWhiteSpace(foreignKeyLabel))
        {
            parts.Add(foreignKeyLabel!);
        }
        else if (column.IsForeignKey && !column.IsPrimaryKey)
        {
            // リレーション由来の連番が無い FK 列（列フラグだけが立っている場合）は番号なしの FK とする
            parts.Add("FK");
        }

        if (!string.IsNullOrWhiteSpace(uniqueConstraintLabel))
        {
            parts.Add(uniqueConstraintLabel!);
        }

        return string.Join("/", parts);
    }

    /// <summary>外部キー列の参照先（テーブル.カラム）を重複なく連結した文字列を返す</summary>
    /// <remarks>複合外部キーでは、この列と対になっている親列だけを参照先として挙げる</remarks>
    public static string GetReferenceText(
        Entity entity,
        Column column,
        IReadOnlyList<Relationship> relationships,
        IReadOnlyDictionary<Guid, Entity> entitiesById
    )
    {
        var references = relationships
            .Where(relationship => relationship.TargetEntityId == entity.Id)
            .SelectMany(relationship =>
                relationship
                    .ColumnPairs.Where(pair => pair.TargetColumnId == column.Id)
                    .Select(pair =>
                        $"{TableNameOf(entitiesById, relationship.SourceEntityId)}.{ColumnNameOf(entitiesById, relationship.SourceEntityId, pair.SourceColumnId)}"
                    )
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
                relationship.TargetEntityId == entity.Id
                && relationship.ColumnPairs.Any(pair => pair.TargetColumnId == column.Id)
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
