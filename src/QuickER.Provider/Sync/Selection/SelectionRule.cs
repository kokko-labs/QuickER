using System.Collections.Generic;

namespace QuickER.Provider.Sync.Selection;

/// <summary>
/// 計画が選んだ項目のうち、計画に載せられない項目を見つけて、取り下げとして返す規則
/// </summary>
/// <param name="selected">いま選んでいる項目の並び（前の規則の取り下げを当てた後の並び）</param>
/// <param name="context">選んだ項目に依らない文脈</param>
/// <returns>
/// 取り下げの並び（当てる順。外す項目が無ければ空）。警告の並びと外した項目の並びは、この並びで決まる
/// </returns>
/// <remarks>
/// 規則は受け取った並びも警告・外した項目の一覧も書き換えない。取り下げを当てるのは周（<see cref="SyncPlanner"/> の除外の周）が
/// <see cref="SelectionWithdrawals.Apply"/> で行い、後ろの規則は当てた後の並びを受ける。規則は項目を外すだけで戻さず、外す項目は
/// 必ず名指しの警告と一緒に返す（項目を外さずに警告だけを返さない）。規則の並びは <see cref="SyncPlanner"/> の規則の表が決める。
/// </remarks>
internal delegate IReadOnlyList<Withdrawal> SelectionRule(
    IReadOnlyList<SchemaDiffItem> selected,
    SelectionContext context
);
