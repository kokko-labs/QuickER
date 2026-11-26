using System.Collections.Generic;
using QuickER.Model;

namespace QuickER.Provider.Sync.Assessment;

internal static partial class AssessmentRules
{
    /// <summary>
    /// 選んだ説明の項目のうち、方言が保存できない文字（BMP の外の文字）を書くものを
    /// <see cref="SyncPlanWarningKind.DescriptionCharactersReplaced"/> で表・列ごとに名指しする
    /// （判定は <see cref="SyncDialectCapabilities.IdentifierLengthLimit"/> の宣言＝MySQL だけ置き換わる）。
    /// </summary>
    /// <param name="facts">計画の事実（<see cref="AssessmentFacts.PostSync"/> の項目の並び＝選んだ項目の最終形・<see cref="AssessmentFacts.Capabilities"/>）</param>
    /// <returns>名指しの警告</returns>
    public static IEnumerable<SyncPlanWarning> WarnDescriptionCharactersReplaced(
        AssessmentFacts facts
    )
    {
        var selected = facts.PostSync.Items;
        var capabilities = facts.Capabilities;
        var warnings = new List<SyncPlanWarning>();
        var limit = capabilities.IdentifierLengthLimit;

        foreach (var item in selected)
        {
            var target = item.Kind switch
            {
                SchemaDiffKind.SetTableDescription => item.TableName,
                SchemaDiffKind.SetColumnDescription when item.ColumnName is not null =>
                    $"{item.TableName}.{item.ColumnName}",
                _ => null,
            };

            if (
                target is null
                || !DescriptionText.IsPresent(item.NewDescription)
                || limit.CanRepresent(item.NewDescription)
            )
            {
                continue;
            }

            warnings.Add(
                new SyncPlanWarning(
                    SyncPlanWarningKind.DescriptionCharactersReplaced,
                    item.TableName,
                    target
                )
            );
        }

        return warnings;
    }
}
