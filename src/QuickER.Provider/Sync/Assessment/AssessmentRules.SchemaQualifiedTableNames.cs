using System.Collections.Generic;
using QuickER.Dialects;
using QuickER.Model;

namespace QuickER.Provider.Sync.Assessment;

internal static partial class AssessmentRules
{
    /// <summary>
    /// 選んだ項目が触れる表（項目の表と、外部キーの親子の表）のうち、ドットを含む名前のものを
    /// <see cref="SyncPlanWarningKind.SchemaQualifiedTableName"/> で表ごとに 1 件名指しする
    /// （<see cref="SyncDialectCapabilities.SupportsSchemaQualifiedTableNames"/> が <c>false</c> の方言のみ）。
    /// </summary>
    /// <param name="facts">計画の事実（<see cref="AssessmentFacts.PostSync"/> の項目の並び＝選んだ項目の最終形・<see cref="AssessmentFacts.Capabilities"/>）</param>
    /// <returns>名指しの警告</returns>
    public static IEnumerable<SyncPlanWarning> WarnSchemaQualifiedTableNames(AssessmentFacts facts)
    {
        var selected = facts.PostSync.Items;
        var capabilities = facts.Capabilities;
        var warnings = new List<SyncPlanWarning>();

        if (capabilities.SupportsSchemaQualifiedTableNames)
        {
            return warnings;
        }

        var seen = new HashSet<string>(SchemaNames.Comparer);

        foreach (var item in selected)
        {
            foreach (
                var name in new[]
                {
                    item.TableName,
                    item.ChildEntity?.TableName,
                    item.ParentEntity?.TableName,
                }
            )
            {
                if (name is null || !SchemaQualifiedName.IsQualified(name))
                {
                    continue;
                }

                var table = SchemaNames.ToStoredForm(name);

                if (seen.Add(table))
                {
                    warnings.Add(
                        new SyncPlanWarning(
                            SyncPlanWarningKind.SchemaQualifiedTableName,
                            table,
                            string.Empty
                        )
                    );
                }
            }
        }

        return warnings;
    }
}
