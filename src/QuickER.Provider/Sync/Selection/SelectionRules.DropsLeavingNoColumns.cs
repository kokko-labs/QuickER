using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider.Sync.State;

namespace QuickER.Provider.Sync.Selection;

internal static partial class SelectionRules
{
    /// <summary>
    /// 選んだ列の削除（<see cref="SchemaDiffKind.DropColumn"/>）で、同期後に残る表の列が 1 本も無くなる表について、その表の選んだ列の
    /// 削除をすべて計画から外し、<see cref="SyncPlanWarningKind.TableLeftWithoutColumns"/> で名指しする（方言に依らない）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 同期後の列の数は「DB の列 − 選んだ列の削除 ＋ 選んだ列の追加」で数える（削除を選んだ表は数えない＝表ごと消える）。
    /// 表の列をすべて消す変更は、どの方言でも正しく扱えない：SQL Server・MySQL・Oracle は最後の列の削除を断り、DDL が暗黙コミット
    /// される MySQL・Oracle ではそれより前の文（一意制約・主キー・ほかの列の削除）が残る。PostgreSQL は列の無い表を作り、取り込み
    /// 直すと列を読めない表として警告になる。SQLite は列の無い再構築の表を作れない。
    /// </para>
    /// <para>
    /// 外すのは列の削除だけで、同じ表の主キーの解除・一意制約の削除は単独で正しいので残す。依存の除外
    /// （<see cref="ExcludeItemsRequiringUnselected"/>）の後で呼び、列の削除に伴う暗黙の外部キー・一意制約の削除を注入する段より前に置く
    /// （外した列を前提にした暗黙の削除を足さない）。
    /// </para>
    /// </remarks>
    private static IReadOnlyList<Withdrawal> ExcludeDropsLeavingNoColumns(
        SelectionState state,
        SelectionContext context
    )
    {
        var selected = state.Items;
        var liveSchema = context.LiveSchema;

        if (liveSchema is null)
        {
            return [];
        }

        var liveByName = liveSchema.Tables;
        var droppedTables = selected
            .Where(i => i.Kind == SchemaDiffKind.DropTable)
            .Select(i => SchemaNames.ToStoredForm(i.TableName))
            .ToHashSet(SchemaNames.Comparer);
        var tablesWithAddedColumns = selected
            .Where(i => i.Kind == SchemaDiffKind.AddColumn)
            .Select(i => SchemaNames.ToStoredForm(i.TableName))
            .ToHashSet(SchemaNames.Comparer);
        var emptied = new HashSet<string>(SchemaNames.Comparer);

        foreach (
            var drops in selected
                .Where(i => i.Kind == SchemaDiffKind.DropColumn && i.ColumnName is not null)
                .GroupBy(i => SchemaNames.ToStoredForm(i.TableName), SchemaNames.Comparer)
        )
        {
            if (droppedTables.Contains(drops.Key) || tablesWithAddedColumns.Contains(drops.Key))
            {
                continue;
            }

            if (!liveByName.TryGet(drops.Key, out var live))
            {
                continue;
            }

            var remains = live.Columns.Any(c =>
                !drops.Any(d => SchemaNames.AreSame(d.ColumnName!, c.Name))
            );

            if (!remains)
            {
                emptied.Add(drops.Key);
            }
        }

        if (emptied.Count == 0)
        {
            return [];
        }

        bool IsExcluded(SchemaDiffItem item) =>
            item.Kind == SchemaDiffKind.DropColumn
            && emptied.Contains(SchemaNames.ToStoredForm(item.TableName));

        var withdrawals = new List<Withdrawal>();

        foreach (
            var drops in selected
                .Where(IsExcluded)
                .GroupBy(i => SchemaNames.ToStoredForm(i.TableName), SchemaNames.Comparer)
        )
        {
            var warning = new SyncPlanWarning(
                SyncPlanWarningKind.TableLeftWithoutColumns,
                drops.Key,
                string.Join(", ", drops.Select(d => d.ColumnName))
            );

            foreach (var drop in drops)
            {
                withdrawals.Add(new Withdrawal(drop, [warning]));
            }
        }

        return withdrawals;
    }
}
