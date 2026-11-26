using System.Collections.Generic;
using QuickER.Provider.Sync.Selection;
using QuickER.Provider.Sync.State;

namespace QuickER.Provider.Sync.Assessment;

internal static partial class AssessmentRules
{
    /// <summary>
    /// <see cref="SyncDialectCapabilities.SetNullRequiresNullableChildColumn"/> が <c>false</c> の方言（SET NULL を作れる方言）で、
    /// 選んだ外部キーの追加・作り直しのうち、参照アクション <c>SET NULL</c> を使い、子の列が同期後に NOT NULL になるものを、
    /// 外さずに <see cref="SyncPlanWarningKind.SetNullOnNotNullColumn"/> で名指しする。作れない方言では何もしない
    /// （外して名指しするのは <see cref="SelectionRules.ExcludeSetNullForeignKeysOnNotNullColumns"/>）。
    /// </summary>
    /// <param name="facts">計画の事実（<see cref="AssessmentFacts.Settled"/>＝除外を終えた選択済みの項目で外部キーの作り直しは展開前と、その同期後の事実）</param>
    /// <returns>名指しの警告</returns>
    /// <remarks>
    /// 除外の周（<see cref="PlanSelection.ExcludeUntilStable"/>）の後に 1 回だけ、周が止まったときの状態で呼ぶ。周の中で積むと、警告した外部キーが
    /// 後の周で別の理由で外れても、計画に無い外部キーの警告が残る。判定は <see cref="SelectionRules.FindSetNullForeignKeysOnNotNullColumns"/>。
    /// </remarks>
    public static IEnumerable<SyncPlanWarning> WarnSetNullForeignKeysOnNotNullColumns(
        AssessmentFacts facts
    )
    {
        var state = facts.Settled;
        var capabilities = facts.Capabilities;
        var warnings = new List<SyncPlanWarning>();

        if (capabilities.SetNullRequiresNullableChildColumn)
        {
            return warnings;
        }

        foreach (
            var (
                _,
                childTable,
                constraintName,
                notNull
            ) in SelectionRules.FindSetNullForeignKeysOnNotNullColumns(state, capabilities)
        )
        {
            warnings.Add(PlanFacts.SetNullWarning(childTable, constraintName, notNull));
        }

        return warnings;
    }
}
