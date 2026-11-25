using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider.Sync.State;

namespace QuickER.Provider.Sync.Selection;

/// <summary>
/// 同期後も残る DB の外部キー・一意制約（選んだ削除・作り直しで外れず、表・構成列も選んだ削除で消えないもの）。足す制約の名前・定義が
/// 重なるかを、方言の宣言（<see cref="SyncDialectCapabilities.ConstraintNameScope"/>・
/// <see cref="SyncDialectCapabilities.AllowsDuplicateForeignKeyDefinitions"/>）で判定する
/// </summary>
/// <remarks>
/// 主キーの名前は取り込まない（同期は実行時に逆引きする）ので数えない。端が消える外部キー・構成列が消える一意制約は、同期が追加より前の
/// 段で暗黙に外すので残る側に数えない（<see cref="PostSyncDifferenceItems.InjectVanishingForeignKeyDrops"/>・<see cref="PostSyncDifferenceItems.InjectVanishingUniqueConstraintDrops"/>）。
/// 名前は図全体から求めた実効名で、比べ方は <see cref="SchemaNames"/> の規則。
/// </remarks>
internal sealed class RemainingLiveConstraints
{
    private readonly List<(
        string Child,
        string Parent,
        string Name,
        string PairSet
    )> _foreignKeys = [];

    private readonly List<(string Table, string Name)> _uniques = [];

    /// <summary>選んだ項目（作り直しを展開した並び）の同期後の事実と DB の図から集める</summary>
    /// <param name="expanded">作り直しを展開した並びの同期後の事実（外す外部キー・一意制約と消える表・列を読む）</param>
    /// <param name="capabilities">方言のケーパビリティ（外部キーの実効名の上限）</param>
    /// <param name="liveSchema">DB の今の図</param>
    public static RemainingLiveConstraints From(
        PostSyncSchema expanded,
        SyncDialectCapabilities capabilities,
        LiveSchema liveSchema
    )
    {
        var limit = capabilities.IdentifierLengthLimit;
        var remaining = new RemainingLiveConstraints();
        var droppedUniques = expanded.DroppedUniqueConstraints;
        var droppedForeignKeys = expanded.DroppedForeignKeys;
        var droppedObjects = expanded.Dropped;
        var liveNames = liveSchema.ForeignKeyNames(limit);

        foreach (var fk in liveSchema.ForeignKeys)
        {
            var child = SchemaDiffService.NormalizeTable(fk.Child);
            var parent = SchemaDiffService.NormalizeTable(fk.Parent);

            if (
                droppedObjects.Touches(fk, child, parent)
                || droppedForeignKeys.Contains(
                    PlanIdentity.ForeignKeyKey(
                        child,
                        parent,
                        fk.ColumnPairs,
                        fk.Relationship,
                        limit
                    )
                )
            )
            {
                continue;
            }

            if (!liveNames.TryGetValue(fk.Relationship, out var name))
            {
                continue;
            }

            remaining._foreignKeys.Add((child, parent, name, PairSet(fk.ColumnPairs)));
        }

        foreach (var entity in liveSchema.Entities)
        {
            var table = SchemaDiffService.NormalizeTable(entity);

            if (droppedObjects.Tables.Contains(table))
            {
                continue;
            }

            foreach (var constraint in entity.UniqueConstraints)
            {
                if (
                    UniqueConstraintNaming.TryResolveColumnNames(
                        entity,
                        constraint,
                        out var columns
                    )
                    && !droppedObjects.TouchesColumns(table, columns)
                    && !droppedUniques.Contains(
                        PlanIdentity.UniqueConstraintKey(table, constraint.Name, columns)
                    )
                )
                {
                    remaining._uniques.Add(
                        (
                            table,
                            UniqueConstraintNaming.Resolve(constraint.Name, table, columns, limit)
                        )
                    );
                }
            }
        }

        return remaining;
    }

    /// <summary>足す制約と、方言の名前の範囲で名前が重なる残る制約（「表.名前」）</summary>
    public IEnumerable<string> NamedLike(
        string name,
        string table,
        bool isForeignKey,
        SyncDialectCapabilities capabilities
    ) =>
        _foreignKeys
            .Where(f =>
                SchemaNames.AreSame(f.Name, name)
                && capabilities.ConstraintNamesCollide(isForeignKey, table, true, f.Child)
            )
            .Select(f => $"{f.Child}.{f.Name}")
            .Concat(
                _uniques
                    .Where(u =>
                        SchemaNames.AreSame(u.Name, name)
                        && capabilities.ConstraintNamesCollide(isForeignKey, table, false, u.Table)
                    )
                    .Select(u => $"{u.Table}.{u.Name}")
            );

    /// <summary>
    /// 同じ定義の外部キーを 2 本持てない方言で、足す外部キーと定義（子の表・親の表・列の組の集合）が同じ残る外部キー（「表.名前」）
    /// </summary>
    public IEnumerable<string> SameDefinitionAs(
        string child,
        string parent,
        IReadOnlyList<ForeignKeyColumnNamePair> pairs,
        SyncDialectCapabilities capabilities
    )
    {
        if (capabilities.AllowsDuplicateForeignKeyDefinitions)
        {
            return [];
        }

        var pairSet = PairSet(pairs);
        return _foreignKeys
            .Where(f =>
                SchemaNames.AreSame(f.Child, child)
                && SchemaNames.AreSame(f.Parent, parent)
                && f.PairSet == pairSet
            )
            .Select(f => $"{f.Child}.{f.Name}");
    }

    /// <summary>列の組の集合のキー（並びを見ない。名前は <see cref="SchemaNames.Key"/> で畳む）</summary>
    private static string PairSet(IEnumerable<ForeignKeyColumnNamePair> pairs) =>
        string.Concat(
            pairs
                .Select(p =>
                    SignatureText.Part(SchemaNames.Key(p.ParentColumn))
                    + SignatureText.Part(SchemaNames.Key(p.ChildColumn))
                )
                .Order(StringComparer.Ordinal)
        );
}
