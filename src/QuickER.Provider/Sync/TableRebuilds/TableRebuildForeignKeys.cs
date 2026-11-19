using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider.Sync.State;

namespace QuickER.Provider.Sync.TableRebuilds;

/// <summary>
/// テーブル再構築の表に張る外部キーの合成（<see cref="Synthesize"/>）と、外部キーの項目の解決
/// （<see cref="ResolveAdded"/>・<see cref="ResolveDroppedSignature"/>）
/// </summary>
internal static class TableRebuildForeignKeys
{
    /// <summary>再構築後に張る FK 集合を合成する（live 集合から Drop を除き Add を足す）</summary>
    /// <param name="tableItems">この表へ畳み込む選択済みの項目</param>
    /// <param name="liveForeignKeys">この表を子に持つ live の外部キー</param>
    internal static List<TableRebuildForeignKey> Synthesize(
        IReadOnlyList<SchemaDiffItem> tableItems,
        IEnumerable<ResolvedForeignKeyRelationship> liveForeignKeys,
        IdentifierLengthLimit limit,
        LiveSchema liveSchema
    )
    {
        var foreignKeys = liveForeignKeys
            .Select(fk => ToRebuildForeignKey(fk, limit, liveSchema))
            .ToList();

        // 選択済み DropForeignKey に一致する live FK を 1 本だけ除去する（親テーブル・構成列ペアで候補を絞り、
        // 名前・参照アクションまで合うものを優先する）。同じ列ペアに外部キーが 2 本ある構成で、1 本の削除・作り直しが
        // もう 1 本まで消さないため（構成列だけで照合すると 2 本とも外れ、変えていない外部キーが作り直した表から黙って消える）
        foreach (var dropFk in tableItems.Where(i => i.Kind == SchemaDiffKind.DropForeignKey))
        {
            var signature = ResolveDroppedSignature(dropFk);

            if (signature is null)
            {
                continue;
            }

            var (parentTable, columnPairs) = signature.Value;
            var candidates = foreignKeys
                .Where(fk =>
                    SchemaNames.AreSame(fk.ParentTable, parentTable)
                    && fk.ChildColumns.SequenceEqual(
                        ForeignKeyColumnPairResolver.ChildColumns(columnPairs),
                        SchemaNames.Comparer
                    )
                    && fk.ParentColumns.SequenceEqual(
                        ForeignKeyColumnPairResolver.ParentColumns(columnPairs),
                        SchemaNames.Comparer
                    )
                )
                .ToList();
            var dropName = dropFk.ChildEntity is null
                ? null
                : SyncPlanner.ResolveForeignKeyName(
                    dropFk.Relationship,
                    SchemaDiffService.NormalizeTable(dropFk.ChildEntity),
                    parentTable,
                    limit
                );
            var onDelete = dropFk.Relationship?.OnDelete ?? ForeignKeyReferentialAction.NoAction;
            var onUpdate = dropFk.Relationship?.OnUpdate ?? ForeignKeyReferentialAction.NoAction;
            var removed =
                candidates.FirstOrDefault(fk =>
                    SchemaNames.AreSame(fk.ConstraintName, dropName)
                    && fk.OnDelete == onDelete
                    && fk.OnUpdate == onUpdate
                )
                ?? candidates.FirstOrDefault(fk =>
                    fk.OnDelete == onDelete && fk.OnUpdate == onUpdate
                )
                ?? candidates.FirstOrDefault();

            if (removed is not null)
            {
                foreignKeys.Remove(removed);
            }
        }

        // 選択済み AddForeignKey を追加する
        foreach (var addFk in tableItems.Where(i => i.Kind == SchemaDiffKind.AddForeignKey))
        {
            var resolved = ResolveAdded(addFk, limit);

            if (resolved is not null)
            {
                foreignKeys.Add(resolved);
            }
        }

        return foreignKeys;
    }

    /// <summary>live の外部キーを、再構築の CREATE TABLE へ載せる FK 仕様へ変換する</summary>
    /// <remarks>名前は警告・外し直しと同じく図全体から求めた live の実効名（<see cref="LiveSchema.ForeignKeyNames"/>）。</remarks>
    private static TableRebuildForeignKey ToRebuildForeignKey(
        ResolvedForeignKeyRelationship fk,
        IdentifierLengthLimit limit,
        LiveSchema liveSchema
    )
    {
        var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

        return new TableRebuildForeignKey(
            SyncPlanner.ResolveForeignKeyName(
                fk.Relationship,
                SchemaDiffService.NormalizeTable(fk.Child),
                parentTable,
                limit,
                liveSchema
            ),
            [.. ForeignKeyColumnPairResolver.ChildColumns(fk.ColumnPairs)],
            parentTable,
            [.. ForeignKeyColumnPairResolver.ParentColumns(fk.ColumnPairs)],
            fk.Relationship.OnDelete,
            fk.Relationship.OnUpdate
        );
    }

    /// <summary>選択済み AddForeignKey 差分項目を解決済みの FK 仕様へ変換する（解決不能なら null）</summary>
    internal static TableRebuildForeignKey? ResolveAdded(
        SchemaDiffItem item,
        IdentifierLengthLimit limit
    )
    {
        if (item.ChildEntity is null || item.ParentEntity is null)
        {
            return null;
        }

        var columnPairs = SyncScriptBuilderHelper.ResolveColumnPairs(item);

        if (columnPairs.Count == 0)
        {
            return null;
        }

        var parentTable = SchemaDiffService.NormalizeTable(item.ParentEntity);

        return new TableRebuildForeignKey(
            SyncScriptBuilderHelper.ForeignKeyNameToAdd(item, limit),
            [.. ForeignKeyColumnPairResolver.ChildColumns(columnPairs)],
            parentTable,
            [.. ForeignKeyColumnPairResolver.ParentColumns(columnPairs)],
            item.Relationship?.OnDelete ?? ForeignKeyReferentialAction.NoAction,
            item.Relationship?.OnUpdate ?? ForeignKeyReferentialAction.NoAction
        );
    }

    /// <summary>
    /// DropForeignKey 差分項目を、live FK 照合用のシグネチャ（親テーブル・構成列ペア）へ解決する
    /// </summary>
    internal static (
        string ParentTable,
        IReadOnlyList<ForeignKeyColumnNamePair> ColumnPairs
    )? ResolveDroppedSignature(SchemaDiffItem item)
    {
        if (item.Relationship is null || item.ParentEntity is null || item.ChildEntity is null)
        {
            return null;
        }

        // 差分項目に載った列ペアを優先し、無ければリレーションから解決し直す
        // （差分計算が作った項目には必ず載るが、外部から組み立てた項目でもモデルから復元できるようにする）
        IReadOnlyList<ForeignKeyColumnNamePair>? columnPairs =
            SyncScriptBuilderHelper.ResolveColumnPairs(item);

        if (columnPairs.Count == 0)
        {
            columnPairs = ForeignKeyColumnPairResolver.Resolve(
                item.Relationship,
                item.ParentEntity,
                item.ChildEntity
            );
        }

        if (columnPairs is null || columnPairs.Count == 0)
        {
            return null;
        }

        return (SchemaDiffService.NormalizeTable(item.ParentEntity), columnPairs);
    }
}
