using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Provider.Sync.Assessment;
using QuickER.Provider.Sync.Scheduling;
using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.Steps;
using QuickER.Provider.Sync.TableRebuilds;

namespace QuickER.Provider.Sync;

/// <summary>
/// 計画を文の並びとして作る。逐次 DDL の方言は選択 → 組み立て → 評価 → 前寄せと取り下げの輪 → 前後の名指し、テーブル再構築の方言は
/// 選択 → 組み立て → 評価
/// </summary>
/// <remarks>
/// <para>
/// テーブル再構築の方言（<see cref="TableRebuildDialects.Requires"/>）は、選択（<see cref="SyncPlanner.PrepareSelection"/>）→ 組み立て
/// （<see cref="TableRebuildScheduler"/>）→ 評価（<see cref="AssessmentRules.Assess"/>）を 1 回だけ流す。前寄せ・取り下げ・前後の名指しは無い。
/// 計画の入力か DB の今の図が無ければ、選択の後に <see cref="InvalidOperationException"/>。
/// </para>
/// <para>逐次 DDL の方言の段（周ごとに繰り返す）：</para>
/// <list type="number">
/// <item>
/// 選択：取り下げた項目を除いた選んだ項目を外し終え、選んだ項目の最終形とその同期後の事実を作る
/// （<see cref="SyncPlanner.PrepareSelection"/>）。警告と外した項目の積み先は周ごとに新しく作る。
/// </item>
/// <item>組み立て：文の並びを組む（<see cref="SequentialScheduler"/>）。</item>
/// <item>評価：規則の表を流して、項目を外さずに積む警告を決める（<see cref="AssessmentRules.Assess"/>）。</item>
/// <item>
/// 前寄せと取り下げ（DDL が暗黙コミットされる方言で、DB の今の図があるときだけ）：文の並びを歩き、必ず断られる最初の文（キーの削除は組）を
/// 決める（<see cref="Refusals.FindCertain"/>）。組に後ろで付け直すキーの削除が 2 つ以上あり、取り下げる一意制約の作り直しが見つかれば
/// （<see cref="UniqueRebuildWithdrawal"/>）、その項目を取り下げて周の先頭（選択）へ戻る。見つからなければ組を先頭へ移す
/// （<see cref="MoveFirst"/>）。
/// </item>
/// <item>
/// 前後の名指し（同じ方言で）：移した後の並びを歩き直し、既存の行で断られうる文の前後で外れている制約を名指しする
/// （<see cref="RefusableChangeNaming"/>）。
/// </item>
/// </list>
/// <para>
/// 周の数は選んだ項目の数 + 1 までで、超えたら <see cref="InvalidOperationException"/>（取り下げは選んだ項目を 1 つずつ減らすだけなので、
/// 超えるのは取り下げた項目を次の周の選択から除いていないとき）。
/// </para>
/// <para>
/// 警告の並びは、選択の名指し → 評価の規則 → 取り下げの名指し（取り下げた順）→ 前後の名指し。外した項目の並びは、選択が外した項目 →
/// 取り下げた項目（取り下げた順）。
/// </para>
/// </remarks>
internal static class SequentialPlanner
{
    /// <summary>選んだ差分の項目から、文の並びの計画を作る</summary>
    /// <param name="items">全差分項目（未選択の項目は読まない）</param>
    /// <param name="capabilities">対象方言の同期ケーパビリティ（逐次 DDL の方言とテーブル再構築の方言）</param>
    /// <param name="context">計画の入力（無ければ <c>null</c>。テーブル再構築の方言では要る）</param>
    /// <returns>計画か、文で表せない理由</returns>
    /// <exception cref="InvalidOperationException">
    /// 周の数が上限を超えた（逐次 DDL の方言）か、テーブル再構築の方言で計画の入力が無い
    /// </exception>
    public static SequentialPlan Plan(
        IEnumerable<SchemaDiffItem> items,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context
    )
    {
        var all = items.ToList();
        var live = LiveSchema.From(context);

        if (TableRebuildDialects.Requires(capabilities))
        {
            return PlanTableRebuilds(all, capabilities, context, live);
        }

        var carried = new List<WithdrawnRebuild>();
        var rounds = all.Count(i => i.IsSelected) + 1;

        for (var round = 0; round < rounds; round++)
        {
            var withdrawn = new HashSet<SchemaDiffItem>(
                carried.Select(c => c.Item),
                ReferenceEqualityComparer.Instance
            );
            var warnings = new List<SyncPlanWarning>();
            var excluded = new List<SchemaDiffItem>();

            // 同期後の状態の差の項目を足す関数が積む警告は受け取るだけで使わない（同じ警告は、評価の規則が組み立てた文から作る）
            var prepared = SyncPlanner.PrepareSelection(
                all.Where(i => !withdrawn.Contains(i)),
                capabilities,
                context,
                live,
                warnings,
                excluded,
                injectedWarnings: []
            );

            var schedule = SequentialScheduler.Schedule(
                prepared.Facts.PostSync,
                live,
                capabilities
            );
            var steps = schedule.Steps;

            if (steps is null)
            {
                return new SequentialPlan(
                    null,
                    schedule.Unrepresentable,
                    prepared.Facts,
                    [.. carried.Select(c => c.Item)]
                );
            }

            AssessmentRules.Assess(new PlanAssessment(prepared.Facts, schedule), warnings);

            var movedFirst = 0;

            // DDL が暗黙コミットされる方言では、必ず断られる文を先頭へ移し（付け直すキーの削除が 2 つ以上の組は、一意制約の作り直しを
            // 取り下げて選択からやり直す）、移した後の並びで断られうる文の前後の制約を名指しする
            if (!capabilities.TransactionalDdl && live is not null)
            {
                var refusal = Refusals.FindCertain(
                    PlanWalk.Over(steps, live, capabilities),
                    prepared.Facts
                );

                if (refusal is not null)
                {
                    var withdrawal = UniqueRebuildWithdrawal.Find(steps, refusal, prepared.Facts);

                    if (withdrawal is not null)
                    {
                        carried.Add(withdrawal);
                        continue;
                    }

                    steps = MoveFirst.Apply(steps, refusal);
                    movedFirst = refusal.Moved.Count;
                }

                AssessmentRules.Add(warnings, carried.Select(c => c.Warning));
                AssessmentRules.Add(
                    warnings,
                    RefusableChangeNaming.Constraints(
                        PlanWalk.Over(steps, live, capabilities),
                        prepared.Facts
                    )
                );
            }

            excluded.AddRange(carried.Select(c => c.Item));

            return new SequentialPlan(
                new StepPlan(steps, movedFirst, warnings, excluded),
                null,
                prepared.Facts,
                [.. carried.Select(c => c.Item)]
            );
        }

        throw new InvalidOperationException(
            "The sequential planner did not settle within the round limit (the selected items + 1)."
        );
    }

    /// <summary>
    /// テーブル再構築の方言の計画を、文の並びとして作る（選択 → 組み立て → 評価）
    /// </summary>
    /// <remarks>
    /// 前寄せ・取り下げ・前後の名指しは無い（再構築の方言は表ごと作り直し、文を流す途中で外れたまま残る制約を作らない）ので、周は 1 回だけ。
    /// 計画の入力か DB の今の図が無ければ、選択の後に <see cref="InvalidOperationException"/>（今の計画器と同じ文面）。
    /// </remarks>
    private static SequentialPlan PlanTableRebuilds(
        List<SchemaDiffItem> all,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context,
        LiveSchema? live
    )
    {
        var warnings = new List<SyncPlanWarning>();
        var excluded = new List<SchemaDiffItem>();

        // 同期後の状態の差の項目を足す関数が積む警告は受け取るだけで使わない（同じ警告は、評価の規則が選んだ項目の最終形から作る）
        var prepared = SyncPlanner.PrepareSelection(
            all,
            capabilities,
            context,
            live,
            warnings,
            excluded,
            injectedWarnings: []
        );

        if (context is null || live is null)
        {
            // 再構築の合成には DB の今の図が要る（図の定義を直接使うと選んでいない変更が紛れ込む）
            throw new InvalidOperationException(
                "Rebuild dialects require a SyncPlanContext (live schema) to synthesize table rebuilds."
            );
        }

        var schedule = TableRebuildScheduler.Schedule(
            prepared.Facts.PostSync,
            live,
            context,
            capabilities
        );
        var steps = schedule.Steps;

        if (steps is null)
        {
            return new SequentialPlan(null, schedule.Unrepresentable, prepared.Facts, []);
        }

        AssessmentRules.Assess(new PlanAssessment(prepared.Facts, schedule), warnings);

        return new SequentialPlan(
            new StepPlan(steps, 0, warnings, excluded),
            null,
            prepared.Facts,
            []
        );
    }
}

/// <summary>新しい計画器の計画の結果（逐次 DDL の方言とテーブル再構築の方言）</summary>
/// <param name="Plan">文の並びの計画（文で表せない項目があれば <c>null</c>）</param>
/// <param name="Unrepresentable">文で表せない理由（表せれば <c>null</c>）</param>
/// <param name="Facts">
/// 最後の周の計画の事実（選んだ項目の最終形の同期後の事実・DB の今の図・方言の宣言ほか）。計画の外から計画を照らす照合
/// （文の並びを歩いた終わりの状態と、同期後に在る制約の突き合わせ）が読む
/// </param>
/// <param name="Withdrawn">
/// 前寄せの組から取り下げた一意制約の作り直しの項目（取り下げた順。無ければ空。テーブル再構築の方言はいつも空）。計画の外した項目の末尾にも
/// 同じ順で載る
/// </param>
internal sealed record SequentialPlan(
    StepPlan? Plan,
    string? Unrepresentable,
    AssessmentFacts Facts,
    IReadOnlyList<SchemaDiffItem> Withdrawn
);
