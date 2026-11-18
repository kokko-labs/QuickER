using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider.Sync.State;

namespace QuickER.Provider.Sync.Selection;

internal static partial class SelectionRules
{
    /// <summary>
    /// 一意制約の名前が、同期後も残る DB の制約と方言の名前の範囲で重なる表の追加（<see cref="SchemaDiffKind.AddTable"/>）を、その制約の削除を
    /// 前提にする項目として計画から外し、<see cref="SyncPlanWarningKind.SelectedItemRequiresUnselectedItem"/> で名指しする
    /// </summary>
    /// <remarks>
    /// 判定は外部キー・一意制約の追加と同じ（<see cref="RemainingLiveConstraints"/>・<see cref="SyncDialectCapabilities.ConstraintNamesCollide"/>）。
    /// 表の名前を変えて古い表の一意制約の名前を新しい表が引き継ぐ図を、既定の選び方（表の追加を選び、古い表の削除を選ばない）で流すと、
    /// 新しい表の作成が名前の重なりで断られる。主キーと同じ列の一意制約を持てない方言で、表の作成が省く一意制約は数えない。主キーの名前は
    /// 取り込まないので数えない。依存の除外（<see cref="ExcludeItemsRequiringUnselected"/>）より前に置き、外した表の追加を前提にする項目
    /// （その表の外部キー・説明）はそこで外す（列の無い表の追加の除外と同じ位置）。Detail は「一意制約の名前 (残る制約の表.名前)」。
    /// </remarks>
    private static IReadOnlyList<Withdrawal> ExcludeTablesWithCollidingConstraintNames(
        SelectionState state,
        SelectionContext context
    )
    {
        var selected = state.Items;
        var capabilities = context.Capabilities;

        if (
            context.LiveSchema is null
            || capabilities.ConstraintNameScope == ConstraintNameScope.None
        )
        {
            return [];
        }

        var limit = capabilities.IdentifierLengthLimit;
        var remaining = state.RemainingConstraints;
        var withdrawals = new List<Withdrawal>();

        foreach (
            var add in selected.Where(i =>
                i.Kind == SchemaDiffKind.AddTable && i.Entity is not null
            )
        )
        {
            var table = SchemaNames.ToStoredForm(add.TableName);
            var primaryKey = add.Entity!.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).ToList();
            var collisions = UniqueConstraintNaming
                .ResolveAll(add.Entity, add.UniqueConstraintNames, limit)
                .Where(u =>
                    capabilities.SupportsUniqueConstraintOnPrimaryKey
                    || !SyncPlanner.IsSameColumnSet(primaryKey, u.ColumnNames)
                )
                .Select(u =>
                    (
                        u.Name,
                        Remaining: remaining
                            .NamedLike(u.Name, table, isForeignKey: false, capabilities)
                            .ToList()
                    )
                )
                .Where(c => c.Remaining.Count > 0)
                .ToList();

            if (collisions.Count == 0)
            {
                continue;
            }

            var warning = new SyncPlanWarning(
                SyncPlanWarningKind.SelectedItemRequiresUnselectedItem,
                table,
                string.Join(
                    ", ",
                    collisions.Select(c => $"{c.Name} ({string.Join(", ", c.Remaining)})")
                )
            );
            withdrawals.Add(new Withdrawal(add, [warning]));
        }

        return withdrawals;
    }
}
