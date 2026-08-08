using QuickER.Model;

namespace QuickER.CodeReverse.CSharp;

/// <summary>
/// コード取込（GUI マージ）専用の後処理。コードが語っていない情報を現在図から温存する。
/// </summary>
/// <remarks>
/// <para>
/// リバース解析結果は方言中立で、多対多リレーションを持たない（コード生成が出力しないため）。
/// 外部キーメタデータ（<c>OnDelete</c> / <c>OnUpdate</c> / <c>ConstraintName</c>）は
/// <c>[NavigationReference]</c> の名前付き引数として往復するようになったが、既定値のときは冗長を避けて
/// 出力されず、旧バージョンで生成したコードには引数自体が存在しない。
/// <see cref="DiagramMergeReconciler"/> による Guid 引継の後に本後処理を挟み:
/// </para>
/// <list type="bullet">
///   <item>(a) 端点 4 つ組（両端テーブル名・列名）が一致するリレーションについて、
///     <b>コードが指定していなかったフィールドだけ</b> 現在図の値で補完する（fallback 専用＝指定があればコードが勝つ）</item>
///   <item>(b) 現在図の多対多リレーションは、両端エンティティが（Guid 引継で）生存していれば結果へ追加温存する</item>
/// </list>
/// <para>
/// コードで消えた通常（多対多以外）のリレーションは追加しない（＝図からも消える）。
/// UNIQUE 制約は <c>[UniqueConstraint]</c> でコードが完全に語れる（属性なし＝制約なし）ため温存対象外＝コードが正本。
/// </para>
/// </remarks>
public static class ReverseMergePostProcessor
{
    /// <summary>マージ後のリレーションへ、現在図由来の参照アクション・制約名・多対多を反映した一覧を返す</summary>
    /// <param name="current">コード取込前の現在図（多対多・未指定フィールドの供給元）</param>
    /// <param name="mergedEntities">Guid 引継済みのマージ結果エンティティ（現在図の Id を引き継いでいる）</param>
    /// <param name="mergedRelationships">Guid 引継済みのマージ結果リレーション（コード由来）</param>
    /// <param name="codeMetadata">
    /// コードが明示していた外部キーメタデータの索引（<see cref="CodeReverseResult.RelationshipMetadata"/>。
    /// <c>null</c> または未登録のリレーションは「全フィールド未指定」＝全項目を現在図から補完する）
    /// </param>
    /// <returns>未指定フィールドを補完し、生存する多対多を追加した最終リレーション一覧</returns>
    public static List<Relationship> Apply(
        ErDiagram current,
        IReadOnlyList<Entity> mergedEntities,
        IReadOnlyList<Relationship> mergedRelationships,
        IReadOnlyDictionary<Guid, ReverseRelationshipMetadata>? codeMetadata = null
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

        // (a) コード由来リレーションの「未指定」フィールドだけ、端点一致する現在図リレーションの値で補完する
        foreach (var relationship in mergedRelationships)
        {
            var endpoints = ResolveEndpoints(relationship, mergedNames);

            if (endpoints is { } key && currentByEndpoints.TryGetValue(key, out var existing))
            {
                var specified =
                    codeMetadata is not null
                    && codeMetadata.TryGetValue(relationship.Id, out var metadata)
                        ? metadata
                        : null;

                if (specified?.OnDelete is null)
                {
                    relationship.OnDelete = existing.OnDelete;
                }

                if (specified?.OnUpdate is null)
                {
                    relationship.OnUpdate = existing.OnUpdate;
                }

                if (specified?.ConstraintName is null)
                {
                    relationship.ConstraintName = existing.ConstraintName;
                }
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

        // C# リバースは単一キー前提のため、先頭の列ペアだけを端点として扱う
        var firstPair = relationship.ColumnPairs.FirstOrDefault();

        return new RelationshipEndpoints(
            SourceEntityId: relationship.SourceEntityId,
            SourceColumnName: ResolveColumnName(sourceColumns, firstPair?.SourceColumnId),
            TargetEntityId: relationship.TargetEntityId,
            TargetColumnName: ResolveColumnName(targetColumns, firstPair?.TargetColumnId)
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
