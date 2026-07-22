using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// DB / Excel の再取込で、取込結果の Guid を現在図へ引き継ぐ「マージ取込」の照合処理。
/// </summary>
/// <remarks>
/// <para>
/// 取込結果は毎回新規 Guid を持つため、素朴に置換すると名前付きクエリ（エンティティ・列を Guid 参照）・
/// 手配置レイアウト・Memo が全滅する。本クラスは取込結果のエンティティ・列を現在図とテーブル名・列名で
/// 照合し、一致した要素の Id を現在図の Guid へ書き換える（＝定義内容は取込結果が正・Id だけ現在図へ寄せる）。
/// これにより現在図のクエリ参照・レイアウトが取込後も温存できる。
/// </para>
/// <para>
/// 照合は「名前が現在図・取込結果の双方で一意なもの」だけを対象にする（同名テーブル・同一テーブル内の
/// 同名列が複数ある場合はその名前をマッチ対象から外す＝曖昧さ回避で新規扱い）。リネームは削除＋新規扱い。
/// 一致エンティティの Memo は <paramref name="preserveExistingMemo"/> が真のとき現在図の値を温存する
/// （DB には対応物がないため。Excel 定義書は Memo を持つので取込値を正とする）。
/// </para>
/// </remarks>
public static class DiagramMergeReconciler
{
    /// <summary>取込結果の Id を現在図へ寄せ、クエリの生存/壊れを判定してマージ結果を返す</summary>
    /// <param name="current">現在の図（名前付きクエリ込み・スナップショット）</param>
    /// <param name="importedEntities">取込結果のエンティティ（Id・Memo をその場で書き換える）</param>
    /// <param name="importedRelationships">取込結果のリレーション（参照 Id をその場で書き換える）</param>
    /// <param name="preserveExistingMemo">一致エンティティの Memo に現在図の値を温存するか（DB=true / Excel=false）</param>
    /// <returns>Id 書換え済みのエンティティ・リレーションと、生存クエリ・壊れクエリの一覧</returns>
    public static DiagramMergeResult Reconcile(
        ErDiagram current,
        IReadOnlyList<Entity> importedEntities,
        IReadOnlyList<Relationship> importedRelationships,
        bool preserveExistingMemo
    )
    {
        // 名前照合の索引（現在図・取込結果とも「一意な名前」だけをマッチ対象にする）
        var currentByName = BuildUniqueNameIndex(current.Entities, entity => entity.TableName);
        var importedByName = BuildUniqueNameIndex(importedEntities, entity => entity.TableName);

        // 取込結果 Id → 現在図 Id の対応表（エンティティ・列）と、Memo 温存用の現在図エンティティ参照
        var entityIdMap = new Dictionary<Guid, Guid>();
        var columnIdMap = new Dictionary<Guid, Guid>();
        var matchedCurrentEntities = new Dictionary<Guid, Entity>();

        foreach (var (name, importedEntity) in importedByName)
        {
            if (!currentByName.TryGetValue(name, out var currentEntity))
            {
                continue;
            }

            // エンティティ一致: 取込結果の Id を現在図の Guid へ寄せる
            entityIdMap[importedEntity.Id] = currentEntity.Id;
            matchedCurrentEntities[importedEntity.Id] = currentEntity;

            // 列も同一エンティティ内で名前が双方一意のものだけ照合する
            var currentColumnsByName = BuildUniqueNameIndex(
                currentEntity.Columns,
                column => column.Name
            );
            var importedColumnsByName = BuildUniqueNameIndex(
                importedEntity.Columns,
                column => column.Name
            );

            foreach (var (columnName, importedColumn) in importedColumnsByName)
            {
                if (currentColumnsByName.TryGetValue(columnName, out var currentColumn))
                {
                    columnIdMap[importedColumn.Id] = currentColumn.Id;
                }
            }
        }

        // 取込結果のエンティティ・列 Id を書き換える（定義内容はそのまま＝取込結果が正、Id だけ寄せる）
        foreach (var entity in importedEntities)
        {
            var originalId = entity.Id;

            if (entityIdMap.TryGetValue(originalId, out var mappedEntityId))
            {
                entity.Id = mappedEntityId;

                // 一致エンティティの Memo を温存する（DB には対応物がないため）
                if (
                    preserveExistingMemo
                    && matchedCurrentEntities.TryGetValue(originalId, out var currentEntity)
                )
                {
                    entity.Memo = currentEntity.Memo;
                }
            }

            foreach (var column in entity.Columns)
            {
                if (columnIdMap.TryGetValue(column.Id, out var mappedColumnId))
                {
                    column.Id = mappedColumnId;
                }
            }
        }

        // リレーションの参照 Id も対応表で追従書き換えする（両端エンティティ・両端列）
        foreach (var relationship in importedRelationships)
        {
            relationship.SourceEntityId = MapId(entityIdMap, relationship.SourceEntityId);
            relationship.TargetEntityId = MapId(entityIdMap, relationship.TargetEntityId);
            relationship.SourceColumnId = MapNullableId(columnIdMap, relationship.SourceColumnId);
            relationship.TargetColumnId = MapNullableId(columnIdMap, relationship.TargetColumnId);
        }

        // 現在図のクエリを、マージ後の図で Guid 参照が解決できるかで生存/壊れに振り分ける
        var (surviving, broken) = ClassifyQueries(current.Queries, importedEntities);

        return new DiagramMergeResult
        {
            Entities = importedEntities,
            Relationships = importedRelationships,
            SurvivingQueries = surviving,
            BrokenQueries = broken,
        };
    }

    /// <summary>名前が一意な要素だけを引ける索引を作る（重複した名前は曖昧さ回避のため除外する）</summary>
    private static Dictionary<string, T> BuildUniqueNameIndex<T>(
        IEnumerable<T> items,
        Func<T, string> nameSelector
    )
    {
        var firstByName = new Dictionary<string, T>(StringComparer.Ordinal);
        var duplicatedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var name = nameSelector(item);

            if (!firstByName.TryAdd(name, item))
            {
                duplicatedNames.Add(name);
            }
        }

        // 2 回以上出現した名前はマッチ対象から外す（現在図・取込結果いずれに複数あっても新規扱い）
        foreach (var name in duplicatedNames)
        {
            firstByName.Remove(name);
        }

        return firstByName;
    }

    /// <summary>現在図のクエリを、マージ後のエンティティに対する Guid 参照解決の可否で生存/壊れへ分類する</summary>
    /// <remarks>
    /// 解決の意味論はコード生成の生成前検証（<c>CollectValidQueries</c>）と揃える：
    /// クエリの <see cref="QueryDefinition.EntityId"/>・パラメータ／射影フィールドの
    /// <c>SourceColumnId</c>・並び順の <c>ColumnId</c> がすべてマージ後の図で解決できれば生存、
    /// 1 つでも解決できなければ壊れ。
    /// </remarks>
    private static (List<QueryDefinition> Surviving, List<QueryDefinition> Broken) ClassifyQueries(
        IReadOnlyList<QueryDefinition> queries,
        IReadOnlyList<Entity> mergedEntities
    )
    {
        var surviving = new List<QueryDefinition>();
        var broken = new List<QueryDefinition>();

        // マージ後のエンティティ Id → 列 Id 集合（クエリの Guid 参照解決に使う）
        var columnsByEntity = new Dictionary<Guid, HashSet<Guid>>();

        foreach (var entity in mergedEntities)
        {
            columnsByEntity[entity.Id] = entity.Columns.Select(column => column.Id).ToHashSet();
        }

        foreach (var query in queries)
        {
            if (IsQueryResolvable(query, columnsByEntity))
            {
                surviving.Add(query);
            }
            else
            {
                broken.Add(query);
            }
        }

        return (surviving, broken);
    }

    /// <summary>クエリの全 Guid 参照（エンティティ・列）がマージ後の図で解決できるかを判定する</summary>
    private static bool IsQueryResolvable(
        QueryDefinition query,
        IReadOnlyDictionary<Guid, HashSet<Guid>> columnsByEntity
    )
    {
        if (!columnsByEntity.TryGetValue(query.EntityId, out var columnIds))
        {
            return false;
        }

        foreach (var parameter in query.Parameters)
        {
            if (
                parameter.SourceColumnId is { } parameterColumnId
                && !columnIds.Contains(parameterColumnId)
            )
            {
                return false;
            }
        }

        foreach (var field in query.Fields)
        {
            if (field.SourceColumnId is { } fieldColumnId && !columnIds.Contains(fieldColumnId))
            {
                return false;
            }
        }

        foreach (var ordering in query.OrderBy)
        {
            if (!columnIds.Contains(ordering.ColumnId))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>対応表で Guid を写す（対応がなければそのまま＝新規要素）</summary>
    private static Guid MapId(IReadOnlyDictionary<Guid, Guid> map, Guid id) =>
        map.TryGetValue(id, out var mapped) ? mapped : id;

    /// <summary>対応表で NULL 許容 Guid を写す（null・対応なしはそのまま）</summary>
    private static Guid? MapNullableId(IReadOnlyDictionary<Guid, Guid> map, Guid? id) =>
        id is { } value && map.TryGetValue(value, out var mapped) ? mapped : id;
}

/// <summary>マージ取込の結果（Id 書換え済みのスキーマと、クエリの生存/壊れ振り分け）</summary>
public sealed class DiagramMergeResult
{
    /// <summary>Id を現在図へ寄せた取込結果のエンティティ</summary>
    public required IReadOnlyList<Entity> Entities { get; init; }

    /// <summary>参照 Id を現在図へ寄せた取込結果のリレーション</summary>
    public required IReadOnlyList<Relationship> Relationships { get; init; }

    /// <summary>マージ後の図で Guid 参照がすべて解決できた（＝温存する）クエリ定義</summary>
    public required IReadOnlyList<QueryDefinition> SurvivingQueries { get; init; }

    /// <summary>Guid 参照が解決できなくなった（＝取込時に削除される）クエリ定義</summary>
    public required IReadOnlyList<QueryDefinition> BrokenQueries { get; init; }
}
