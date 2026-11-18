using System.Collections.Generic;

namespace QuickER.Provider.Sync.Steps;

/// <summary>
/// 文の並びで表した同期の計画
/// </summary>
/// <param name="Steps">
/// 文の並び。先頭の <paramref name="MovedFirstCount"/> 個が前寄せした文で、その後ろに、種類の固定の順に並べた文、列を動かす文、
/// 表を作り直す文が続く
/// </param>
/// <param name="MovedFirstCount">
/// 必ず断られる文として計画の先頭へ移した文の数（DDL が暗黙コミットされる方言の前寄せ。無ければ 0）
/// </param>
/// <param name="Warnings">計画を実行する前に利用者へ伝える警告</param>
/// <param name="ExcludedItems">利用者が選んだのに計画が外した項目</param>
internal sealed record StepPlan(
    IReadOnlyList<SyncStep> Steps,
    int MovedFirstCount,
    IReadOnlyList<SyncPlanWarning> Warnings,
    IReadOnlyList<SchemaDiffItem> ExcludedItems
);
