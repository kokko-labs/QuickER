using System.Collections.Generic;

namespace QuickER.Provider.Sync.Selection;

/// <summary>
/// 選んだ項目を外す規則（<see cref="SelectionRule"/>）が返す取り下げ 1 件
/// </summary>
/// <param name="Item">計画から外す選んだ項目（外した項目の一覧へ積むもの）</param>
/// <param name="Warnings">
/// 外した項目を名指しする警告の並び（積む順。1 件以上。同じ値の警告が既に積まれていれば積まない）
/// </param>
/// <param name="Replacement">
/// 外した項目の位置に代わりに残す項目（無ければ <c>null</c>。型の変更も含む列の変更から NULL 許容化だけを外した項目など）
/// </param>
/// <remarks>当て方は <see cref="SelectionWithdrawals.Apply"/> の 1 か所。</remarks>
internal sealed record Withdrawal(
    SchemaDiffItem Item,
    IReadOnlyList<SyncPlanWarning> Warnings,
    SchemaDiffItem? Replacement = null
);
