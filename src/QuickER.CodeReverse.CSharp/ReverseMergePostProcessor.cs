using QuickER.Model;

namespace QuickER.CodeReverse.CSharp;

/// <summary>
/// コード取込（GUI マージ）専用の後処理。コードに存在しない情報を現在図から温存する。
/// </summary>
/// <remarks>
/// <para>
/// リバース解析結果は方言中立で、ON DELETE / ON UPDATE・FK 制約名・多対多リレーションを持たない
/// （コード生成がこれらを出力しないため）。<see cref="DiagramMergeReconciler"/> による Guid 引継の後に本後処理を挟み:
/// </para>
/// <list type="bullet">
///   <item>(a) 端点 4 つ組（両端テーブル名・列名）が一致するリレーションの <c>OnDelete</c> / <c>OnUpdate</c> /
///     <c>ConstraintName</c> を現在図の値へ引き継ぐ</item>
///   <item>(b) 現在図の多対多リレーションは、両端エンティティが（Guid 引継で）生存していれば結果へ追加温存する</item>
/// </list>
/// <para>
/// コードで消えた通常（多対多以外）のリレーションは追加しない（＝図からも消える）。
/// </para>
/// </remarks>
public static class ReverseMergePostProcessor
{
    /// <summary>マージ後のリレーションへ、現在図由来の参照アクション・制約名・多対多を反映した一覧を返す</summary>
    /// <param name="current">コード取込前の現在図（多対多・参照アクション・制約名の供給元）</param>
    /// <param name="mergedEntities">Guid 引継済みのマージ結果エンティティ（現在図の Id を引き継いでいる）</param>
    /// <param name="mergedRelationships">Guid 引継済みのマージ結果リレーション（コード由来）</param>
    /// <returns>参照アクション・制約名を引き継ぎ、生存する多対多を追加した最終リレーション一覧</returns>
    public static List<Relationship> Apply(
        ErDiagram current,
        IReadOnlyList<Entity> mergedEntities,
        IReadOnlyList<Relationship> mergedRelationships
    )
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(mergedEntities);
        ArgumentNullException.ThrowIfNull(mergedRelationships);

        // マージ結果・現在図それぞれの「エンティティ Id → (列 Id → 列名)」索引（端点 4 つ組の解決に使う）
        var mergedNames = BuildNameLookup(mergedEntities);
        var currentNames = BuildNameLookup(current.Entities);

        // 現在図リレーションを端点 4 つ組で引ける索引にする（参照アクション・制約名の供給元）
        var currentByEndpoints = new Dictionary<RelationshipEndpoints, Relationship>();

        foreach (var relationship in current.Relationships)
        {
            var endpoints = ResolveEndpoints(relationship, currentNames);

            if (endpoints is { } key)
            {
                // 同一端点が複数ある場合は最初の 1 件を採用する（曖昧さは実用上まれ）
                currentByEndpoints.TryAdd(key, relationship);
            }
        }

        var result = new List<Relationship>(mergedRelationships.Count);

        // (a) コード由来リレーションへ、端点一致する現在図リレーションの参照アクション・制約名を引き継ぐ
        foreach (var relationship in mergedRelationships)
        {
            var endpoints = ResolveEndpoints(relationship, mergedNames);

            if (endpoints is { } key && currentByEndpoints.TryGetValue(key, out var existing))
            {
                relationship.OnDelete = existing.OnDelete;
                relationship.OnUpdate = existing.OnUpdate;
                relationship.ConstraintName = existing.ConstraintName;
            }

            result.Add(relationship);
        }

        // (b) 現在図の多対多は、両端エンティティが生存していれば温存して追加する
        var survivingEntityIds = mergedEntities.Select(entity => entity.Id).ToHashSet();

        foreach (var relationship in current.Relationships)
        {
            if (
                relationship.Type == RelationshipType.ManyToMany
                && survivingEntityIds.Contains(relationship.SourceEntityId)
                && survivingEntityIds.Contains(relationship.TargetEntityId)
            )
            {
                result.Add(relationship);
            }
        }

        return result;
    }

    /// <summary>エンティティ集合から「エンティティ Id → (列 Id → 列名)」の索引を作る</summary>
    private static Dictionary<Guid, Dictionary<Guid, string>> BuildNameLookup(
        IEnumerable<Entity> entities
    )
    {
        var lookup = new Dictionary<Guid, Dictionary<Guid, string>>();

        foreach (var entity in entities)
        {
            lookup[entity.Id] = entity.Columns.ToDictionary(
                column => column.Id,
                column => column.Name
            );
        }

        return lookup;
    }

    /// <summary>リレーションの端点 4 つ組（両端テーブル名・列名）を解決する（両端エンティティ不明時は <c>null</c>）</summary>
    private static RelationshipEndpoints? ResolveEndpoints(
        Relationship relationship,
        IReadOnlyDictionary<Guid, Dictionary<Guid, string>> names
    )
    {
        if (
            !names.TryGetValue(relationship.SourceEntityId, out var sourceColumns)
            || !names.TryGetValue(relationship.TargetEntityId, out var targetColumns)
        )
        {
            return null;
        }

        return new RelationshipEndpoints(
            SourceEntityId: relationship.SourceEntityId,
            SourceColumnName: ResolveColumnName(sourceColumns, relationship.SourceColumnId),
            TargetEntityId: relationship.TargetEntityId,
            TargetColumnName: ResolveColumnName(targetColumns, relationship.TargetColumnId)
        );
    }

    /// <summary>列 Id から列名を引く（未設定・未解決は <c>null</c>）</summary>
    private static string? ResolveColumnName(
        IReadOnlyDictionary<Guid, string> columns,
        Guid? columnId
    ) => columnId is { } id && columns.TryGetValue(id, out var name) ? name : null;

    /// <summary>端点一致判定用のキー（両端エンティティ Id ＋ 両端列名。エンティティ Id は Guid 引継後の共通 Id）</summary>
    private readonly record struct RelationshipEndpoints(
        Guid SourceEntityId,
        string? SourceColumnName,
        Guid TargetEntityId,
        string? TargetColumnName
    );
}
