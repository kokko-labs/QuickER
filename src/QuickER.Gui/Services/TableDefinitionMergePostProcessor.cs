using QuickER.Model;

namespace QuickER.Services;

/// <summary>
/// Excel 定義書のマージ取込で、定義書が運べない情報を現在図から引き継ぐ後処理
/// </summary>
/// <remarks>
/// <para>
/// 定義書は計算列の指定と、一意制約の構成列の順序・名前と、複合主キーの順序を書かない（キー欄の <c>UQ{n}</c> と
/// <c>PK</c> は列の行に付くので、取込は構成列を列の並び順で組み直し、一意制約の名前は持たない）。マージ取込でこれらを
/// 取込値のまま採ると、定義書を書き出して読み戻しただけで計算列が通常の列に戻り、<c>UNIQUE (b, a)</c> が
/// <c>(a, b)</c>・名前つき制約が合成名に変わり、<c>PRIMARY KEY (b, a)</c> が <c>(a, b)</c> に変わる
/// （次の DB 同期で主キーの張り直しが出る）。
/// 正本は現在図にしかないため、Guid 引継（<c>DiagramMergeReconciler.Reconcile</c>）の後で現在図から引き継ぐ
/// （C# コード取込の <c>ReverseMergePostProcessor</c> と同じ流儀）。
/// </para>
/// <para>
/// <c>DiagramMergeReconciler</c> には入れない。DB 取込・C# コード取込は、計算列も一意制約の順序・名前も自分で持ち帰る（正本が取込側にある）ため。
/// </para>
/// </remarks>
internal static class TableDefinitionMergePostProcessor
{
    /// <summary>Guid 引継済みのマージ結果へ、現在図の計算列の指定と一意制約の順序・名前と主キーの順序を書き戻す</summary>
    /// <param name="current">取込前の現在図</param>
    /// <param name="mergedEntities">Guid 引継済みのマージ結果エンティティ（その場で書き換える）</param>
    /// <remarks>
    /// <list type="bullet">
    /// <item>計算列: 現在図と同じ Id の列（＝名前で一致した列）の <see cref="Column.IsComputed"/> を引き継ぐ</item>
    /// <item>一意制約: 構成列の集合が現在図のいずれかの制約と同じなら、その制約の構成列の順序を引き継ぎ、
    ///   現在図に名前があれば名前も引き継ぐ（現在図の各制約は 1 回だけ使う）。集合が違う制約は取込値のまま</item>
    /// <item>主キーの順序: 現在図が明示していて、主キー列の集合が列名で一致するなら、その順序を引き継ぐ
    ///   （<see cref="Entity.InheritPrimaryKeyOrderFrom"/>＝C# コード取込のマージと同じ規則）</item>
    /// </list>
    /// </remarks>
    public static void Apply(ErDiagram current, IReadOnlyList<Entity> mergedEntities)
    {
        var currentById = current.Entities.ToDictionary(entity => entity.Id);

        foreach (var entity in mergedEntities)
        {
            if (!currentById.TryGetValue(entity.Id, out var existing))
            {
                continue;
            }

            var existingColumns = existing.Columns.ToDictionary(column => column.Id);

            foreach (var column in entity.Columns)
            {
                if (existingColumns.TryGetValue(column.Id, out var existingColumn))
                {
                    column.IsComputed = existingColumn.IsComputed;
                }
            }

            InheritUniqueConstraints(existing, entity);
            entity.InheritPrimaryKeyOrderFrom(existing);
        }
    }

    /// <summary>構成列の集合が同じ一意制約へ、現在図の順序と名前を書き戻す</summary>
    private static void InheritUniqueConstraints(Entity existing, Entity merged)
    {
        var unused = existing.UniqueConstraints.ToList();

        foreach (var constraint in merged.UniqueConstraints)
        {
            var source = unused.FirstOrDefault(candidate =>
                candidate.ColumnIds.Count == constraint.ColumnIds.Count
                && candidate.ColumnIds.ToHashSet().SetEquals(constraint.ColumnIds)
            );

            if (source is null)
            {
                continue;
            }

            unused.Remove(source);
            constraint.ColumnIds = source.ColumnIds.ToList();

            if (!string.IsNullOrWhiteSpace(source.Name))
            {
                constraint.Name = source.Name;
            }
        }
    }
}
