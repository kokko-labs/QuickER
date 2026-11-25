using System.Collections.Generic;
using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Assessment;

/// <summary>
/// 前寄せの組（<see cref="Refusals.FindCertain"/>）に後ろで付け直すキーの削除が 2 つ以上あるとき、一意制約の作り直し 1 つを取り下げる
/// </summary>
/// <remarks>
/// <para>
/// 組の最後の削除だけが断られ、前の削除は通るので、付け直すキーの削除が 2 つ以上あると、どう並べても前の方の付け直すキーが消える。
/// 組（付け直すキーの削除を最後へ並べた後）の最初の、後ろで付け直す一意制約を外す文の由来が、利用者の選んだ一意制約の作り直し
/// （<see cref="SelectedOrigin"/> で項目の種類が <see cref="SchemaDiffKind.AlterUniqueConstraint"/>）なら、その項目を取り下げる。
/// 取り下げた一意制約が古い形で外部キーを支え続けるので、ほかの削除は通る。由来が作り直しでなければ取り下げない（前寄せだけ）。
/// </para>
/// <para>
/// 名指しは <see cref="SyncPlanWarningKind.UniqueConstraintRebuildKeptForForeignKey"/>：表は外す文の表名のまま、Detail は
/// 「一意制約の名前 (阻む外部キーの実効名)」（実効名は DB の今の図の外部キーの実効名＝<see cref="PlanIdentity.ResolveForeignKeyName"/>）。
/// </para>
/// </remarks>
internal static class UniqueRebuildWithdrawal
{
    /// <summary>取り下げる作り直しと、その名指し（取り下げないなら <c>null</c>）</summary>
    /// <param name="steps">前寄せを当てる前の文の並び（判定の位置はこの並びの位置）</param>
    /// <param name="refusal">必ず断られる文の判定</param>
    /// <param name="facts">計画の事実（DB の今の図があること）</param>
    public static WithdrawnRebuild? Find(
        IReadOnlyList<SyncStep> steps,
        CertainRefusal refusal,
        AssessmentFacts facts
    )
    {
        var index = refusal.FirstReaddedUniqueDrop;
        var blocking = refusal.BlockingForeignKey;

        if (refusal.ReaddedDrops < 2 || index is null || blocking is null)
        {
            return null;
        }

        var drop = steps[index.Value] as DropUniqueConstraintStep;
        var alter =
            drop?.Origin is SelectedOrigin selected
            && selected.Item.Kind == SchemaDiffKind.AlterUniqueConstraint
                ? selected.Item
                : null;

        if (drop?.Name is null || alter is null)
        {
            return null;
        }

        var limit = facts.Capabilities.IdentifierLengthLimit;
        var foreignKeyName = PlanIdentity.ResolveForeignKeyName(
            blocking.Relationship,
            SchemaDiffService.NormalizeTable(blocking.Child),
            SchemaDiffService.NormalizeTable(blocking.Parent),
            limit,
            facts.Live
        );

        return new WithdrawnRebuild(
            alter,
            new SyncPlanWarning(
                SyncPlanWarningKind.UniqueConstraintRebuildKeptForForeignKey,
                drop.TableName,
                $"{drop.Name} ({foreignKeyName})"
            )
        );
    }
}

/// <summary>取り下げた一意制約の作り直しと、その名指し</summary>
/// <param name="Item">取り下げた利用者の選んだ項目（一意制約の作り直し）</param>
/// <param name="Warning">名指しの警告</param>
internal sealed record WithdrawnRebuild(SchemaDiffItem Item, SyncPlanWarning Warning);
