using System;
using System.Collections.Generic;

namespace QuickER.Provider.Sync.Selection;

/// <summary>規則が返した取り下げ（<see cref="Withdrawal"/>）を、項目の並び・警告・外した項目の一覧へ当てる</summary>
internal static class SelectionWithdrawals
{
    /// <summary>取り下げを当てて、次の規則へ渡す項目の並びを作る</summary>
    /// <param name="current">規則が受け取った項目の並び</param>
    /// <param name="withdrawals">規則が返した取り下げ（当てる順）</param>
    /// <param name="warnings">名指しの警告を積む先</param>
    /// <param name="excluded">外した項目を積む先</param>
    /// <returns>
    /// 外した項目を落とし、代わりの項目をその位置へ入れた並び（元の順を保つ）。取り下げが無ければ <paramref name="current"/> そのもの
    /// </returns>
    /// <remarks>
    /// <para>
    /// 取り下げの順に、その取り下げの警告を並びの順に積み（同じ値の警告が既に積まれていれば積まない）、項目を外した項目の一覧へ積む。
    /// 警告の並びと外した項目の並びは、取り下げの並びで決まる。
    /// </para>
    /// <para>
    /// 並びからは、外す項目と参照が同じ要素をすべて落とす。代わりの項目は、同じ項目の取り下げの順に、その項目の出現の位置へ 1 つずつ入れる。
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">警告の無い取り下げがある（外した項目は必ず名指しする）</exception>
    public static List<SchemaDiffItem> Apply(
        List<SchemaDiffItem> current,
        IReadOnlyList<Withdrawal> withdrawals,
        List<SyncPlanWarning> warnings,
        List<SchemaDiffItem> excluded
    )
    {
        if (withdrawals.Count == 0)
        {
            return current;
        }

        var removed = new HashSet<SchemaDiffItem>(ReferenceEqualityComparer.Instance);
        var replacements = new Dictionary<SchemaDiffItem, Queue<SchemaDiffItem>>(
            ReferenceEqualityComparer.Instance
        );

        foreach (var withdrawal in withdrawals)
        {
            if (withdrawal.Warnings.Count == 0)
            {
                throw new InvalidOperationException(
                    "A withdrawn item must be named by at least one warning."
                );
            }

            foreach (var warning in withdrawal.Warnings)
            {
                if (!warnings.Contains(warning))
                {
                    warnings.Add(warning);
                }
            }

            excluded.Add(withdrawal.Item);
            removed.Add(withdrawal.Item);

            if (withdrawal.Replacement is null)
            {
                continue;
            }

            if (!replacements.TryGetValue(withdrawal.Item, out var queue))
            {
                queue = new Queue<SchemaDiffItem>();
                replacements[withdrawal.Item] = queue;
            }

            queue.Enqueue(withdrawal.Replacement);
        }

        var next = new List<SchemaDiffItem>(current.Count);

        foreach (var item in current)
        {
            if (!removed.Contains(item))
            {
                next.Add(item);
                continue;
            }

            if (replacements.TryGetValue(item, out var queue) && queue.Count > 0)
            {
                next.Add(queue.Dequeue());
            }
        }

        return next;
    }
}
