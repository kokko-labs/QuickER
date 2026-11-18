using System.Collections.Generic;
using System.Linq;

namespace QuickER.Provider.Sync.Scheduling;

internal static partial class RebuildDeclarations
{
    /// <summary>
    /// 外部キーの両端の列の型を 1 列ずつ変える間、片側だけ変えた途中の状態で両端の型が並べられない外部キーは外れていなければならない
    /// （列の変更で外部キーの外し直しを要しない方言＝
    /// <see cref="QuickER.Dialects.SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild"/> が偽）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 対象は、ある列の組の両端とも型がこの同期で決まり（<see cref="State.PostSyncSchema.TypeDecidingColumns"/>）、「新しい子 × 古い親」か
    /// 「古い子 × 新しい親」のどちらかが <see cref="QuickER.Dialects.SyncDialectCapabilities.AreForeignKeyCompatibleTypes"/> で
    /// 並べられない外部キー。列の変更の順はどちらもありうるので、どちらかが並べられなければ対象にする。
    /// </para>
    /// <para>
    /// これらの方言は外部キーを張ったまま列を変えるので、外さないと最初の列の変更で DB が断る。片側だけ決まる組は、途中の状態が
    /// 同期後の状態と同じなので対象にしない。
    /// </para>
    /// </remarks>
    private static IEnumerable<LiveConstraint> RequireForeignKeysOffDivergingColumnChanges(
        SchedulingInput input
    )
    {
        var capabilities = input.Capabilities;

        if (capabilities.AlterColumnRequiresForeignKeyRebuild)
        {
            yield break;
        }

        var typeDecidingColumns = input.PostSync.TypeDecidingColumns;

        if (typeDecidingColumns.Count == 0)
        {
            yield break;
        }

        foreach (var fk in input.Live.ForeignKeys)
        {
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            if (
                fk.ColumnPairs.Any(pair =>
                    typeDecidingColumns.TryGetValue(
                        SyncPlanner.ColumnKey(childTable, pair.ChildColumn),
                        out var newChild
                    )
                    && typeDecidingColumns.TryGetValue(
                        SyncPlanner.ColumnKey(parentTable, pair.ParentColumn),
                        out var newParent
                    )
                    && (
                        !capabilities.AreForeignKeyCompatibleTypes(
                            newChild,
                            SyncPlanner.ColumnTypeOf(fk.Parent, pair.ParentColumn)
                        )
                        || !capabilities.AreForeignKeyCompatibleTypes(
                            SyncPlanner.ColumnTypeOf(fk.Child, pair.ChildColumn),
                            newParent
                        )
                    )
                )
            )
            {
                yield return new LiveForeignKey(fk);
            }
        }
    }
}
