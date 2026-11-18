using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Scheduling;

/// <summary>
/// 外部キーを付ける文の列の組を、参照先の同期後のキーの並びへ並べ替える（参照する列の並びがキーの並びと一致しないと外部キーを
/// 作れない方言）
/// </summary>
/// <remarks>
/// <para>
/// 参照先の同期後のキーは、同期後の主キー（<see cref="PostSyncSchema.PrimaryKeyOf"/>）、続いて同期後の一意制約
/// （<see cref="PostSyncSchema.UniqueKeys"/>）。参照する列の集合がどのキーともちょうど一致しなければ並べ替えない。並べ替えの規則は
/// DDL と同じ <see cref="ForeignKeyColumnOrder.ArrangeToKey"/>。
/// </para>
/// <para>
/// DB の今の図が無いとき、または参照先の表の同期後の主キーが分からないときは、文が持つ参照先の表の定義
/// （<see cref="ScheduledStep.ParentDefinition"/>）のキーへそろえる。
/// </para>
/// <para>列の組が 1 つ以下の文は並べ替えない。</para>
/// </remarks>
internal static class ForeignKeyPairOrder
{
    /// <summary>外部キーを付ける文の列の組を並べ替える</summary>
    /// <param name="steps">文の並び</param>
    /// <param name="postSync">同期後の事実</param>
    /// <param name="live">DB の今の図（無ければ null）</param>
    /// <returns>外部キーを付ける文の列の組を並べ替えた並び（ほかの文はそのまま）</returns>
    public static List<ScheduledStep> Arrange(
        IEnumerable<ScheduledStep> steps,
        PostSyncSchema postSync,
        LiveSchema? live
    )
    {
        var postSyncUniques = postSync.UniqueKeys;

        return [.. steps.Select(ArrangeOne)];

        ScheduledStep ArrangeOne(ScheduledStep scheduled)
        {
            if (
                scheduled.Step is not AddForeignKeyStep add
                || add.ColumnPairs.Count < 2
                || scheduled.ParentDefinition is not { } parent
            )
            {
                return scheduled;
            }

            var parentTable = SchemaNames.ToStoredForm(add.ParentTable);
            var postSyncKey = live is null ? null : postSync.PrimaryKeyOf(parentTable);
            var arranged = postSyncKey is not null
                ? ForeignKeyColumnOrder.ArrangeToKey(
                    add.ColumnPairs,
                    PostSyncKeys(parentTable, postSyncKey)
                )
                : ForeignKeyColumnOrder.ArrangeToReferencedKey(add.ColumnPairs, parent);

            return arranged.SequenceEqual(add.ColumnPairs)
                ? scheduled
                : scheduled with
                {
                    Step = add with { ColumnPairs = arranged },
                };
        }

        IEnumerable<IReadOnlyList<string>> PostSyncKeys(
            string parentTable,
            IReadOnlyList<string> postSyncKey
        )
        {
            if (postSyncKey.Count > 0)
            {
                yield return postSyncKey;
            }

            if (postSyncUniques.TryGetValue(parentTable, out var uniques))
            {
                foreach (var unique in uniques)
                {
                    yield return unique;
                }
            }
        }
    }
}
