using System.Collections.Generic;
using QuickER.Model;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Assessment;

internal static partial class AssessmentRules
{
    /// <summary>
    /// 既存の表の再構築が落とす属性（意味モデルの持たない列の属性・制約の句・テーブルオプション）を、DB の今の <c>CREATE TABLE</c> の原文から
    /// 見つけて、<see cref="SyncPlanWarningKind.TableRebuildDropsColumnAttribute"/> で名指しする
    /// </summary>
    /// <param name="assessment">計画の事実と、組み立ての結果（再構築の文）</param>
    /// <returns>名指しの警告（組み立てた文の順。1 つの再構築につき多くて 1 件。表は再構築の表名・Detail は落ちる属性の並び）</returns>
    /// <remarks>
    /// <para>
    /// 組み立てた文のうち、表を作るだけでない再構築の文（<see cref="TableRebuildPlan.CreateOnly"/> が偽）だけを見る。表を作るだけの再構築は
    /// 落とす元の定義を持たない。検出は <see cref="TableRebuildAttributeDetector.Detect"/>。
    /// </para>
    /// <para>
    /// 計画の入力が無いか、その表の原文（<see cref="SyncPlanContext.TableCreateSql"/>）が無いか、落ちる属性が無ければ返さない。原文を持つのは
    /// テーブル再構築の方言の取込だけなので、ほかの方言の計画では何も返さない（再構築の文も無い）。
    /// </para>
    /// </remarks>
    public static IEnumerable<SyncPlanWarning> WarnTableRebuildsDroppingColumnAttributes(
        PlanAssessment assessment
    )
    {
        var context = assessment.Context;
        var warnings = new List<SyncPlanWarning>();

        if (context is null || context.TableCreateSql.Count == 0)
        {
            return warnings;
        }

        foreach (var step in assessment.Schedule.Steps ?? [])
        {
            var rebuild = (step as RebuildTableStep)?.Rebuild;

            if (rebuild is null || rebuild.CreateOnly)
            {
                continue;
            }

            var createSql = context.TableCreateSql.GetValueOrDefault(
                SchemaNames.ToStoredForm(rebuild.TableName)
            );

            if (createSql is null)
            {
                continue;
            }

            var lost = TableRebuildAttributeDetector.Detect(createSql);

            if (lost.Count == 0)
            {
                continue;
            }

            warnings.Add(
                new SyncPlanWarning(
                    SyncPlanWarningKind.TableRebuildDropsColumnAttribute,
                    rebuild.TableName,
                    string.Join(", ", lost)
                )
            );
        }

        return warnings;
    }
}
