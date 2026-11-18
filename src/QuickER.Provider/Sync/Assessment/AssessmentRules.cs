using System.Collections.Generic;

namespace QuickER.Provider.Sync.Assessment;

/// <summary>
/// 項目を外さず、警告だけを作る規則（<c>Warn*</c>）と、規則を流す順の表と、警告を積む <see cref="Add"/>
/// </summary>
/// <remarks>
/// 規則は 1 つ 1 ファイル（<c>AssessmentRules.{規則}.cs</c>）に置く。どれも計画の事実（<see cref="AssessmentFacts"/>。組み立ての結果も
/// 読む規則は <see cref="PlanAssessment"/>）を受け取って警告の並びを返すだけで、警告を積む先は書き換えない。積むのは <see cref="Add"/> が
/// 受け持つ。
/// </remarks>
internal static partial class AssessmentRules
{
    /// <summary>
    /// 組み立てた計画を評価する規則の表（流す順）
    /// </summary>
    /// <remarks>
    /// 並びは仕様（警告の並びは、この表の順と、規則が返した並びで決まる）。方言や入力で掛からない規則も表に置き、掛からないときは
    /// 空の並びを返す（門は規則の中に持つ）。
    /// </remarks>
    internal static readonly AssessmentRule[] All =
    [
        WarnSetNullForeignKeysOnNotNullColumns,
        WarnUniqueConstraintsDroppedWithColumns,
        WarnUniqueConstraintsReplacedByPrimaryKey,
        WarnSetNullOnNotNullLiveForeignKeys,
        WarnUniqueConstraintDropsBreakingForeignKeys,
        WarnAddedForeignKeysNotOnCandidateKey,
        WarnSetDefaultOnNotNullColumns,
        WarnSchemaQualifiedTableNames,
        WarnDefaultsOnColumnTypeKindChange,
        WarnDescriptionCharactersReplaced,
        WarnLiveForeignKeysWithPostSyncTypeMismatch,
        WarnDetachedForeignKeysAtRisk,
    ];

    /// <summary>規則の表を順に流し、見つけた警告を積む</summary>
    /// <param name="assessment">計画の事実と、組み立ての結果</param>
    /// <param name="warnings">積む先（外した項目の名指しの警告が既に積まれている）</param>
    public static void Assess(PlanAssessment assessment, List<SyncPlanWarning> warnings)
    {
        foreach (var rule in All)
        {
            Add(warnings, rule(assessment));
        }
    }

    /// <summary>同じ値の警告を重ねない種別</summary>
    private static readonly HashSet<SyncPlanWarningKind> NonRepeatingKinds =
    [
        SyncPlanWarningKind.SetNullOnNotNullColumn,
        SyncPlanWarningKind.ForeignKeyColumnTypeMismatch,
        SyncPlanWarningKind.ConstraintRebuildAroundEmptyColumnChange,
        SyncPlanWarningKind.ConstraintRebuildAroundRowDependentChange,
        SyncPlanWarningKind.UniqueConstraintRebuildKeptForForeignKey,
    ];

    /// <summary>規則が見つけた警告を順に積む</summary>
    /// <param name="warnings">積む先</param>
    /// <param name="found">規則が返した警告</param>
    /// <remarks>
    /// 重ねるかは種別で決まる。重ねない種別（<see cref="SyncPlanWarningKind.SetNullOnNotNullColumn"/>・
    /// <see cref="SyncPlanWarningKind.ForeignKeyColumnTypeMismatch"/>・
    /// <see cref="SyncPlanWarningKind.ConstraintRebuildAroundEmptyColumnChange"/>・
    /// <see cref="SyncPlanWarningKind.ConstraintRebuildAroundRowDependentChange"/>・
    /// <see cref="SyncPlanWarningKind.UniqueConstraintRebuildKeptForForeignKey"/>）は、同じ値の警告が、どの規則が積んだものでも
    /// 既に在れば積まない。ほかの種別はそのまま積む（同じ値が 2 件並ぶことがある）。
    /// </remarks>
    public static void Add(List<SyncPlanWarning> warnings, IEnumerable<SyncPlanWarning> found)
    {
        foreach (var warning in found)
        {
            if (NonRepeatingKinds.Contains(warning.Kind) && warnings.Contains(warning))
            {
                continue;
            }

            warnings.Add(warning);
        }
    }
}
