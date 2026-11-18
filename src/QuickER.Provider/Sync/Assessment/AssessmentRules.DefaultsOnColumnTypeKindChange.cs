using System.Collections.Generic;
using System.Linq;
using QuickER.Model;

namespace QuickER.Provider.Sync.Assessment;

internal static partial class AssessmentRules
{
    /// <summary>
    /// 選んだ列の変更のうち、取込で既定値の分かった列の型の種類（方言の正規化を通した、括弧より前の基本の名前）を
    /// 変えるものを <see cref="SyncPlanWarningKind.DefaultOnColumnTypeKindChange"/> で名指しする
    /// （<see cref="QuickER.Dialects.SyncDialectCapabilities.ChecksDefaultOnColumnTypeChange"/> が <c>true</c> の方言のみ）。
    /// </summary>
    /// <param name="facts">
    /// 計画の事実（<see cref="AssessmentFacts.PostSync"/> の項目の並び＝選んだ項目の最終形・<see cref="AssessmentFacts.Capabilities"/>・
    /// <see cref="AssessmentFacts.Context"/>＝取込で分かった列の既定値）
    /// </param>
    /// <returns>名指しの警告</returns>
    public static IEnumerable<SyncPlanWarning> WarnDefaultsOnColumnTypeKindChange(
        AssessmentFacts facts
    )
    {
        var selected = facts.PostSync.Items;
        var capabilities = facts.Capabilities;
        var context = facts.Context;
        var warnings = new List<SyncPlanWarning>();

        if (
            !capabilities.ChecksDefaultOnColumnTypeChange
            || context is null
            || context.ColumnDefaults.Count == 0
        )
        {
            return warnings;
        }

        foreach (
            var alter in selected.Where(i =>
                i.Kind == SchemaDiffKind.AlterColumn
                && i.ColumnName is not null
                && i.Column is not null
                && i.OldColumn is not null
            )
        )
        {
            var oldType = alter.OldColumn!.DataType;
            var newType = alter.Column!.DataType;

            if (
                !capabilities.ChangesTypeKind(oldType, newType)
                || !context.ColumnDefaults.Any(d =>
                    SchemaNames.AreSame(d.TableName, alter.TableName)
                    && SchemaNames.AreSame(d.ColumnName, alter.ColumnName!)
                )
            )
            {
                continue;
            }

            warnings.Add(
                new SyncPlanWarning(
                    SyncPlanWarningKind.DefaultOnColumnTypeKindChange,
                    SchemaNames.ToStoredForm(alter.TableName),
                    $"{alter.ColumnName} ({oldType} → {newType})"
                )
            );
        }

        return warnings;
    }
}
