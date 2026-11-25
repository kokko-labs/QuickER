using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider.Sync.State;

namespace QuickER.Provider.Sync.Assessment;

internal static partial class AssessmentRules
{
    /// <summary>
    /// 選んだ外部キーの追加（作り直しは展開後の追加）のうち、参照アクション <c>SET DEFAULT</c> を使い、子の列に NOT NULL の
    /// 列があるものを <see cref="SyncPlanWarningKind.SetDefaultOnNotNullColumn"/> で名指しする
    /// （<see cref="QuickER.Dialects.SyncDialectCapabilities.SetDefaultRequiresColumnDefault"/> が <c>true</c> の方言のみ）。
    /// </summary>
    /// <param name="facts">
    /// 計画の事実（<see cref="AssessmentFacts.PostSync"/>＝展開と暗黙の項目の注入を終えた項目〔主キーの変更が足す暗黙の NOT NULL 化を含む〕の
    /// 同期後の事実・<see cref="AssessmentFacts.Capabilities"/>）
    /// </param>
    /// <returns>名指しの警告</returns>
    /// <remarks>
    /// 子の列の NULL 許容は同期後の値で見る（<see cref="PostSyncSchema.AllowsNull"/>＝選んだ主キーの変更で主キーに入る列は NOT NULL、
    /// 選んだ列の変更があればその値、live に在る列は live の値、live に無い列は図の値）。図の値で見ると、NULL 許容化を選ばずに
    /// 外部キーだけを足す同期で、DB では NOT NULL のままの列を見逃す。主キーの変更などに伴う暗黙の付け直しは対象外
    /// （live に既にある外部キーなので、DB の列は DEFAULT 制約を持っている）。
    /// </remarks>
    public static IEnumerable<SyncPlanWarning> WarnSetDefaultOnNotNullColumns(AssessmentFacts facts)
    {
        var postSync = facts.PostSync;
        var capabilities = facts.Capabilities;
        var warnings = new List<SyncPlanWarning>();

        if (!capabilities.SetDefaultRequiresColumnDefault)
        {
            return warnings;
        }

        Func<Entity, string, string, bool> postSyncNullable = postSync.AllowsNull;

        foreach (
            var add in postSync.Items.Where(i =>
                i.Kind == SchemaDiffKind.AddForeignKey
                && i.Relationship is not null
                && i.ChildEntity is not null
                && i.ParentEntity is not null
                && ForeignKeyStatementNotes.UsesSetDefault(i.Relationship)
            )
        )
        {
            var child = add.ChildEntity!;
            var childTable = SchemaDiffService.NormalizeTable(child);
            var notNull = ForeignKeyColumnPairResolver
                .ChildColumns(SyncScriptBuilderHelper.ResolveColumnPairs(add))
                .Where(name => !postSyncNullable(child, childTable, name))
                .ToList();

            if (notNull.Count == 0)
            {
                continue;
            }

            var name = PlanIdentity.SelectedForeignKeyName(add, capabilities.IdentifierLengthLimit);

            warnings.Add(
                new SyncPlanWarning(
                    SyncPlanWarningKind.SetDefaultOnNotNullColumn,
                    childTable,
                    $"{name} ({string.Join(", ", notNull)})"
                )
            );
        }

        return warnings;
    }
}
