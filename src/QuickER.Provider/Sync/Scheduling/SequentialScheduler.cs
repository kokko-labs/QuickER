using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Scheduling;

/// <summary>
/// 逐次 DDL の方言の文の並びを組み立てる（選んだ項目の最終形の同期後の事実と、DB の今の図と、方言の宣言から）
/// </summary>
/// <remarks>
/// <para>組み立ての段：</para>
/// <list type="number">
/// <item>選んだ項目の最終形を文へ写す（<see cref="SelectedSteps"/>）。</item>
/// <item>外し直しの宣言（<see cref="RebuildDeclarations"/>）から、選んだ文を流す間外れていなければならない制約を集め、外す文と
/// 付け直す文の対を足す（<see cref="Readds"/>。付け直せない制約は外さない＝<see cref="UnreaddableConstraints"/>）。外す文は
/// 選んだ文より前、付け直す文は選んだ文より後ろへ置く。DB の今の図が無ければ足さない。</item>
/// <item>参照する列の並びがキーの並びと一致しないと外部キーを作れない方言では、外部キーを付ける文の列の組を参照先の同期後の
/// キーの並びへ並べ替える（<see cref="ForeignKeyPairOrder"/>）。</item>
/// <item>種類の固定の順に並べる（<see cref="StepOrder"/>）。</item>
/// <item>DDL が暗黙コミットされる方言では、外部キーを付ける文を断られにくい順に並べる（<see cref="ForeignKeyAddOrder"/>）。</item>
/// <item>列順を同期する方言では、列を動かす文を後ろへ足す。</item>
/// </list>
/// <para>
/// 結果には、選んだ項目の最終形の一意制約の削除を項目の順のまま写した文（<see cref="SequentialSchedule.SelectedUniqueConstraintDrops"/>）も
/// 添える。
/// </para>
/// <para>
/// 結果は文の並びだけで、警告・外した項目・必ず断られる文の前寄せは持たない。差分の項目も警告も作らない。同期後の事実は受けた
/// ものを読み、作り直さない。
/// </para>
/// </remarks>
internal static class SequentialScheduler
{
    /// <summary>文の並びを組み立てる</summary>
    /// <param name="postSync">選んだ項目の最終形（<see cref="PlanStage.Selected"/>）の同期後の事実</param>
    /// <param name="live">DB の今の図（無ければ null）</param>
    /// <param name="capabilities">方言の宣言</param>
    /// <returns>組み立ての結果</returns>
    public static SequentialSchedule Schedule(
        PostSyncSchema postSync,
        LiveSchema? live,
        SyncDialectCapabilities capabilities
    )
    {
        var (selected, unrepresentable) = SelectedSteps.From(postSync.Items);

        if (selected is null)
        {
            return new SequentialSchedule(null, unrepresentable);
        }

        IEnumerable<ScheduledStep> steps = selected;
        RequiredConstraints? requiredOff = null;

        if (live is not null)
        {
            var input = new SchedulingInput(selected, postSync, live, capabilities);
            var required = RebuildDeclarations.Collect(input);
            requiredOff = required;
            var foreignKeys = Readds.ForeignKeys(required.ForeignKeys, input);
            var uniques = Readds.UniqueConstraints(required.UniqueConstraints, input);
            steps =
            [
                .. uniques.Drops,
                .. foreignKeys.Drops,
                .. selected,
                .. foreignKeys.Adds,
                .. uniques.Adds,
                .. Readds.PrimaryKeys(required.PrimaryKeys, input),
            ];
        }

        if (capabilities.ReferencedKeyRequiresSameColumnOrder)
        {
            steps = ForeignKeyPairOrder.Arrange(steps, postSync, live);
        }

        var ordered = StepOrder.Arrange(steps);

        if (!capabilities.TransactionalDdl)
        {
            ordered = ForeignKeyAddOrder.Arrange(ordered, postSync, live, capabilities);
        }

        return new SequentialSchedule(
            [.. ordered.Select(s => s.Step), .. ColumnMoves(postSync.Items, live, capabilities)],
            null,
            requiredOff
        )
        {
            SelectedUniqueConstraintDrops = SelectedSteps.UniqueConstraintDrops(postSync.Items),
        };
    }

    /// <summary>列を動かす文（列順を同期する方言で、選んだ列順の変更から）</summary>
    private static IEnumerable<SyncStep> ColumnMoves(
        IReadOnlyList<SchemaDiffItem> items,
        LiveSchema? live,
        SyncDialectCapabilities capabilities
    )
    {
        if (capabilities.ColumnReorder != ColumnReorderMode.Native)
        {
            yield break;
        }

        foreach (var reorder in SyncPlanner.BuildReorderPlans([.. items], live))
        {
            foreach (var move in reorder.Moves)
            {
                yield return new MoveColumnStep(
                    new SelectedOrigin(reorder.SourceItems[0]),
                    reorder.TableName,
                    move.Column,
                    move.AfterColumn
                );
            }
        }
    }
}
