using System.Collections.Generic;
using System.Linq;
using QuickER.Model;

namespace QuickER.Provider.Sync.Selection;

internal static partial class SelectionRules
{
    /// <summary>
    /// 列の無い表の追加（<see cref="SchemaDiffKind.AddTable"/>）を計画から外し、<see cref="SyncPlanWarningKind.TableWithoutColumns"/>
    /// で名指しする（方言に依らない）。
    /// </summary>
    /// <remarks>
    /// 列の無い表は <c>CREATE TABLE t ( );</c> になり、どの方言でも正しく作れない（判定と理由は <see cref="SqlTableShape"/>）。
    /// 図からの DDL は同じ判定で図全体を止めるが、同期は表の追加 1 つを外せばほかの変更を実行できるので、隣の
    /// <see cref="ExcludeDropsLeavingNoColumns"/> と同じく外して名指しする。依存の除外（<see cref="ExcludeItemsRequiringUnselected"/>）
    /// より前に置き、外した表の追加を前提にする項目はそこで外す。
    /// </remarks>
    private static IReadOnlyList<Withdrawal> ExcludeTablesWithoutColumns(
        SelectionState state,
        SelectionContext context
    )
    {
        var withdrawals = new List<Withdrawal>();

        foreach (var add in state.Items.Where(SqlTableShape.IsAddTableWithoutColumns))
        {
            var warning = new SyncPlanWarning(
                SyncPlanWarningKind.TableWithoutColumns,
                SchemaNames.ToStoredForm(add.TableName),
                string.Empty
            );
            withdrawals.Add(new Withdrawal(add, [warning]));
        }

        return withdrawals;
    }
}
