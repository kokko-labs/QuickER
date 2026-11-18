using System;
using System.Collections.Generic;
using QuickER.Dialects;
using QuickER.Provider.Sync.Assessment;
using QuickER.Provider.Sync.Scheduling;
using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync;

/// <summary>
/// 逐次 DDL の方言の計画を、文の並びとして作る（選択 → 組み立て → 評価）
/// </summary>
/// <remarks>
/// <para>段：</para>
/// <list type="number">
/// <item>選択：選んだ項目を外し終え、選んだ項目の最終形とその同期後の事実を作る（<see cref="SyncPlanner.PrepareSelection"/>）。</item>
/// <item>組み立て：文の並びを組む（<see cref="SequentialScheduler"/>）。</item>
/// <item>評価：規則の表を流して、項目を外さずに積む警告を決める（<see cref="AssessmentRules.Assess"/>）。</item>
/// </list>
/// <para>
/// 結果は文の並び・警告・外した項目。必ず断られる文の前寄せと、文の位置を読む警告は持たない（前寄せした文の数はいつも 0）。
/// </para>
/// </remarks>
internal static class SequentialPlanner
{
    /// <summary>選んだ差分の項目から、文の並びの計画を作る</summary>
    /// <param name="items">全差分項目（未選択の項目は読まない）</param>
    /// <param name="capabilities">対象方言の同期ケーパビリティ（逐次 DDL の方言）</param>
    /// <param name="context">計画の入力（無ければ <c>null</c>）</param>
    /// <returns>計画か、文で表せない理由</returns>
    /// <exception cref="NotSupportedException">テーブル再構築の方言を渡した</exception>
    public static SequentialPlan Plan(
        IEnumerable<SchemaDiffItem> items,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context
    )
    {
        var live = LiveSchema.From(context);
        var warnings = new List<SyncPlanWarning>();
        var excluded = new List<SchemaDiffItem>();

        // 同期後の状態の差の項目を足す関数が積む警告は受け取るだけで使わない（同じ警告は、評価の規則が組み立てた文から作る）
        var prepared = SyncPlanner.PrepareSelection(
            items,
            capabilities,
            context,
            live,
            warnings,
            excluded,
            injectedWarnings: []
        );

        if (prepared.IsRebuildDialect)
        {
            throw new NotSupportedException(
                "The sequential planner does not plan table-rebuild dialects."
            );
        }

        var schedule = SequentialScheduler.Schedule(prepared.Facts.PostSync, live, capabilities);
        var steps = schedule.Steps;

        if (steps is null)
        {
            return new SequentialPlan(null, schedule.Unrepresentable);
        }

        AssessmentRules.Assess(new PlanAssessment(prepared.Facts, schedule), warnings);

        return new SequentialPlan(new StepPlan(steps, 0, warnings, excluded), null);
    }
}

/// <summary>逐次 DDL の方言の計画の結果</summary>
/// <param name="Plan">文の並びの計画（文で表せない項目があれば <c>null</c>）</param>
/// <param name="Unrepresentable">文で表せない理由（表せれば <c>null</c>）</param>
internal sealed record SequentialPlan(StepPlan? Plan, string? Unrepresentable);
