using System.Collections.Generic;

namespace QuickER.Provider.Sync.Selection;

/// <summary>
/// 計画が選んだ項目のうち、計画に載せられない項目を見つけて、取り下げとして返す規則
/// </summary>
/// <param name="state">
/// いま選んでいる項目の並び（前の規則の取り下げを当てた後の並び）と、その並びの同期後の事実
/// </param>
/// <param name="context">選んだ項目に依らない文脈</param>
/// <returns>
/// 取り下げの並び（当てる順。外す項目が無ければ空）。警告の並びと外した項目の並びは、この並びで決まる
/// </returns>
/// <remarks>
/// 規則は受け取った並びも警告・外した項目の一覧も書き換えない。取り下げを当てるのは除外の周（<see cref="PlanSelection.ExcludeUntilStable"/>）が
/// <see cref="SelectionWithdrawals.Apply"/> で行い、後ろの規則は当てた後の並びとその並びから作り直した状態を受ける。同期後の事実は
/// 状態（<see cref="SelectionState"/>）から読み、規則が自分で求め直さない。規則は項目を外すだけで戻さず、外す項目は
/// 必ず名指しの警告と一緒に返す（項目を外さずに警告だけを返さない）。規則の並びは規則の表（<see cref="SelectionRules.BeforeRounds"/>・<see cref="SelectionRules.EachRound"/>）が決める。
/// </remarks>
internal delegate IReadOnlyList<Withdrawal> SelectionRule(
    SelectionState state,
    SelectionContext context
);
