using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Assessment;

internal static partial class AssessmentRules
{
    /// <summary>
    /// 参照アクション <c>SET DEFAULT</c> の外部キーを付ける文のうち、子の列に、同期後に NOT NULL で DB の既定値の無い列があるものを
    /// <see cref="SyncPlanWarningKind.SetDefaultOnNotNullColumn"/> で名指しする
    /// （<see cref="SyncDialectCapabilities.SetDefaultRequiresColumnDefault"/> が <c>true</c> の方言のみ）。
    /// </summary>
    /// <param name="assessment">
    /// 計画の事実（<see cref="AssessmentFacts.PostSync"/>＝展開と暗黙の項目の注入を終えた項目〔主キーの変更が足す暗黙の NOT NULL 化を含む〕の
    /// 同期後の事実・<see cref="AssessmentFacts.Capabilities"/>・<see cref="AssessmentFacts.Context"/> の列の既定値）と、組み立てた文の並び
    /// </param>
    /// <returns>名指しの警告（選んだ外部キーの追加を項目の順に、続けて暗黙の付け直しを文の順に）</returns>
    /// <remarks>
    /// <para>
    /// 見る文は 2 つの腕：選んだ外部キーの追加（作り直しは展開後の追加。名前は <see cref="PlanIdentity.SelectedForeignKeyName"/>）と、
    /// 組み立てが足した外部キーの付け直し（<see cref="ImplicitOrigin"/> の <see cref="AddForeignKeyStep"/>。名前は文の名前か既定名＝
    /// <see cref="ForeignKeyStepNames.Added"/>）。付け直しは DB に既にある外部キーだが、子の列を同じ計画で NOT NULL にすると、DB は付け直しを
    /// 列の既定値が無いことで断る（SQL Server の Msg 1760）。
    /// </para>
    /// <para>
    /// 子の列の NULL 許容は同期後の値で見る（<see cref="PostSyncSchema.AllowsNull"/>＝選んだ主キーの変更で主キーに入る列は NOT NULL、
    /// 選んだ列の変更があればその値、live に在る列は live の値、live に無い列は図の値）。図の値で見ると、NULL 許容化を選ばずに
    /// 外部キーだけを足す同期で、DB では NOT NULL のままの列を見逃す。DB の列の既定値（取込の事実＝<see cref="SyncPlanContext.ColumnDefaults"/>）
    /// がある列は、DB が SET DEFAULT を受けるので数えない（両方の腕で同じ）。図は既定値を持たないので、DB に無い列は既定値の無い列として数える。
    /// </para>
    /// </remarks>
    public static IEnumerable<SyncPlanWarning> WarnSetDefaultOnNotNullColumns(
        PlanAssessment assessment
    )
    {
        var postSync = assessment.PostSync;
        var capabilities = assessment.Capabilities;
        var warnings = new List<SyncPlanWarning>();

        if (!capabilities.SetDefaultRequiresColumnDefault)
        {
            return warnings;
        }

        var defaults = assessment.Context?.ColumnDefaults ?? [];
        Func<Entity, string, string, bool> postSyncNullable = postSync.AllowsNull;

        bool CountsAsNotNullWithoutDefault(Entity child, string childTable, string column) =>
            !postSyncNullable(child, childTable, column)
            && !defaults.Any(d =>
                SchemaNames.AreSame(SchemaNames.ToStoredForm(d.TableName), childTable)
                && SchemaNames.AreSame(d.ColumnName, column)
            );

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
                .Where(name => CountsAsNotNullWithoutDefault(child, childTable, name))
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

        if (assessment.Schedule.Steps is not { } steps || assessment.Live is not { } liveSchema)
        {
            return warnings;
        }

        foreach (
            var readd in steps
                .OfType<AddForeignKeyStep>()
                .Where(s =>
                    s.Origin is ImplicitOrigin
                    && (
                        s.OnDelete == ForeignKeyReferentialAction.SetDefault
                        || s.OnUpdate == ForeignKeyReferentialAction.SetDefault
                    )
                )
        )
        {
            var childTable = SchemaNames.ToStoredForm(readd.ChildTable);

            if (!liveSchema.Tables.TryGet(childTable, out var child))
            {
                continue;
            }

            var notNull = readd
                .ColumnPairs.Select(p => p.ChildColumn)
                .Where(name => CountsAsNotNullWithoutDefault(child, childTable, name))
                .ToList();

            if (notNull.Count == 0)
            {
                continue;
            }

            warnings.Add(
                new SyncPlanWarning(
                    SyncPlanWarningKind.SetDefaultOnNotNullColumn,
                    childTable,
                    $"{ForeignKeyStepNames.Added(readd, capabilities.IdentifierLengthLimit)} ({string.Join(", ", notNull)})"
                )
            );
        }

        return warnings;
    }
}
