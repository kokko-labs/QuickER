using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Assessment;

/// <summary>
/// 既存の行で断られうる文の前で外し、その文か後ろで付け直す制約を名指しする（DDL が暗黙コミットされる方言）
/// </summary>
/// <remarks>
/// <para>
/// 文の並び（前寄せを当てた後）を歩いた結果と計画の事実を受け、警告の並びを返す。列が空でないと通らない列の変更は
/// <see cref="SyncPlanWarningKind.ConstraintRebuildAroundEmptyColumnChange"/>、ほかの断られうる文は
/// <see cref="SyncPlanWarningKind.ConstraintRebuildAroundRowDependentChange"/> で名指しする（断られうる文は <see cref="Refusals.FindRefusable"/>）。
/// 位置を読むので、評価の規則の表（<see cref="AssessmentRules.All"/>）には載せない。
/// </para>
/// <para>
/// 範囲：制約を外す文の位置から、同じ身元（<see cref="ConstraintIdentity.Key"/>）を付ける最初の文まで。列が空でないと通らない変更は
/// 両端を含まず、ほかの断られうる文は付ける文を含む（断られうる文そのものが付け直す制約は、その文が断られると外れたまま残る）。
/// 付ける文が無ければ、利用者が選んだ削除でない限り、外す文より後ろの全部（消すはずの表・列や置き換える主キーが来ないまま外れて残る）。
/// </para>
/// <para>
/// 利用者が選んだ削除：文の由来が利用者の選んだ項目（<see cref="SelectedOrigin"/>）で、その項目が外部キー・一意制約の作り直しでない
/// （作り直しを展開した削除は付け直しを伴う）。
/// </para>
/// <para>
/// 警告の並び：外す文の位置の順に、制約ごとに列が空でないと通らない変更 → ほかの断られうる文。表は身元の表、Detail は
/// 「制約名 (文面, …)」（文面は位置の順に重複を除いたもの。主キーの制約名は <c>PRIMARY KEY</c>）。挟む文が無ければ積まない。
/// </para>
/// </remarks>
internal static class RefusableChangeNaming
{
    /// <summary>断られうる文の前後で外れている制約を名指しする警告の並び</summary>
    /// <param name="walk">前寄せを当てた後の文の並びを、DB の今の図から歩いた結果</param>
    /// <param name="facts">計画の事実（DB の今の図があること）</param>
    public static IEnumerable<SyncPlanWarning> Constraints(PlanWalk walk, AssessmentFacts facts)
    {
        var refusable = Refusals.FindRefusable(walk, facts);

        if (refusable.EmptyColumn.Count == 0 && refusable.RowDependent.Count == 0)
        {
            yield break;
        }

        var positions = walk.Positions;

        foreach (var drop in positions)
        {
            var dropped = drop.Drops;

            if (dropped is null)
            {
                continue;
            }

            var readd = -1;

            for (var index = drop.Index + 1; index < positions.Count; index++)
            {
                if (positions[index].Adds?.Key == dropped.Key)
                {
                    readd = index;
                    break;
                }
            }

            // 付け直さない削除のうち利用者が選んだものは、利用者が消すと決めた制約なので、断られうる文が後ろにあっても名指ししない
            // （名指しするのは、付け直すはずの制約と、消すはずの表・列や置き換える主キーが来ないまま外れて残る暗黙の削除）
            if (readd < 0 && IsUserSelectedDrop(drop.Step))
            {
                continue;
            }

            // 範囲：付け直すなら付ける文まで（列が空でないと通らない変更は付ける文を含まず、ほかの断られうる文は含む）、
            // 付け直さないなら外す文より後ろの全部
            var empty = refusable.EmptyColumn.Where(c =>
                c.Index > drop.Index && (readd < 0 || c.Index < readd)
            );
            var rowDependent = refusable.RowDependent.Where(c =>
                c.Index > drop.Index && (readd < 0 || c.Index <= readd)
            );

            var emptyWarning = Warning(
                SyncPlanWarningKind.ConstraintRebuildAroundEmptyColumnChange,
                dropped,
                empty
            );

            if (emptyWarning is not null)
            {
                yield return emptyWarning;
            }

            var rowDependentWarning = Warning(
                SyncPlanWarningKind.ConstraintRebuildAroundRowDependentChange,
                dropped,
                rowDependent
            );

            if (rowDependentWarning is not null)
            {
                yield return rowDependentWarning;
            }
        }
    }

    /// <summary>利用者が選んだ削除か（作り直しを展開した削除でない、利用者の選んだ項目から来た文）</summary>
    private static bool IsUserSelectedDrop(SyncStep step) =>
        step.Origin is SelectedOrigin selected
        && selected.Item.Kind
            is not SchemaDiffKind.AlterForeignKey
                and not SchemaDiffKind.AlterUniqueConstraint;

    /// <summary>挟む文があれば、制約 1 つの警告（無ければ <c>null</c>）</summary>
    private static SyncPlanWarning? Warning(
        SyncPlanWarningKind kind,
        ConstraintIdentity identity,
        IEnumerable<RefusableChange> between
    )
    {
        var texts = between.Select(c => c.Text).Distinct(StringComparer.Ordinal).ToList();

        return texts.Count == 0
            ? null
            : new SyncPlanWarning(
                kind,
                identity.Table,
                $"{identity.Name} ({string.Join(", ", texts)})"
            );
    }
}
