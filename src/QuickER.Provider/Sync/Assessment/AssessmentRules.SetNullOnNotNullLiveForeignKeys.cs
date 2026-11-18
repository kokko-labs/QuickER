using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider.Sync.State;

namespace QuickER.Provider.Sync.Assessment;

internal static partial class AssessmentRules
{
    /// <summary>
    /// 選んだ列の変更（主キーの変更に伴う暗黙の NOT NULL 化を含む）で、DB にある参照アクション <c>SET NULL</c> の外部キーの
    /// 子の列が NOT NULL になるとき、その外部キーを <see cref="SyncPlanWarningKind.SetNullOnNotNullColumn"/> で名指しする
    /// （明示的に外す外部キーは除く）。方言に依らない（作れる方言でも、参照される親の行の削除・更新が失敗する）。
    /// </summary>
    /// <param name="facts">計画の事実（<see cref="AssessmentFacts.PostSync"/>・<see cref="AssessmentFacts.Live"/>・<see cref="AssessmentFacts.Capabilities"/>）</param>
    /// <returns>名指しの警告</returns>
    /// <remarks>
    /// 名指しするのは、この同期で NOT NULL になる子の列があるときだけ（live の時点で既に NOT NULL の子の列を持つ外部キーは、
    /// 同期が変えるものではない）。列は同期後に NOT NULL の子の列をすべて並べる。作れない方言で外し直さないのは
    /// <see cref="SyncPlanner.InjectImplicitForeignKeyRebuilds"/>。
    /// </remarks>
    public static IEnumerable<SyncPlanWarning> WarnSetNullOnNotNullLiveForeignKeys(
        AssessmentFacts facts
    )
    {
        var postSync = facts.PostSync;
        var capabilities = facts.Capabilities;
        var liveSchema = facts.Live;
        var warnings = new List<SyncPlanWarning>();

        if (liveSchema is null)
        {
            return warnings;
        }

        var limit = capabilities.IdentifierLengthLimit;
        var explicitlyDropped = postSync.DroppedForeignKeys;
        Func<Entity, string, string, bool> postSyncNullable = postSync.AllowsNull;

        foreach (var fk in liveSchema.ForeignKeys)
        {
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            if (
                !SyncPlanner.UsesSetNull(fk.Relationship, capabilities)
                || explicitlyDropped.Contains(
                    SyncPlanner.ForeignKeyKey(
                        childTable,
                        parentTable,
                        fk.ColumnPairs,
                        fk.Relationship,
                        limit
                    )
                )
            )
            {
                continue;
            }

            // この同期で NOT NULL になる子の列（live では NULL を許す列）
            var madeNotNull = fk.ColumnPairs.Any(p =>
                fk.Child.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, p.ChildColumn))
                    is { } childColumn
                && ColumnNullability.AllowsNull(childColumn)
                && !postSyncNullable(fk.Child, childTable, p.ChildColumn)
            );

            if (madeNotNull)
            {
                warnings.Add(
                    SyncPlanner.SetNullWarning(
                        childTable,
                        SyncPlanner.ResolveForeignKeyName(
                            fk.Relationship,
                            childTable,
                            parentTable,
                            limit,
                            liveSchema
                        ),
                        SyncPlanner.PostSyncNotNullChildColumns(fk, childTable, postSyncNullable)
                    )
                );
            }
        }

        return warnings;
    }
}
