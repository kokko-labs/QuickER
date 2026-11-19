using System.Collections.Generic;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Assessment;

internal static partial class AssessmentRules
{
    /// <summary>
    /// 選んだ列の削除で構成列が消えるので暗黙に外す一意制約を、
    /// <see cref="SyncPlanWarningKind.UniqueConstraintDroppedWithColumn"/> で名指しする
    /// </summary>
    /// <param name="assessment">計画の事実と、組み立ての結果（選んだ項目の最終形の一意制約を外す文）</param>
    /// <returns>名指しの警告（選んだ項目の最終形の項目の順。Detail は「制約名 (構成列, …)」）</returns>
    /// <remarks>
    /// 選んだ項目の最終形から写した一意制約を外す文（<see cref="Scheduling.SequentialSchedule.SelectedUniqueConstraintDrops"/>。
    /// テーブル再構築の方言で再構築へ畳む削除も含む）のうち、理由が <see cref="ImplicitStepReason.VanishingUniqueConstraint"/> の文
    /// 1 つにつき 1 件。警告は文の値から作るので、外す文の無い警告も、警告の無い外す文も出ない。
    /// </remarks>
    public static IEnumerable<SyncPlanWarning> WarnUniqueConstraintsDroppedWithColumns(
        PlanAssessment assessment
    ) =>
        ImplicitUniqueConstraintDrops(
            assessment,
            ImplicitStepReason.VanishingUniqueConstraint,
            SyncPlanWarningKind.UniqueConstraintDroppedWithColumn
        );

    /// <summary>
    /// 新しい主キーと同じ列集合なので暗黙に外す一意制約（主キーと同じ列集合の一意制約を持てない方言）を、
    /// <see cref="SyncPlanWarningKind.UniqueConstraintReplacedByPrimaryKey"/> で名指しする
    /// </summary>
    /// <param name="assessment">計画の事実と、組み立ての結果（選んだ項目の最終形の一意制約を外す文）</param>
    /// <returns>名指しの警告（選んだ項目の最終形の項目の順。Detail は「制約名 (構成列, …)」）</returns>
    /// <remarks>
    /// 選んだ項目の最終形から写した一意制約を外す文（<see cref="Scheduling.SequentialSchedule.SelectedUniqueConstraintDrops"/>）のうち、
    /// 理由が <see cref="ImplicitStepReason.UniqueConstraintReplacedByPrimaryKey"/> の文 1 つにつき 1 件。
    /// </remarks>
    public static IEnumerable<SyncPlanWarning> WarnUniqueConstraintsReplacedByPrimaryKey(
        PlanAssessment assessment
    ) =>
        ImplicitUniqueConstraintDrops(
            assessment,
            ImplicitStepReason.UniqueConstraintReplacedByPrimaryKey,
            SyncPlanWarningKind.UniqueConstraintReplacedByPrimaryKey
        );

    /// <summary>理由が <paramref name="reason"/> の一意制約を外す文を、種別 <paramref name="kind"/> の警告にする</summary>
    private static List<SyncPlanWarning> ImplicitUniqueConstraintDrops(
        PlanAssessment assessment,
        ImplicitStepReason reason,
        SyncPlanWarningKind kind
    )
    {
        var limit = assessment.Capabilities.IdentifierLengthLimit;
        var warnings = new List<SyncPlanWarning>();

        foreach (var step in assessment.Schedule.SelectedUniqueConstraintDrops)
        {
            if (step is { Origin: ImplicitOrigin origin } drop && origin.Reason == reason)
            {
                warnings.Add(
                    new SyncPlanWarning(
                        kind,
                        drop.TableName,
                        $"{UniqueConstraintNaming.Resolve(drop.Name, drop.TableName, drop.Columns, limit)} ({string.Join(", ", drop.Columns)})"
                    )
                );
            }
        }

        return warnings;
    }
}
