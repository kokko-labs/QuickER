using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider.Sync.State;

namespace QuickER.Provider.Sync.Selection;

internal static partial class SelectionRules
{
    /// <summary>
    /// <see cref="SyncDialectCapabilities.SetNullRequiresNullableChildColumn"/> が <c>true</c> の方言（SQL Server・MySQL）で、
    /// 選んだ外部キーの追加・作り直しのうち、参照アクション <c>SET NULL</c> を使い、子の列が同期後に NOT NULL になるものを
    /// 計画から外し、<see cref="SyncPlanWarningKind.SetNullOnNotNullColumn"/> で名指しする（作り直しは展開前の 1 項目として
    /// 外すので、削除だけが走って外部キーが消えたまま残らない）。ほかの方言では何も外さない
    /// （外さずに名指しするのは <see cref="Assessment.AssessmentRules.WarnSetNullForeignKeysOnNotNullColumns"/>）。
    /// </summary>
    /// <param name="state">選択済みの項目（外部キーの作り直しは展開前）とその同期後の事実</param>
    /// <param name="context">
    /// 文脈（方言が書く参照アクションと作れるかの宣言・live スキーマ。live スキーマが無ければ図の値で見る）
    /// </param>
    /// <returns>取り下げ（項目の並びの順）</returns>
    /// <remarks>判定は <see cref="FindSetNullForeignKeysOnNotNullColumns"/>。</remarks>
    private static IReadOnlyList<Withdrawal> ExcludeSetNullForeignKeysOnNotNullColumns(
        SelectionState state,
        SelectionContext context
    )
    {
        var capabilities = context.Capabilities;

        if (!capabilities.SetNullRequiresNullableChildColumn)
        {
            return [];
        }

        return
        [
            .. FindSetNullForeignKeysOnNotNullColumns(state, capabilities)
                .Select(found => new Withdrawal(
                    found.Item,
                    [
                        PlanFacts.SetNullWarning(
                            found.ChildTable,
                            found.ConstraintName,
                            found.NotNullColumns
                        ),
                    ]
                )),
        ];
    }

    /// <summary>
    /// 選んだ外部キーの追加・作り直しのうち、参照アクション <c>SET NULL</c> を使い、子の列が同期後に NOT NULL になるものを、
    /// 項目の並びの順に返す（子の表・スクリプトが付ける名前・NOT NULL になる子の列つき）。
    /// </summary>
    /// <remarks>
    /// 子の列の同期後の NULL 許容は、選んだ主キーの変更で主キーに入る列は NOT NULL、選んだ列の変更があればその値、
    /// live に在る列は live の値、live に無い列（追加する表・列）は図の値（<see cref="PostSyncSchema.AllowsNull"/>）。
    /// 参照アクションは方言が書くもの（<see cref="SyncDialectCapabilities.EffectiveOnDelete"/> /
    /// <see cref="SyncDialectCapabilities.EffectiveOnUpdate"/>）で見る＝ON UPDATE を持たない方言（Oracle）の ON UPDATE は数えない。
    /// </remarks>
    internal static List<(
        SchemaDiffItem Item,
        string ChildTable,
        string ConstraintName,
        List<string> NotNullColumns
    )> FindSetNullForeignKeysOnNotNullColumns(
        SelectionState state,
        SyncDialectCapabilities capabilities
    )
    {
        var selected = state.Items;
        Func<Entity, string, string, bool> postSyncNullable = state.PostSync.AllowsNull;
        var found =
            new List<(
                SchemaDiffItem Item,
                string ChildTable,
                string ConstraintName,
                List<string> NotNullColumns
            )>();

        foreach (var item in selected)
        {
            if (
                item.Kind is not (SchemaDiffKind.AddForeignKey or SchemaDiffKind.AlterForeignKey)
                || item.Relationship is null
                || item.ChildEntity is null
                || item.ParentEntity is null
                || !PlanFacts.UsesSetNull(item.Relationship, capabilities)
            )
            {
                continue;
            }

            var child = item.ChildEntity;
            var childTable = SchemaDiffService.NormalizeTable(child);
            var notNull = ForeignKeyColumnPairResolver
                .ChildColumns(SyncScriptBuilderHelper.ResolveColumnPairs(item))
                .Where(column => !postSyncNullable(child, childTable, column))
                .ToList();

            if (notNull.Count > 0)
            {
                found.Add(
                    (
                        item,
                        childTable,
                        PlanIdentity.SelectedForeignKeyName(
                            item,
                            capabilities.IdentifierLengthLimit
                        ),
                        notNull
                    )
                );
            }
        }

        return found;
    }
}
